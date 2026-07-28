using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using NCMS.Utils;
using tools;
using UnityEngine;

namespace ModernBox
{
    // A deliberately small, reactive layer over the existing launchers and
    // destroyers.  It does not add a new unit, weapon or production loop.
    internal static class IntegratedAirDefense
    {
        private const string DecisionId = "modernbox_integrated_air_defense";
        private const float LandRange = 30f;
        private const float NavalRange = 38f;
        private const float InterceptorSubmarineRange = 34f;
        private const float LandCooldown = 5.5f;
        private const float NavalCooldown = 4.5f;
        private const float InterceptorSubmarineCooldown = 14f;
        private const float MissileVisibleTime = 0.25f;
        private const float MissileCheckInterval = 0.15f;
        private const float InterceptorProjectileSpeed = 108f;
        private const float InterceptorArrivalRadius = 3.5f;
        private const float InterceptorArrivalGraceSeconds = 1.2f;

        private static readonly HashSet<string> InterceptableMissiles = new HashSet<string>(StringComparer.Ordinal)
        {
            "missileartillery",
            "fireboneartillery",
            "frostmissileartillery",
            "plantmissileartillery",
            "NUKER",
            "modernbox_baseline_ssbn_warhead",
            "SSBN_CZAR_WARHEAD",
            "modernbox_torpedo",
            "modernbox_arsenal_warhead",
            "modernbox_trident_warhead",
            "modernbox_neutron_warhead",
            "modernbox_emp_warhead",
            "modernbox_hammer_warhead",
            "modernbox_ruin_warhead"
        };

        private static readonly HashSet<string> ConventionalMissiles = new HashSet<string>(StringComparer.Ordinal)
        {
            "missileartillery",
            "fireboneartillery",
            "frostmissileartillery",
            "plantmissileartillery",
            "modernbox_torpedo",
            "modernbox_arsenal_warhead"
        };

        private static readonly HashSet<string> HeavyConventionalMissiles = new HashSet<string>(StringComparer.Ordinal)
        {
            "missileartillery",
            "fireboneartillery",
            "frostmissileartillery",
            "plantmissileartillery",
            "modernbox_arsenal_warhead"
        };

        private static readonly ConditionalWeakTable<Projectile, ProjectileData> ProjectileStates =
            new ConditionalWeakTable<Projectile, ProjectileData>();
        private static readonly ConditionalWeakTable<Actor, CooldownData> DefenderCooldowns =
            new ConditionalWeakTable<Actor, CooldownData>();
        private static bool _decisionRegistered;

        internal static bool Enabled { get; private set; } = true;

        // Strategic missiles keep their normal native flight/render path, but
        // ordinary actors must not be able to treat an ICBM as a sword-sized
        // collision target. Dedicated missile interception is handled below.
        internal static bool IsProtectedMissile(Projectile projectile)
        {
            return projectile?.asset != null && InterceptableMissiles.Contains(projectile.asset.id);
        }

        private sealed class ProjectileData
        {
            internal float age;
            internal float nextCheck;
            internal bool attempted;
            internal bool countermeasurePending;
            internal float interceptAt;
            internal float interceptTargetRemaining;
            internal Vector2 interceptPoint;
            internal bool impactSoundPlayed;
        }

        private sealed class CooldownData
        {
            internal float readyAt;
        }

        internal static void EnsureDecisionAsset()
        {
            if (_decisionRegistered)
                return;

            DecisionAsset decision = new DecisionAsset();
            decision.id = DecisionId;
            decision.priority = NeuroLayer.Layer_4_Critical;
            decision.path_icon = "ui/icons/MIRV";
            decision.cooldown = 1;
            decision.unique = true;
            decision.weight = 10f;
            decision.action_check_launch = TryLaunchAntiAir;
            AssetManager.decisions_library.add(decision);
            _decisionRegistered = true;
        }

