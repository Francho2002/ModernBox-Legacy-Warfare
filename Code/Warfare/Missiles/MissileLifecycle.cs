using System;
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
        private static readonly ConditionalWeakTable<Projectile, MissileState> States =
            new ConditionalWeakTable<Projectile, MissileState>();
        private static readonly AccessTools.FieldRef<Projectile, Vector3> CurrentPositionRef =
            TryCreateFieldRef<Vector3>("_current_position_3d");
        private static readonly AccessTools.FieldRef<Projectile, Vector2> TargetRef =
            TryCreateFieldRef<Vector2>("_vector_target");
        private static readonly AccessTools.FieldRef<Projectile, Vector3> VelocityRef =
            TryCreateFieldRef<Vector3>("_velocity");

        private sealed class MissileState
        {
            internal Vector2 RawTarget;
            internal Vector2 ImpactPoint;
            internal Vector2 StartPoint;
            internal float StartHeight;
            internal float Distance;
            internal float HorizontalSpeed;
            internal float Travelled;
            internal float ArcHeight;
            internal bool HasTarget;
            internal bool Landed;
            internal int LandedFrame = -1;
            internal bool ImpactStarted;
            internal bool ImpactCompleted;
            internal bool ImpactSoundPlayed;
            internal bool Terminal;
        }

        internal static void CaptureLaunch(Projectile projectile)
        {
            if (projectile == null)
                return;

            MissileProfile profile;
            if (!MissileCatalog.TryGet(projectile, out profile))
                return;

            MissileState state = States.GetOrCreateValue(projectile);
            state.RawTarget = projectile.getTargetVector();
            Vector2 current = projectile.getCurrentPosition();
            Vector3 current3D = GetCurrentPosition3D(projectile, current);
            state.StartPoint = current;
            state.StartHeight = Mathf.Max(0f, current3D.z);

            WorldTile targetTile = ResolveNativeTargetTile(state.RawTarget);
            state.HasTarget = targetTile != null;
            state.ImpactPoint = state.HasTarget ? targetTile.pos : state.RawTarget;
            state.Distance = state.HasTarget
                ? Vector2.Distance(state.StartPoint, state.ImpactPoint)
                : 0f;

            Vector3 velocity = GetVelocity(projectile);
            float capturedHorizontalSpeed = new Vector2(velocity.x, velocity.y).magnitude;
            if (!IsFinite(capturedHorizontalSpeed) || capturedHorizontalSpeed < 0.1f)
                capturedHorizontalSpeed = Mathf.Max(1f, projectile.asset.speed * 0.7f);
            state.HorizontalSpeed = capturedHorizontalSpeed;
            state.ArcHeight = GetArcHeight(profile, state.Distance);

            // Every consumer, including air defense, now sees the same immutable
            // tile-centred target that the native build-719 impact will use.
            if (state.HasTarget)
                TrySetTarget(projectile, state.ImpactPoint);
        }

        internal static bool BeforeUpdate(Projectile projectile)
        {
            MissileState state;
            if (projectile == null || !States.TryGetValue(projectile, out state) || !state.Landed)
                return true;

            // This prefix runs before IAD. A missile pinned to its target is no
            // longer an airborne interception candidate: keep its final sprite
            // exact for one frame, then enter the native impact pipeline.
            TryAlignAtImpact(projectile, state);
            if (Time.frameCount > state.LandedFrame)
                CompleteNativeImpact(projectile, state);
            return false;
        }

        internal static bool UpdateVelocity(Projectile projectile, float elapsed)
        {
            MissileState state;
            MissileProfile profile;
            if (projectile == null || !MissileCatalog.TryGet(projectile, out profile) ||
                !States.TryGetValue(projectile, out state))
                return true;

            if (state.Terminal)
                return false;

            if (!state.HasTarget)
            {
                Airburst(projectile, projectile.getCurrentPosition());
                return false;
            }

            // Keep the complete missile sprite on the exact impact point for one
            // rendered frame. On the next Unity frame the native targetReached
            // pipeline performs effect, sound and terraform at that same point.
            if (state.Landed)
            {
                TryAlignAtImpact(projectile, state);
                if (Time.frameCount > state.LandedFrame)
                    CompleteNativeImpact(projectile, state);
                return false;
            }

            float safeElapsed = Mathf.Max(0f, elapsed);
            state.Travelled = Mathf.Min(state.Distance,
                state.Travelled + state.HorizontalSpeed * safeElapsed);
            float progress = state.Distance <= 0.001f
                ? 1f
                : Mathf.Clamp01(state.Travelled / state.Distance);

            Vector3 previous = GetCurrentPosition3D(projectile, state.StartPoint);
            Vector2 horizontal = Vector2.Lerp(state.StartPoint, state.ImpactPoint, progress);
            float baseHeight = Mathf.Lerp(state.StartHeight, 0f, progress);
            float arc = 4f * state.ArcHeight * progress * (1f - progress);
            Vector3 next = new Vector3(horizontal.x, horizontal.y, Mathf.Max(0f, baseHeight + arc));

            if (!TrySetGuidedPosition(projectile, previous, next, safeElapsed))
            {
                Airburst(projectile, projectile.getCurrentPosition());
                return false;
            }

            if (progress >= 1f)
            {
                state.Landed = true;
                state.LandedFrame = Time.frameCount;
                TryAlignAtImpact(projectile, state);
            }

            // The native Projectile.update continues after this skipped
            // updateVelocity call, preserving scale, trail and light updates.
            return false;
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

            // A foreign callback must never detonate an offensive missile in
            // the middle of its deterministic flight. Restore the active state
            // and let the lifecycle reach its persisted destination normally.
            if (profile.Offensive && !state.Landed)
            {
                projectile.setState(ProjectileState.Active);
                return false;
            }

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
            if (!TryAlignAtImpact(projectile, state))
            {
                state.ImpactCompleted = true;
                Airburst(projectile, projectile.getCurrentPosition());
                return false;
            }

            // One ordered impact pipeline. The sound is intentionally played
            // here, before native impact, instead of through asset.sound_impact
            // or an independent Harmony patch. That makes the report match the
            // captured blast point and guarantees a single refined sample.
            PlayImpactSound(projectile, profile, state.ImpactPoint);
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

        private static void CompleteNativeImpact(Projectile projectile, MissileState state)
        {
            if (projectile == null || state == null || state.Terminal)
                return;

            try
            {
                if (!TryAlignAtImpact(projectile, state))
                {
                    Airburst(projectile, projectile.getCurrentPosition());
                    return;
                }

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
                // Never retry a native impact that may already have emitted an
                // effect or changed the world. Exactly-once takes precedence.
                ModernBoxLogger.Warning("[MissileLifecycle] Native impact failed: " + ex.Message);
                projectile.setState(ProjectileState.ToRemove);
                if (!state.ImpactStarted)
                    Airburst(projectile, projectile.getCurrentPosition());
                else
                {
                    IntegratedAirDefense.Forget(projectile);
                    NuclearAlertController.Forget(projectile);
                }
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
            projectile.setState(ProjectileState.ToRemove);
            IntegratedAirDefense.Forget(projectile);
            NuclearAlertController.Forget(projectile);
        }

        private static bool TryAlignAtImpact(Projectile projectile, MissileState state)
        {
            if (projectile == null || state == null || CurrentPositionRef == null ||
                TargetRef == null || VelocityRef == null || !state.HasTarget)
                return false;

            try
            {
                // z=0 is intentional: native effect and terrain lookup now
                // consume one identical, tile-centred impact point.
                CurrentPositionRef(projectile) =
                    new Vector3(state.ImpactPoint.x, state.ImpactPoint.y, 0f);
                TargetRef(projectile) = state.ImpactPoint;
                VelocityRef(projectile) = Vector3.zero;
                return true;
            }
            catch (Exception ex)
            {
                ModernBoxLogger.Warning("[MissileLifecycle] Could not align missile impact: " + ex.Message);
                return false;
            }
        }

        private static void PlayImpactSound(Projectile projectile, MissileProfile profile, Vector2 position)
        {
            if (projectile == null || profile == null || string.IsNullOrEmpty(profile.ImpactSoundId))
                return;

            MissileState state;
            if (!States.TryGetValue(projectile, out state))
                state = States.GetOrCreateValue(projectile);
            if (state.ImpactSoundPlayed)
                return;

            state.ImpactSoundPlayed = true;
            // A non-negative position produces FMOD's positional route. It
            // remains audible in aerial view, but never becomes a global
            // map-wide rumble because the event is anchored at its impact.
            MusicBox.playSound(profile.ImpactSoundId, position.x, position.y,
                profile.ImpactSoundGameViewOnly, false);
        }

        private static WorldTile ResolveNativeTargetTile(Vector2 target)
        {
            if (!IsFinite(target) || World.world == null)
                return null;

            // Projectile.getCurrentTilePosition in build 719 uses conv.i4,
            // which truncates toward zero. Matching it exactly prevents the
            // visual effect and damageWorld from selecting adjacent tiles.
            return World.world.GetTile((int)target.x, (int)target.y);
        }

        private static float GetArcHeight(MissileProfile profile, float distance)
        {
            string id = profile?.Id;
            if (string.Equals(id, MissileIds.Torpedo, StringComparison.OrdinalIgnoreCase))
                return Mathf.Clamp(distance * 0.015f, 0.20f, 0.65f);
            if (string.Equals(id, MissileIds.Interceptor, StringComparison.OrdinalIgnoreCase))
                return Mathf.Clamp(distance * 0.035f, 0.75f, 2.25f);
            if (string.Equals(id, MissileIds.JetRocket, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, MissileIds.JetRocketHorde, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, MissileIds.JetRocketHarden, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, MissileIds.JetRocketGaia, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, MissileIds.BomberRocket, StringComparison.OrdinalIgnoreCase))
                return Mathf.Clamp(distance * 0.05f, 1.25f, 4.5f);
            if (profile != null && profile.Nuclear)
                return Mathf.Clamp(distance * 0.11f, 7f, 18f);
            return Mathf.Clamp(distance * 0.08f, 3.5f, 11f);
        }

        private static bool TrySetGuidedPosition(Projectile projectile, Vector3 previous,
            Vector3 next, float elapsed)
        {
            if (projectile == null || CurrentPositionRef == null || VelocityRef == null ||
                !IsFinite(new Vector2(next.x, next.y)) || !IsFinite(next.z))
                return false;

            try
            {
                CurrentPositionRef(projectile) = next;
                Vector3 velocity = elapsed > 0.0001f
                    ? (next - previous) / elapsed
                    : Vector3.zero;
                VelocityRef(projectile) = velocity;

                Vector2 visualDelta = new Vector2(
                    next.x - previous.x,
                    (next.y + next.z) - (previous.y + previous.z));
                if (visualDelta.sqrMagnitude > 0.000001f)
                {
                    float angle = Mathf.Atan2(visualDelta.y, visualDelta.x) * Mathf.Rad2Deg;
                    projectile.rotation = Quaternion.AngleAxis(angle, Vector3.forward);
                }
                return true;
            }
            catch (Exception ex)
            {
                ModernBoxLogger.Warning("[MissileLifecycle] Guided flight failed: " + ex.Message);
                return false;
            }
        }

        private static void TrySetTarget(Projectile projectile, Vector2 target)
        {
            if (projectile == null || TargetRef == null)
                return;
            try
            {
                TargetRef(projectile) = target;
            }
            catch
            {
                // Flight remains safe because the persisted ImpactPoint is the
                // authority even if a future build renames this private field.
            }
        }

        private static Vector3 GetVelocity(Projectile projectile)
        {
            if (projectile == null || VelocityRef == null)
                return Vector3.zero;
            try
            {
                return VelocityRef(projectile);
            }
            catch
            {
                return Vector3.zero;
            }
        }

        private static AccessTools.FieldRef<Projectile, TField> TryCreateFieldRef<TField>(string fieldName)
        {
            try
            {
                return AccessTools.FieldRefAccess<Projectile, TField>(fieldName);
            }
            catch (Exception ex)
            {
                ModernBoxLogger.Warning("[MissileLifecycle] Missing build-719 field " +
                    fieldName + ": " + ex.Message);
                return null;
            }
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
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(Projectile __instance)
        {
            MissileLifecycle.Forget(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
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
        private static bool Prefix(Projectile __instance)
        {
            return MissileLifecycle.BeforeUpdate(__instance);
        }
    }

    [HarmonyPatch(typeof(Projectile), "updateVelocity")]
    internal static class MissileLifecycleVelocityPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Projectile __instance, float pElapsed)
        {
            return MissileLifecycle.UpdateVelocity(__instance, pElapsed);
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
