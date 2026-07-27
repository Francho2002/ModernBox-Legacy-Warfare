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
        private const float LandCooldown = 5.5f;
        private const float NavalCooldown = 4.5f;
        private const float MissileVisibleTime = 0.25f;
        private const float MissileCheckInterval = 0.15f;

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

        private sealed class ProjectileData
        {
            internal float age;
            internal float nextCheck;
            internal bool attempted;
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
            if (!Enabled || !IsDefensePlatform(defender) || !IsReady(defender))
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
            if (state.attempted || state.age < MissileVisibleTime || state.age < state.nextCheck || projectile.kingdom == null)
                return false;

            state.nextCheck = state.age + MissileCheckInterval;
            Actor defender = FindDefender(projectile.getCurrentTilePosition(), projectile.kingdom);
            if (defender == null)
                return false;

            state.attempted = true;
            PutOnCooldown(defender);
            if (UnityEngine.Random.value > GetInterceptChance(projectile.asset.id))
                return false;

            Vector2 position = projectile.getCurrentPosition();
            EffectsLibrary.spawnAt("fx_firebomb_explosion", position, 0.4f);
            MusicBox.playSound("event:/SFX/EXPLOSIONS/ExplosionSmall", position.x, position.y, true, false);
            projectile.setState(ProjectileState.ToRemove);
            return true;
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
            // Coordinates outside the map use WorldBox's non-positional path:
            // the impact remains audible at maximum zoom without nuclear volume.
            // Cruise/land missiles use WorldBox's native meteorite report; the
            // lighter torpedo keeps its original smaller splash-like impact.
            string impactSound = HeavyConventionalMissiles.Contains(projectile.asset.id)
                ? "event:/SFX/EXPLOSIONS/ExplosionMeteorite"
                : "event:/SFX/EXPLOSIONS/ExplosionSmall";
            MusicBox.playSound(impactSound, -1f, -1f, true, false);
        }

        private static Actor FindDefender(WorldTile missileTile, Kingdom missileKingdom)
        {
            if (missileTile == null || World.world?.units == null)
                return null;

            Actor best = null;
            float nearest = float.MaxValue;
            foreach (Actor candidate in Finder.getUnitsFromChunk(missileTile, 4, NavalRange, false))
            {
                if (!IsDefensePlatform(candidate) || candidate.kingdom == null ||
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

        private static bool IsDefensePlatform(Actor actor)
        {
            if (actor == null || !actor.isAlive() || actor.asset == null || actor.current_tile == null || actor.kingdom == null)
                return false;

            string id = actor.asset.id;
            return id.StartsWith("MissileSystem_", StringComparison.OrdinalIgnoreCase) ||
                   id.StartsWith("aDestroyer_", StringComparison.OrdinalIgnoreCase) ||
                   id.StartsWith("bDestroyer_", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsHostileAircraft(Actor defender, Actor candidate)
        {
            if (candidate == null || !candidate.isAlive() || candidate.kingdom == null ||
                !defender.kingdom.isEnemy(candidate.kingdom) || candidate.asset == null ||
                !candidate.isFlying())
                return false;

            return ModernCapPolicy.IsAllowedAircraft(candidate.asset.id);
        }

        private static bool IsReady(Actor defender)
        {
            return !DefenderCooldowns.TryGetValue(defender, out CooldownData data) || Time.time >= data.readyAt;
        }

        private static void PutOnCooldown(Actor defender)
        {
            CooldownData data = DefenderCooldowns.GetOrCreateValue(defender);
            data.readyAt = Time.time + (IsNavalPlatform(defender) ? NavalCooldown : LandCooldown);
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
            return IsNavalPlatform(actor) ? NavalRange : LandRange;
        }

        private static float GetInterceptChance(string projectileId)
        {
            if (string.Equals(projectileId, "SSBN_CZAR_WARHEAD", StringComparison.Ordinal))
                return 0.25f;
            if (string.Equals(projectileId, "modernbox_baseline_ssbn_warhead", StringComparison.Ordinal))
                return 0.25f;
            if (string.Equals(projectileId, "modernbox_hammer_warhead", StringComparison.Ordinal))
                return 0.12f;
            if (string.Equals(projectileId, "NUKER", StringComparison.Ordinal))
                return 0.25f;
            if (NavalRoles.IsHeavyWarhead(projectileId))
                return 0.30f;
            return 0.65f;
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
            NuclearAlertController.Forget(__instance);
        }
    }
}