        internal static void ConfigurePlatform(ActorAsset asset)
        {
            if (asset == null)
                return;

            if (asset.decision_ids == null)
                asset.decision_ids = new List<string>();
            if (!asset.decision_ids.Contains(DecisionId))
                asset.addDecision(DecisionId);
        }

        internal static void Toggle()
        {
            bool enabled = PowerButtons.GetToggleValue("air_defense_toggle");
            Main.modifyBoolOption("AirDefenseOption", enabled);
            Enabled = enabled;
        }

        internal static void SetEnabled(bool enabled)
        {
            Enabled = enabled;
        }

        private static bool TryLaunchAntiAir(Actor defender)
        {
            if (!Enabled || !IsAntiAirPlatform(defender) || !IsReady(defender))
                return false;

            float range = GetRange(defender);
            int chunkRadius = Mathf.Clamp(Mathf.CeilToInt(range / 12f), 1, 4);
            Actor target = null;
            float nearest = float.MaxValue;
            foreach (Actor candidate in Finder.getUnitsFromChunk(defender.current_tile, chunkRadius, range, false))
            {
                if (!IsHostileAircraft(defender, candidate))
                    continue;

                float distance = Vector2.Distance(defender.current_position, candidate.current_position);
                if (distance < nearest)
                {
                    target = candidate;
                    nearest = distance;
                }
            }

            if (target == null)
                return false;

            Vector3 origin = defender.current_position;
            Vector3 destination = target.current_position;
            Vector3 vector = Toolbox.getNewPoint(origin.x, origin.y, destination.x, destination.y, nearest);
            Vector3 start = Toolbox.getNewPoint(origin.x, origin.y, destination.x, destination.y, defender.stats["size"]);
            start.y += 0.5f;
            World.world.projectiles.spawn(defender, target, "jetrocketprojectile", start, vector);
            defender.punchTargetAnimation(vector, true, false, 45f);
            PutOnCooldown(defender);
            return true;
        }

        internal static bool TryInterceptMissile(Projectile projectile, float elapsed)
        {
            if (!Enabled || projectile?.asset == null || !InterceptableMissiles.Contains(projectile.asset.id))
                return false;

            ProjectileData state = ProjectileStates.GetOrCreateValue(projectile);
            state.age += elapsed;
            if (state.countermeasurePending)
                return ResolvePendingCountermeasure(projectile, state);
            if (state.attempted || state.age < MissileVisibleTime || state.age < state.nextCheck || projectile.kingdom == null)
                return false;

            state.nextCheck = state.age + MissileCheckInterval;
            Actor defender = FindMissileDefender(projectile.getCurrentTilePosition(), projectile.kingdom);
            if (defender == null)
                return false;

            if (IsInterceptorSubmarine(defender))
            {
                if (UnityEngine.Random.value > GetInterceptChance(defender, projectile.asset.id))
                {
                    state.attempted = true;
                    return false;
                }

                if (TryLaunchSubmarineCountermeasure(defender, projectile, state))
                {
                    state.attempted = true;
                    PutOnCooldown(defender);
                }
                else
                {
                    // The Guardian may be inside its detection radius while
                    // still being unable to reach this particular trajectory.
                    // Keep the hostile missile available for a later check
                    // instead of consuming every other defense opportunity.
                    state.nextCheck = state.age + MissileCheckInterval;
                }
                return false;
            }

            state.attempted = true;
            if (UnityEngine.Random.value > GetInterceptChance(defender, projectile.asset.id))
                return false;

            PutOnCooldown(defender);
            Vector2 position = projectile.getCurrentPosition();
            EffectsLibrary.spawnAt("fx_firebomb_explosion", position, 0.4f);
            MusicBox.playSound("event:/SFX/EXPLOSIONS/ExplosionSmall", position.x, position.y, true, false);
            projectile.setState(ProjectileState.ToRemove);
            return true;
        }

