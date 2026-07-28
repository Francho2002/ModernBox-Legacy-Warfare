using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ModernBox
{
    /// <summary>
    /// Owns the lifetime of registered missile projectiles from launch to either
    /// a precise native impact or a safe airburst.  It deliberately does not
    /// replace WorldBox's impact implementation: targetReached remains the
    /// one place where the real projectile blast is performed.
    /// </summary>
    internal static class MissileLifecycle
    {
        private const float StallSecondsBeforeRecovery = 5.5f;
        private const float MinimumTimeoutSeconds = 30f;
        private const float MaximumTimeoutSeconds = 180f;
        private const float MinimumExpectedFlightMultiplier = 12f;

        private static readonly ConditionalWeakTable<Projectile, MissileState> States =
            new ConditionalWeakTable<Projectile, MissileState>();
        private static readonly FieldInfo CurrentPositionField =
            AccessTools.Field(typeof(Projectile), "_current_position_3d");
        private static readonly FieldInfo TargetField =
            AccessTools.Field(typeof(Projectile), "_vector_target");

        private sealed class MissileState
        {
            internal Vector2 Target;
            internal float InitialDistance;
            internal float LastDistance;
            internal Vector3 LastPosition;
            internal float Age;
            internal float StalledSeconds;
            internal bool HasTarget;
            internal bool HasSample;
            internal bool ImpactStarted;
            internal bool ImpactCompleted;
            internal bool Terminal;
        }

        internal static void CaptureLaunch(Projectile projectile)
        {
            if (projectile == null)
                return;

            States.Remove(projectile);
            MissileProfile profile;
            if (!MissileCatalog.TryGet(projectile, out profile))
                return;

            MissileState state = States.GetOrCreateValue(projectile);
            Vector2 target = projectile.getTargetVector();
            state.Target = target;
            state.HasTarget = IsValidWorldTarget(target);

            Vector2 current = projectile.getCurrentPosition();
            float distance = Vector2.Distance(current, target);
            state.InitialDistance = IsFinite(distance) ? distance : 0f;
            state.LastDistance = state.InitialDistance;
            state.LastPosition = GetCurrentPosition3D(projectile, current);
            state.HasSample = true;
        }

        internal static bool Update(Projectile projectile, float elapsed)
        {
            MissileState state;
            MissileProfile profile;
            if (projectile == null || !MissileCatalog.TryGet(projectile, out profile) ||
                !States.TryGetValue(projectile, out state))
                return true;

            if (state.Terminal)
                return false;

            float safeElapsed = Mathf.Max(0f, elapsed);
            state.Age += safeElapsed;

            Vector2 current = projectile.getCurrentPosition();
            float remainingDistance = Vector2.Distance(current, state.Target);
            if (!state.HasTarget || !IsFinite(remainingDistance) || !IsFinite(current))
            {
                Airburst(projectile, current);
                return false;
            }

            Vector3 current3D = GetCurrentPosition3D(projectile, current);
            float movedDistance = state.HasSample ? Vector3.Distance(current3D, state.LastPosition) : 0f;
            bool approachedTarget = remainingDistance + 0.08f < state.LastDistance;
            if (approachedTarget || movedDistance > 0.02f)
                state.StalledSeconds = 0f;
            else if (remainingDistance > 0.35f)
                state.StalledSeconds += safeElapsed;

            state.LastDistance = remainingDistance;
            state.LastPosition = current3D;
            state.HasSample = true;

            float speed = Mathf.Max(1f, projectile.asset.speed);
            float timeout = Mathf.Clamp(Mathf.Max(MinimumTimeoutSeconds,
                (state.InitialDistance / speed) * MinimumExpectedFlightMultiplier),
                MinimumTimeoutSeconds, MaximumTimeoutSeconds);

            // Never delete a valid warhead in flight. A stalled or otherwise
            // timed-out missile is completed through WorldBox's own impact path
            // at its immutable launch target, which keeps terrain, effects and
            // the sprite in agreement.
            if (state.StalledSeconds >= StallSecondsBeforeRecovery || state.Age >= timeout)
            {
                if (profile.Offensive)
                    ForceNativeImpact(projectile);
                else
                    Airburst(projectile, current);
                return false;
            }

            return true;
        }

        internal static bool Intercept(Projectile projectile)
        {
            MissileState state;
            MissileProfile profile;
            if (projectile == null || !MissileCatalog.TryGet(projectile, out profile) || !profile.Interceptable)
                return false;

            if (!States.TryGetValue(projectile, out state))
                state = States.GetOrCreateValue(projectile);
            if (state.Terminal)
                return false;

            state.Terminal = true;
            Vector2 airPosition;
            try
            {
                // The visual projectile position includes its altitude. An
                // interception must never render a surface explosion under a
                // flying missile or make it look as if it splashed into water.
                airPosition = projectile.getTransformedPositionWithHeight();
            }
            catch
            {
                airPosition = projectile.getCurrentPosition();
            }

            EffectsLibrary.spawnAt("fx_explosion_middle", airPosition, 0.45f);
            MusicBox.playSound("event:/SFX/EXPLOSIONS/ExplosionSmall", airPosition.x, airPosition.y, true, false);
            projectile.setState(ProjectileState.ToRemove);
            IntegratedAirDefense.Forget(projectile);
            NuclearAlertController.Forget(projectile);
            return true;
        }

        internal static void Forget(Projectile projectile)
        {
            if (projectile != null)
                States.Remove(projectile);
        }

        internal static bool BeforeNativeImpact(Projectile projectile)
        {
            MissileState state;
            MissileProfile profile;
            if (projectile == null || !MissileCatalog.TryGet(projectile, out profile) ||
                !States.TryGetValue(projectile, out state))
                return true;

            // A pooled projectile keeps its tombstone until the next start.
            // This prevents a duplicate targetReached from replaying a blast,
            // fallout or special warhead effect after it has already ended.
            if (state.ImpactStarted || state.Terminal)
                return false;

            if (!profile.Offensive)
            {
                state.Terminal = true;
                state.ImpactCompleted = true;
                // Defensive interceptor projectiles are only the visible
                // countermeasure. MissileLifecycle.Intercept owns the single
                // aerial explosion, so this projectile must not add a second
                // native flash on the water or ground at its destination.
                projectile.setState(ProjectileState.ToRemove);
                IntegratedAirDefense.Forget(projectile);
                NuclearAlertController.Forget(projectile);
                return false;
            }

            state.ImpactStarted = true;
            state.Terminal = true;
            AlignAtCapturedTarget(projectile, state);

            // One ordered impact pipeline. These used to be independent
            // targetReached patches whose ordering varied with loader state.
            IntegratedAirDefense.PlayConventionalImpactSound(projectile);
            NuclearFallout.RememberImpact(projectile);
            NavalRoles.HandleSpecialWarheadImpact(projectile);
            return true;
        }

        internal static void AfterNativeImpact(Projectile projectile)
        {
            MissileState state;
            if (projectile == null || !States.TryGetValue(projectile, out state) || state.ImpactCompleted)
                return;

            if (state.ImpactStarted)
                NuclearFallout.ApplyRememberedImpact(projectile);
            IntegratedAirDefense.Forget(projectile);
            NuclearAlertController.Forget(projectile);
            state.ImpactCompleted = true;
        }

        private static void ForceNativeImpact(Projectile projectile)
        {
            MissileState state;
            if (!States.TryGetValue(projectile, out state))
            {
                Airburst(projectile, projectile.getCurrentPosition());
                return;
            }

            if (!state.HasTarget)
            {
                Airburst(projectile, projectile.getCurrentPosition());
                return;
            }

            try
            {
                AlignAtCapturedTarget(projectile, state);
                // Native updateVelocity normally marks the projectile for
                // removal before invoking targetReached. Recovery calls the
                // impact method directly, so it must reproduce that state
                // transition or a terminal projectile would remain pooled and
                // frozen in the world.
                projectile.setState(ProjectileState.ToRemove);
                projectile.targetReached();
            }
            catch (Exception ex)
            {
                // A future game build may change targetReached. Do not leave a
                // frozen projectile in the world if that happens.
                ModernBoxLogger.Warning("[MissileLifecycle] Native impact recovery failed: " + ex.Message);
                Airburst(projectile, projectile.getCurrentPosition());
            }
        }

        private static void Airburst(Projectile projectile, Vector2 fallbackPosition)
        {
            if (projectile == null)
                return;

            MissileState state;
            if (States.TryGetValue(projectile, out state))
                state.Terminal = true;

            Vector2 position = IsFinite(fallbackPosition) ? fallbackPosition : Vector2.zero;
            try
            {
                position = projectile.getTransformedPositionWithHeight();
            }
            catch
            {
                // The already-selected fallback still produces a visible safe
                // end if a pooled projectile has lost its transform.
            }

            EffectsLibrary.spawnAt("fx_explosion_middle", position, 0.45f);
            MusicBox.playSound("event:/SFX/EXPLOSIONS/ExplosionSmall", position.x, position.y, true, false);
            projectile.setState(ProjectileState.ToRemove);
            IntegratedAirDefense.Forget(projectile);
            NuclearAlertController.Forget(projectile);
        }

        private static void AlignAtCapturedTarget(Projectile projectile, MissileState state)
        {
            if (CurrentPositionField == null || TargetField == null || !state.HasTarget)
                return;

            // z=0 is intentional: native terrain and blast lookups read the
            // projectile's world position, not its aerial sprite transform.
            CurrentPositionField.SetValue(projectile, new Vector3(state.Target.x, state.Target.y, 0f));
            TargetField.SetValue(projectile, state.Target);
        }

        private static bool IsValidWorldTarget(Vector2 target)
        {
            if (!IsFinite(target) || World.world == null)
                return false;

            return World.world.GetTile(Mathf.RoundToInt(target.x), Mathf.RoundToInt(target.y)) != null;
        }

        private static Vector3 GetCurrentPosition3D(Projectile projectile, Vector2 fallback)
        {
            if (projectile == null)
                return fallback;

            try
            {
                return new Vector3(fallback.x, fallback.y, projectile.getCurrentHeight());
            }
            catch
            {
                return fallback;
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinite(Vector2 value)
        {
            return IsFinite(value.x) && IsFinite(value.y);
        }
    }

    [HarmonyPatch(typeof(Projectile), "start")]
    internal static class MissileLifecycleStartPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Projectile __instance)
        {
            MissileLifecycle.CaptureLaunch(__instance);
        }
    }

    [HarmonyPatch(typeof(Projectile), "update")]
    internal static class MissileLifecycleUpdatePatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Projectile __instance, float pElapsed)
        {
            return MissileLifecycle.Update(__instance, pElapsed);
        }
    }

    [HarmonyPatch(typeof(Projectile), "targetReached")]
    internal static class MissileLifecycleImpactPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Projectile __instance)
        {
            return MissileLifecycle.BeforeNativeImpact(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Projectile __instance)
        {
            MissileLifecycle.AfterNativeImpact(__instance);
        }
    }
}