        private static bool TryLaunchSubmarineCountermeasure(Actor defender, Projectile hostile,
            ProjectileData state)
        {
            if (defender?.current_tile == null || hostile?.asset == null || World.world?.projectiles == null ||
                AssetManager.projectiles?.get(NavalRoles.InterceptorProjectileId) == null)
                return false;

            Vector2 current = hostile.getCurrentPosition();
            Vector2 destination = hostile.getTargetVector();
            Vector2 path = destination - current;
            float remainingDistance = path.magnitude;
            float hostileSpeed = Mathf.Max(1f, hostile.asset.speed);
            if (remainingDistance <= 3.5f || float.IsNaN(remainingDistance) || float.IsInfinity(remainingDistance))
                return false;

            float hostileEta = remainingDistance / hostileSpeed;
            Vector2 direction = path / remainingDistance;
            float directTravel = Vector2.Distance(defender.current_position, current) / InterceptorProjectileSpeed;
            if (directTravel >= hostileEta - 0.05f)
                return false;

            float leadDistance = Mathf.Clamp(hostileSpeed * directTravel, 1.5f, remainingDistance - 1.5f);
            Vector2 interceptPoint = current + direction * leadDistance;
            float interceptorEta = Vector2.Distance(defender.current_position, interceptPoint) / InterceptorProjectileSpeed;
            if (interceptorEta >= hostileEta - 0.02f)
                return false;

            Vector3 origin = defender.current_position;
            Vector3 vector = Toolbox.getNewPoint(origin.x, origin.y, interceptPoint.x, interceptPoint.y,
                Vector2.Distance(origin, interceptPoint));
            Vector3 start = Toolbox.getNewPoint(origin.x, origin.y, interceptPoint.x, interceptPoint.y,
                defender.stats["size"]);
            start.y += 0.5f;
            try
            {
                World.world.projectiles.spawn(defender, null, NavalRoles.InterceptorProjectileId, start, vector);
            }
            catch
            {
                return false;
            }

            state.countermeasurePending = true;
            state.interceptAt = Time.time + interceptorEta;
            state.interceptPoint = interceptPoint;
            state.interceptTargetRemaining = Vector2.Distance(interceptPoint, destination);
            defender.punchTargetAnimation(vector, true, false, 45f);
            return true;
        }

        private static bool ResolvePendingCountermeasure(Projectile hostile, ProjectileData state)
        {
            if (Time.time < state.interceptAt)
                return false;

            Vector2 current = hostile.getCurrentPosition();
            float distanceToIntercept = Vector2.Distance(current, state.interceptPoint);
            if (distanceToIntercept <= InterceptorArrivalRadius)
            {
                state.countermeasurePending = false;
                EffectsLibrary.spawnAt("fx_explosion_middle", state.interceptPoint, 0.45f);
                MusicBox.playSound("event:/SFX/EXPLOSIONS/ExplosionSmall", state.interceptPoint.x,
                    state.interceptPoint.y, true, false);
                hostile.setState(ProjectileState.ToRemove);
                return true;
            }

            float remainingToTarget = Vector2.Distance(current, hostile.getTargetVector());
            if (Time.time >= state.interceptAt + InterceptorArrivalGraceSeconds ||
                remainingToTarget + 0.5f < state.interceptTargetRemaining)
            {
                // The hostile projectile passed or out-ran the rendezvous. The
                // defensive missile stays harmless and the original warhead
                // continues normally rather than disappearing mid-flight.
                state.countermeasurePending = false;
            }
            return false;
        }

        internal static void Forget(Projectile projectile)
        {
            if (projectile != null)
                ProjectileStates.Remove(projectile);
        }

        internal static void PlayConventionalImpactSound(Projectile projectile)
        {
            if (projectile?.asset == null || !ConventionalMissiles.Contains(projectile.asset.id))
                return;

            ProjectileData state = ProjectileStates.GetOrCreateValue(projectile);
            if (state.impactSoundPlayed)
                return;

            state.impactSoundPlayed = true;
            // Keep the conventional impact local to the real detonation.  The
            // previous (-1, -1) non-positional route made every impact rumble
            // across the entire map and could be mistaken for a nuclear blast.
            // Cruise/land missiles retain WorldBox's native meteorite report;
            // the lighter torpedo keeps its original smaller splash-like impact.
            string impactSound = HeavyConventionalMissiles.Contains(projectile.asset.id)
                ? "event:/SFX/EXPLOSIONS/ExplosionMeteorite"
                : "event:/SFX/EXPLOSIONS/ExplosionSmall";
            Vector2 impactPosition = projectile.getTargetVector();
            MusicBox.playSound(impactSound, impactPosition.x, impactPosition.y, true, true);
        }

        private static Actor FindMissileDefender(WorldTile missileTile, Kingdom missileKingdom)
        {
            if (missileTile == null || World.world?.units == null)
                return null;

            Actor best = null;
            float nearest = float.MaxValue;
            foreach (Actor candidate in Finder.getUnitsFromChunk(missileTile, 4, NavalRange, false))
            {
                if (!IsMissileDefensePlatform(candidate) || candidate.kingdom == null ||
                    !candidate.kingdom.isEnemy(missileKingdom) || !IsReady(candidate))
                    continue;

                float distance = Vector2.Distance(candidate.current_position, missileTile.pos);
                if (distance <= GetRange(candidate) && distance < nearest)
                {
                    best = candidate;
                    nearest = distance;
                }
            }
            return best;
        }

        private static bool IsMissileDefensePlatform(Actor actor)
        {
            return IsAntiAirPlatform(actor) || IsInterceptorSubmarine(actor);
        }

        private static bool IsAntiAirPlatform(Actor actor)
        {
            if (actor == null || !actor.isAlive() || actor.asset == null || actor.current_tile == null || actor.kingdom == null)
                return false;

            string id = actor.asset.id;
            return id.StartsWith("MissileSystem_", StringComparison.OrdinalIgnoreCase) ||
                   id.StartsWith("aDestroyer_", StringComparison.OrdinalIgnoreCase) ||
                   id.StartsWith("bDestroyer_", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsInterceptorSubmarine(Actor actor)
        {
            return actor != null && actor.isAlive() && actor.asset != null && actor.current_tile != null &&
                actor.kingdom != null && NavalRoles.IsInterceptorSubmarine(actor.asset.id);
        }

        private static bool IsHostileAircraft(Actor defender, Actor candidate)
        {
            if (candidate == null || !candidate.isAlive() || candidate.kingdom == null ||
                !defender.kingdom.isEnemy(candidate.kingdom) || candidate.asset == null ||
                !candidate.isFlying())
                return false;

            if (!ModernCapPolicy.IsAllowedAircraft(candidate.asset.id) ||
                (Vehicles.IsFixedWingMissionAircraft(candidate) && !Vehicles.IsAircraftInAttackWindow(candidate)))
                return false;

            // The B-2-style bomber only exposes itself to an anti-air search
            // on a rare radar contact.  It remains technically interceptable,
            // while several overlapping batteries do not reliably erase every
            // sortie during its short attack pass.
            return !Vehicles.IsStealthBomber(candidate) || UnityEngine.Random.value < 0.04f;
        }

        private static bool IsReady(Actor defender)
        {
            return !DefenderCooldowns.TryGetValue(defender, out CooldownData data) || Time.time >= data.readyAt;
        }

        private static void PutOnCooldown(Actor defender)
        {
            CooldownData data = DefenderCooldowns.GetOrCreateValue(defender);
            data.readyAt = Time.time + (IsInterceptorSubmarine(defender)
                ? InterceptorSubmarineCooldown
                : IsNavalPlatform(defender) ? NavalCooldown : LandCooldown);
        }

        private static bool IsNavalPlatform(Actor actor)
        {
            string id = actor?.asset?.id;
            return !string.IsNullOrEmpty(id) &&
                   (id.StartsWith("aDestroyer_", StringComparison.OrdinalIgnoreCase) ||
                    id.StartsWith("bDestroyer_", StringComparison.OrdinalIgnoreCase));
        }

        private static float GetRange(Actor actor)
        {
            return IsInterceptorSubmarine(actor) ? InterceptorSubmarineRange :
                IsNavalPlatform(actor) ? NavalRange : LandRange;
        }

        private static float GetInterceptChance(Actor defender, string projectileId)
        {
            float chance;
            if (string.Equals(projectileId, "SSBN_CZAR_WARHEAD", StringComparison.Ordinal))
                chance = 0.25f;
            else if (string.Equals(projectileId, "modernbox_baseline_ssbn_warhead", StringComparison.Ordinal))
                chance = 0.25f;
            else if (string.Equals(projectileId, "modernbox_hammer_warhead", StringComparison.Ordinal))
                chance = 0.12f;
            else if (string.Equals(projectileId, "NUKER", StringComparison.Ordinal))
                chance = 0.25f;
            else if (NavalRoles.IsHeavyWarhead(projectileId))
                chance = 0.30f;
            else
                chance = 0.65f;

            return IsInterceptorSubmarine(defender)
                ? Mathf.Clamp(chance + 0.15f, 0.20f, 0.82f)
                : chance;
        }
    }

    [HarmonyPatch(typeof(Projectile), "update")]
    internal static class IntegratedAirDefenseProjectilePatch
    {
        private static bool Prefix(Projectile __instance, float pElapsed)
        {
            bool intercepted = IntegratedAirDefense.TryInterceptMissile(__instance, pElapsed);
            if (!intercepted)
                NuclearAlertController.Observe(__instance, pElapsed);
            return !intercepted;
        }

        [HarmonyPatch(typeof(Projectile), "start")]
        [HarmonyPostfix]
        private static void StartPostfix(Projectile __instance)
        {
            IntegratedAirDefense.Forget(__instance);
            NuclearAlertController.Forget(__instance);
        }

        [HarmonyPatch(typeof(Projectile), "reset")]
        [HarmonyPostfix]
        private static void ResetPostfix(Projectile __instance)
        {
            IntegratedAirDefense.Forget(__instance);
            NuclearAlertController.Forget(__instance);
        }

        [HarmonyPatch(typeof(Projectile), "targetReached")]
        [HarmonyPrefix]
        private static void TargetReachedPrefix(Projectile __instance)
        {
            IntegratedAirDefense.PlayConventionalImpactSound(__instance);
            IntegratedAirDefense.Forget(__instance);
            NuclearAlertController.Forget(__instance);
        }
    }

    [HarmonyPatch(typeof(Projectile), "canBeCollided")]
    internal static class StrategicMissileCollisionPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Projectile __instance, ref bool __result)
        {
            if (!IntegratedAirDefense.IsProtectedMissile(__instance))
                return true;

            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Projectile), "checkHitOnNearbyUnits")]
    internal static class StrategicMissileNearbyHitPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Projectile __instance, ref AttackDataResult __result)
        {
            if (!IntegratedAirDefense.IsProtectedMissile(__instance))
                return true;

            // `default(AttackDataResult)` means Hit in WorldBox, which makes
            // Projectile.update remove the missile before its flight sprite is
            // drawn. Continue rejects ordinary nearby attacks but preserves the
            // native movement and render loop.
            __result = AttackDataResult.Continue;
            return false;
        }
    }

    [HarmonyPatch(typeof(Projectile), "getDeflected")]
    internal static class StrategicMissileDeflectionPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Projectile __instance)
        {
            return !IntegratedAirDefense.IsProtectedMissile(__instance);
        }
    }

}
