using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using NCMS.Utils;
using tools;
using UnityEngine;

namespace ModernBox
{
    /// <summary>
    /// Realistic submarine classes layered over the original faction submarine
    /// sprites.  Their identifiers are stable and never depend on a visual era.
    /// </summary>
    internal static class NavalRoles
    {
        private const string HunterDecisionId = "modernbox_sub_hunter_attack";
        private const string ArsenalDecisionId = "modernbox_sub_arsenal_attack";
        private const string TridentDecisionId = "modernbox_sub_trident_attack";
        private const string NeutronDecisionId = "modernbox_sub_neutron_attack";
        private const string EmpDecisionId = "modernbox_sub_emp_attack";
        private const string HammerDecisionId = "modernbox_sub_hammer_attack";
        private const string RuinDecisionId = "modernbox_sub_ruin_attack";

        private const string TorpedoProjectileId = "modernbox_torpedo";
        internal const string InterceptorProjectileId = "modernbox_interceptor_missile";
        private const string ArsenalProjectileId = "modernbox_arsenal_warhead";
        private const string TridentProjectileId = "modernbox_trident_warhead";
        private const string NeutronProjectileId = "modernbox_neutron_warhead";
        private const string EmpProjectileId = "modernbox_emp_warhead";
        private const string HammerProjectileId = "modernbox_hammer_warhead";
        private const string RuinProjectileId = "modernbox_ruin_warhead";

        private static readonly string[] Factions = { "alliance", "harden", "gaia", "horde" };
        private static readonly ConditionalWeakTable<Actor, ConventionalLaunchState> ConventionalLaunchStates =
            new ConditionalWeakTable<Actor, ConventionalLaunchState>();

        private sealed class ConventionalLaunchState
        {
            internal float readyAt;
        }

        private sealed class RoleDefinition
        {
            internal readonly string Prefix;
            internal readonly string DisplayName;
            internal readonly string BoatPrefix;
            internal readonly string DecisionId;
            internal readonly ConstructionCost Cost;
            internal readonly bool Strategic;

            internal RoleDefinition(string prefix, string displayName, string boatPrefix, string decisionId,
                ConstructionCost cost, bool strategic)
            {
                Prefix = prefix;
                DisplayName = displayName;
                BoatPrefix = boatPrefix;
                DecisionId = decisionId;
                Cost = cost;
                Strategic = strategic;
            }
        }

        private static readonly RoleDefinition[] Roles =
        {
            // A dedicated missile-defense hull. It deliberately has no attack
            // decision: IntegratedAirDefense launches its interceptor only at
            // an incoming projectile, never at a city or an aircraft.
            new RoleDefinition("InterceptorSubmarine", "SSN Guardián", "interceptor_submarine", null,
                new ConstructionCost(10, 9, 7, 3), false),
            new RoleDefinition("HunterSubmarine", "SSN Cazador", "hunter_submarine", HunterDecisionId,
                new ConstructionCost(6, 5, 3, 1), false),
            new RoleDefinition("ArsenalSubmarine", "SSGN Arsenal", "arsenal_submarine", ArsenalDecisionId,
                new ConstructionCost(8, 7, 5, 2), true),
            new RoleDefinition("TridentSubmarine", "SSBN Tridente", "trident_submarine", TridentDecisionId,
                new ConstructionCost(12, 10, 8, 4), true),
            new RoleDefinition("NeutronSubmarine", "SSBN Neutrón", "neutron_submarine", NeutronDecisionId,
                new ConstructionCost(9, 8, 6, 3), true),
            new RoleDefinition("EmpSubmarine", "SSBN EMP", "emp_submarine", EmpDecisionId,
                new ConstructionCost(9, 8, 6, 3), true),
            new RoleDefinition("HammerSubmarine", "SSBN Martillo", "hammer_submarine", HammerDecisionId,
                new ConstructionCost(13, 11, 9, 5), true),
            new RoleDefinition("RuinSubmarine", "SSBN Ruina", "ruin_submarine", RuinDecisionId,
                new ConstructionCost(9, 8, 6, 3), true)
        };

        private static bool _initialized;

        internal static void Initialize()
        {
            if (_initialized)
                return;

            CreateSafeBlasts();
            CreateProjectiles();
            RegisterDecisions();
            foreach (string faction in Factions)
            {
                foreach (RoleDefinition role in Roles)
                    CreateRoleSubmarine(faction, role);
            }

            _initialized = true;
        }

        internal static void RegisterSpawnUnits()
        {
            if (UnitTracker.Instance == null)
                return;

            foreach (string id in GetRoleIds())
                UnitTracker.Instance.RegisterUnit(id);
        }

        internal static IEnumerable<string> GetRoleIds()
        {
            foreach (string faction in Factions)
            {
                foreach (RoleDefinition role in Roles)
                    yield return role.Prefix + "_" + faction;
            }
        }

        internal static bool IsRoleSubmarine(string actorId)
        {
            if (string.IsNullOrEmpty(actorId))
                return false;

            foreach (RoleDefinition role in Roles)
            {
                if (actorId.StartsWith(role.Prefix + "_", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        internal static bool IsAnyModernSubmarine(string actorId)
        {
            return !string.IsNullOrEmpty(actorId) &&
                (actorId.StartsWith("Submarine_", StringComparison.OrdinalIgnoreCase) ||
                 actorId.StartsWith("SalvoSubmarine_", StringComparison.OrdinalIgnoreCase) ||
                 IsRoleSubmarine(actorId));
        }

        internal static bool IsStrategicSubmarine(string actorId)
        {
            if (string.IsNullOrEmpty(actorId))
                return false;

            // The original nuclear submarine remains strategic for existing saves.
            if (actorId.StartsWith("Submarine_", StringComparison.OrdinalIgnoreCase) ||
                actorId.StartsWith("SalvoSubmarine_", StringComparison.OrdinalIgnoreCase))
                return true;

            foreach (RoleDefinition role in Roles)
            {
                if (role.Strategic && actorId.StartsWith(role.Prefix + "_", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        internal static bool IsInterceptorSubmarine(string actorId)
        {
            return !string.IsNullOrEmpty(actorId) &&
                actorId.StartsWith("InterceptorSubmarine_", StringComparison.OrdinalIgnoreCase);
        }

        internal static SubmarineTargetLane GetTargetReservationLane(Actor caster)
        {
            string actorId = caster?.asset?.id;
            if (string.IsNullOrEmpty(actorId))
                return SubmarineTargetLane.Conventional;
            if (actorId.StartsWith("EmpSubmarine_", StringComparison.OrdinalIgnoreCase))
                return SubmarineTargetLane.Electronic;
            if (IsStrategicSubmarine(actorId))
                return SubmarineTargetLane.Strategic;
            return SubmarineTargetLane.Conventional;
        }

        internal static string GetRoleLabel(string actorId)
        {
            if (string.IsNullOrEmpty(actorId))
                return null;

            if (actorId.StartsWith("SalvoSubmarine_", StringComparison.OrdinalIgnoreCase))
                return "SSBN Apocalipsis";
            if (actorId.StartsWith("Submarine_", StringComparison.OrdinalIgnoreCase))
                return "SSBN Tactico";
            foreach (RoleDefinition role in Roles)
            {
                if (actorId.StartsWith(role.Prefix + "_", StringComparison.OrdinalIgnoreCase))
                    return role.DisplayName;
            }
            return null;
        }

        internal static bool TryGetSpawnMetadata(string actorId, string faction, out string title, out string description)
        {
            title = null;
            description = null;
            string label = GetRoleLabel(actorId);
            if (string.IsNullOrEmpty(label))
                return false;

            string factionLabel = string.IsNullOrEmpty(faction) ? "Sin facción" : faction;
            title = label + " - " + factionLabel;
            if (actorId.StartsWith("HunterSubmarine_", StringComparison.OrdinalIgnoreCase))
            {
                description = "1 torpedo + 2 misiles normales.";
            }
            else if (IsInterceptorSubmarine(actorId))
            {
                description = "Contramisil defensivo: intercepta misiles entrantes; no ataca ciudades ni aeronaves.";
            }
            else if (actorId.StartsWith("Submarine_", StringComparison.OrdinalIgnoreCase))
            {
                description = "1 misil nuclear normal.";
            }
            else if (actorId.StartsWith("ArsenalSubmarine_", StringComparison.OrdinalIgnoreCase))
            {
                description = "6 a 10 misiles normales.";
            }
            else if (actorId.StartsWith("TridentSubmarine_", StringComparison.OrdinalIgnoreCase))
            {
                description = "3 a 5 misiles nucleares.";
            }
            else if (actorId.StartsWith("NeutronSubmarine_", StringComparison.OrdinalIgnoreCase))
            {
                description = "1 nuclear pequeña que aturde.";
            }
            else if (actorId.StartsWith("EmpSubmarine_", StringComparison.OrdinalIgnoreCase))
            {
                description = "1 pulso EMP que paraliza vehículos.";
            }
            else if (actorId.StartsWith("HammerSubmarine_", StringComparison.OrdinalIgnoreCase))
            {
                description = "1 misil nuclear de gran potencia.";
            }
            else if (actorId.StartsWith("RuinSubmarine_", StringComparison.OrdinalIgnoreCase))
            {
                description = "1 nuclear pequeña con aturdimiento.";
            }
            else
            {
                description = "4 a 6 misiles nucleares a objetivos distintos.";
            }
            return true;
        }

        internal static bool IsHeavyWarhead(string projectileId)
        {
            return string.Equals(projectileId, TridentProjectileId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(projectileId, NeutronProjectileId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(projectileId, HammerProjectileId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(projectileId, RuinProjectileId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(projectileId, EmpProjectileId, StringComparison.OrdinalIgnoreCase);
        }

        // Cuando un reino carece de alas fijas operativas, el controlador naval
        // usa esta cadencia como sustitución limitada del apoyo aéreo. No altera
        // los enfriamientos ni las decisiones normales mientras sí haya aviación.
        internal static float GetNoAirFallbackCadence(string actorId)
        {
            if (string.IsNullOrEmpty(actorId))
                return 30f;
            if (actorId.StartsWith("HunterSubmarine_", StringComparison.OrdinalIgnoreCase))
                return 30f;
            if (actorId.StartsWith("ArsenalSubmarine_", StringComparison.OrdinalIgnoreCase))
                return 90f;
            if (actorId.StartsWith("SalvoSubmarine_", StringComparison.OrdinalIgnoreCase))
                return 24f;
            if (IsRoleSubmarine(actorId))
                return 45f;
            return 18f;
        }

        internal static bool TryLaunchNoAirFallback(Actor caster)
        {
            string actorId = caster?.asset?.id;
            if (IsInterceptorSubmarine(actorId))
                return false;
            if (!CanLaunchConventional(caster, 0))
                return false;

            if (actorId != null && actorId.StartsWith("HunterSubmarine_", StringComparison.OrdinalIgnoreCase))
                return HunterEffect(caster);
            if (actorId != null && actorId.StartsWith("ArsenalSubmarine_", StringComparison.OrdinalIgnoreCase))
                return ArsenalEffect(caster);

            // Los SSBN de guerra especial mantienen sus restricciones nucleares.
            // Esta salida sólo usa su misil convencional habitual para cubrir la
            // ausencia de aviación, nunca adelanta una carga estratégica.
            Vector2? target = GetEnemyTargets(caster, 1, 10f, 4f).FirstOrNull();
            return target != null && LaunchAt(caster, target.Value, GetFactionConventionalProjectile(actorId));
        }

        private static string GetFactionConventionalProjectile(string actorId)
        {
            if (actorId != null && actorId.EndsWith("_horde", StringComparison.OrdinalIgnoreCase))
                return "fireboneartillery";
            if (actorId != null && actorId.EndsWith("_harden", StringComparison.OrdinalIgnoreCase))
                return "frostmissileartillery";
            if (actorId != null && actorId.EndsWith("_gaia", StringComparison.OrdinalIgnoreCase))
                return "plantmissileartillery";
            return "missileartillery";
        }

        private static void CreateRoleSubmarine(string faction, RoleDefinition role)
        {
            string id = role.Prefix + "_" + faction;
            if (AssetManager.actor_library.get(id) != null)
                return;

            ActorAsset submarine = AssetManager.actor_library.clone(id, "Submarine_" + faction);
            if (submarine == null)
            {
                ModernBoxLogger.Warning("[NavalRoles] Base submarine missing for faction " + faction + ".");
                return;
            }

            submarine.id = id;
            submarine.boat_type = role.BoatPrefix + "_" + faction + "_boat";
            submarine.name_locale = role.DisplayName;
            submarine.cost = role.Cost;
            submarine.decision_ids = new List<string>();
            if (!string.IsNullOrEmpty(role.DecisionId))
                submarine.addDecision(role.DecisionId);
            submarine.addDecision("random_swim");
            submarine.default_attack = IsInterceptorSubmarine(id) ? "boat_cannonball" : "MissileSystemmissile";
            submarine.addTrait("NavalUnit");
            // Strategic hulls are intentionally costly and survivable without
            // crossing into fantastical end-game statistics.
            if (role.Strategic)
            {
                submarine.base_stats["health"] = 2600f;
                submarine.base_stats["armor"] = 38f;
                submarine.base_stats["speed"] = 52f;
            }
            else
            {
                submarine.base_stats["health"] = 2200f;
                submarine.base_stats["armor"] = 34f;
                submarine.base_stats["speed"] = 66f;
            }

            AssetManager.actor_library.add(submarine);
            Localization.addLocalization(submarine.name_locale, submarine.name_locale);
        }

        private static void RegisterDecisions()
        {
            RegisterDecision(HunterDecisionId, 30, HunterEffect);
            RegisterDecision(ArsenalDecisionId, 90, ArsenalEffect);
            RegisterDecision(TridentDecisionId, 720, TridentEffect);
            RegisterDecision(NeutronDecisionId, 180, NeutronEffect);
            RegisterDecision(EmpDecisionId, 150, EmpEffect);
            RegisterDecision(HammerDecisionId, 900, HammerEffect);
            RegisterDecision(RuinDecisionId, 210, RuinEffect);
        }

        private static void RegisterDecision(string id, int cooldown, Func<Actor, bool> action)
        {
            if (string.IsNullOrEmpty(id))
                return;
            if (AssetManager.decisions_library.get(id) != null)
                return;

            DecisionAsset decision = new DecisionAsset();
            decision.id = id;
            decision.priority = NeuroLayer.Layer_1_Low;
            decision.path_icon = "ui/icons/MIRV";
            decision.cooldown = cooldown;
            decision.unique = true;
            decision.weight = 1f;
            decision.action_check_launch = delegate(Actor actor) { return action(actor); };
            AssetManager.decisions_library.add(decision);
        }

        private static void CreateSafeBlasts()
        {
            CreateSafeBlast("modernbox_arsenal_blast", 150, 5, true);
            CreateSafeBlast("modernbox_trident_blast", 680, 15, true);
            CreateSafeBlast("modernbox_neutron_blast", 260, 6, true);
            CreateSafeBlast("modernbox_ruin_blast", 110, 4, true);
            CreateSafeBlast("modernbox_hammer_blast", 1100, 22, true);
        }

        private static void CreateSafeBlast(string id, int damage, int strength, bool destroysBuildings)
        {
            if (AssetManager.terraform.get(id) != null)
                return;

            TerraformOptions blast = AssetManager.terraform.clone(id, "modern_cap_nuclear_blast");
            if (blast == null)
                return;

            blast.shake = false;
            blast.transform_to_wasteland = false;
            blast.explode_tile = false;
            blast.destroy_buildings = destroysBuildings;
            blast.damage_buildings = destroysBuildings;
            blast.damage = damage;
            blast.explode_strength = strength;
            blast.set_fire = false;
            blast.explode_and_set_random_fire = false;
            blast.add_burned = false;
            blast.add_trait = null;
            // Atomic-bomb callbacks can mutate terrain independently of the
            // TerraformOptions fields. The projectiles below retain the blast
            // damage but never delegate permanent terrain work to the stock callback.
            blast.bomb_action = null;
            AssetManager.terraform.add(blast);
        }

        private static void CreateProjectiles()
        {
            // A visual defensive projectile only. It has no terraform option,
            // damage, fire or nuclear effect; the defense controller removes
            // the hostile missile only after this countermeasure reaches it.
            CreateProjectile(InterceptorProjectileId, "missileartillery", null, 0, 108f,
                0.26f, "fx_explosion_middle", false, 0.35f, false);
            CreateProjectile(TorpedoProjectileId, "missileartillery", "modern_cap_missile_blast", 4, 62f,
                0.42f, "fx_firebomb_explosion", false, 0.55f);
            // Retained only so an already-saved in-flight Arsenal projectile
            // can still be resolved after upgrading. New Arsenal salvos use
            // the exact faction conventional projectile below.
            CreateProjectile(ArsenalProjectileId, "missileartillery", "modernbox_arsenal_blast", 6, 37f,
                0.50f, "fx_explosion_meteorite", false, 0.72f);
            // El Tridente emplea una cabeza MIRV propia: más amplia que la
            // nuclear estándar pero por debajo del Martillo, sin cráteres ni
            // bioma radiactivo.
            CreateProjectile(TridentProjectileId, "NUKER", "modernbox_trident_blast", 16, 43f,
                0.58f, "fx_explosion_nuke_atomic", true, 1.00f);
            CreateProjectile(NeutronProjectileId, "NUKER", "modernbox_neutron_blast", 7, 45.5f,
                0.48f, "fx_explosion_nuke_atomic", true, 0.72f);
            // The EMP detonates at altitude as a flash: it does not damage
            // terrain and its disable effect applies only to hostile forces.
            CreateProjectile(EmpProjectileId, "NUKER", null, 0, 51f,
                0.46f, "fx_explosion_middle", true, 0.80f);
            CreateProjectile(HammerProjectileId, "NUKER", "modernbox_hammer_blast", 34, 38.5f,
                0.72f, "fx_explosion_huge", true, 1.45f);
            CreateProjectile(RuinProjectileId, "NUKER", "modernbox_ruin_blast", 9, 44f,
                0.50f, "fx_explosion_middle", true, 0.90f);
        }

        private static void CreateProjectile(string id, string texture, string terraformId, int terraformRange,
            float speed, float scale, string effect, bool nuclearSound, float effectScale, bool leaveTrail = true)
        {
            if (AssetManager.projectiles.get(id) != null)
                return;

            ProjectileAsset projectile = new ProjectileAsset();
            projectile.id = id;
            projectile.speed = speed;
            projectile.texture = texture;
            projectile.look_at_target = true;
            projectile.texture_shadow = "shadows/projectiles/shadow_ball";
            projectile.terraform_option = string.IsNullOrEmpty(terraformId) ? string.Empty : terraformId;
            projectile.terraform_range = terraformRange;
            projectile.draw_light_area = nuclearSound;
            projectile.sound_launch = nuclearSound
                ? "event:/SFX/WEAPONS/WeaponFireballStart"
                : "event:/SFX/WEAPONS/WeaponShotgunStart";
            projectile.sound_impact = nuclearSound
                ? "event:/SFX/WEAPONS/WeaponFireballLand"
                : string.Empty;
            projectile.end_effect = effect;
            projectile.end_effect_scale = effectScale;
            projectile.trail_effect_enabled = leaveTrail;
            projectile.trail_effect_id = "modern_cap_missile_trail";
            projectile.trail_effect_scale = 0.30f;
            projectile.trail_effect_timer = 0.10f;
            projectile.scale_start = scale;
            projectile.scale_target = scale;
            projectile.can_be_left_on_ground = false;
            projectile.can_be_blocked = false;
            AssetManager.projectiles.add(projectile);
        }

        private static bool HunterEffect(Actor caster)
        {
            if (!CanLaunchConventional(caster, 8))
                return false;

            if (!IsConventionalLaunchReady(caster))
                return false;

            Vector2? target = GetNearestEnemyBoatTarget(caster) ?? GetEnemyTargets(caster, 1, 6f, 4f, false).FirstOrNull();
            if (target == null)
                return false;

            SpendGold(caster.city, 8);
            bool launched = LaunchAt(caster, target.Value, TorpedoProjectileId, true);
            launched |= LaunchAt(caster, target.Value + new Vector2(4f, 0f), "missileartillery", true);
            launched |= LaunchAt(caster, target.Value + new Vector2(-4f, 0f), "missileartillery", true);
            if (launched)
                MarkConventionalLaunch(caster, 30f);
            return launched;
        }

        private static bool ArsenalEffect(Actor caster)
        {
            if (!CanLaunchConventional(caster, 25))
                return false;

            if (!IsConventionalLaunchReady(caster))
                return false;

            int count = UnityEngine.Random.Range(6, 11);
            List<Vector2> targets = GetEnemyTargets(caster, count, 8f, 4f);
            if (targets.Count == 0)
                return false;

            SpendGold(caster.city, 25);
            string projectileId = GetFactionConventionalProjectile(caster.asset?.id);
            bool launched = LaunchAtAll(caster, targets, projectileId);
            if (launched)
                MarkConventionalLaunch(caster, 90f);
            return launched;
        }

        private static bool TridentEffect(Actor caster)
        {
            if (!CanLaunchNuclear(caster, 180, true))
                return false;

            int count = UnityEngine.Random.Range(3, 6);
            List<Vector2> targets = GetEnemyTargets(caster, count, 16f, 20f);
            if (targets.Count == 0)
                return false;

            SpendGold(caster.city, 180);
            return LaunchAtAll(caster, targets, TridentProjectileId);
        }

        private static bool NeutronEffect(Actor caster)
        {
            if (!CanLaunchNuclear(caster, 35, false))
                return false;

            Vector2? target = GetEnemyTargets(caster, 1, 10f, 20f).FirstOrNull();
            if (target == null)
                return false;

            SpendGold(caster.city, 35);
            return LaunchAt(caster, target.Value, NeutronProjectileId);
        }

        private static bool EmpEffect(Actor caster)
        {
            if (!CanLaunchConventional(caster, 30))
                return false;

            Vector2? target = GetEnemyTargets(caster, 1, 12f, 20f).FirstOrNull();
            if (target == null)
                return false;

            SpendGold(caster.city, 30);
            return LaunchAt(caster, target.Value, EmpProjectileId);
        }

        private static bool HammerEffect(Actor caster)
        {
            if (!CanLaunchNuclear(caster, 240, true))
                return false;

            Vector2? target = GetEnemyTargets(caster, 1, 24f, 34f).FirstOrNull();
            if (target == null)
                return false;

            SpendGold(caster.city, 240);
            return LaunchAt(caster, target.Value, HammerProjectileId);
        }

        private static bool RuinEffect(Actor caster)
        {
            if (!CanLaunchNuclear(caster, 25, false))
                return false;

            Vector2? target = GetEnemyTargets(caster, 1, 12f, 20f).FirstOrNull();
            if (target == null)
                return false;

            SpendGold(caster.city, 25);
            return LaunchAt(caster, target.Value, RuinProjectileId);
        }

        private static bool CanLaunchConventional(Actor caster, int gold)
        {
            return caster != null && caster.isAlive() && caster.kingdom != null && caster.kingdom.hasEnemies() &&
                caster.city != null && caster.city.amount_gold >= gold &&
                !Vehicles.IsLocalFriendlyTerritoryUnderInvasion(caster);
        }

        private static bool CanLaunchNuclear(Actor caster, int gold, bool requiresLastResort)
        {
            if (!Vehicles.nukesEnabled || caster == null || !caster.isAlive() || caster.kingdom == null ||
                !caster.kingdom.hasEnemies() || caster.city == null || caster.city.amount_gold < gold)
                return false;
            return !requiresLastResort || Vehicles.IsKingdomInNuclearLastResort(caster.kingdom);
        }

        private static void SpendGold(City city, int gold)
        {
            if (city != null)
                city.takeResource("gold", gold);
        }

        private static bool IsConventionalLaunchReady(Actor caster)
        {
            return caster != null &&
                (!ConventionalLaunchStates.TryGetValue(caster, out ConventionalLaunchState state) ||
                 Time.time >= state.readyAt);
        }

        private static void MarkConventionalLaunch(Actor caster, float cooldown)
        {
            ConventionalLaunchState state = ConventionalLaunchStates.GetOrCreateValue(caster);
            state.readyAt = Time.time + cooldown;
        }

        private static Vector2? GetNearestEnemyBoatTarget(Actor caster)
        {
            if (caster == null || caster.kingdom == null || World.world?.units == null)
                return null;

            Actor closest = null;
            float shortestDistance = float.MaxValue;
            foreach (Actor other in World.world.units)
            {
                if (other == null || !other.isAlive() || other.kingdom == null ||
                    !caster.kingdom.isEnemy(other.kingdom) || other.asset == null ||
                    (!other.asset.is_boat && !other.hasTrait("boat")))
                    continue;

                float distance = Vector2.Distance(caster.current_position, other.current_position);
                if (distance < shortestDistance)
                {
                    closest = other;
                    shortestDistance = distance;
                }
            }
            return closest == null ? (Vector2?)null : closest.current_position;
        }

        private static List<Vector2> GetEnemyTargets(Actor caster, int targetCount, float minimumSeparation,
            float blastSafetyRadius, bool reserveTargets = true)
        {
            List<Vector2> targets = new List<Vector2>();
            if (caster == null || caster.kingdom == null || !caster.kingdom.hasEnemies())
                return targets;

            List<City> enemyCities = new List<City>();
            using (var enemies = caster.kingdom.getEnemiesKingdoms())
            {
                foreach (Kingdom enemyKingdom in enemies)
                {
                    if (enemyKingdom?.cities == null)
                        continue;
                    foreach (City city in enemyKingdom.cities)
                    {
                        if (city != null && city.isAlive())
                            enemyCities.Add(city);
                    }
                }
            }

            foreach (City city in enemyCities)
            {
                TryAddTarget(caster, targets, GetCityPriorityTarget(city), minimumSeparation, blastSafetyRadius, reserveTargets);
                if (targets.Count >= targetCount)
                    return targets;
            }

            foreach (City city in enemyCities)
            {
                if (city.buildings != null)
                {
                    foreach (Building building in city.buildings)
                    {
                        if (building?.current_tile != null)
                            TryAddTarget(caster, targets, building.current_tile.pos, minimumSeparation, blastSafetyRadius, reserveTargets);
                        if (targets.Count >= targetCount)
                            return targets;
                    }
                }

                if (city.hasLeader() && city.leader != null && city.leader.isAlive())
                    TryAddTarget(caster, targets, city.leader.current_position, minimumSeparation, blastSafetyRadius, reserveTargets);
                if (targets.Count >= targetCount)
                    return targets;

                WorldTile tile = city.getTile();
                if (tile != null)
                    TryAddTarget(caster, targets, tile.pos, minimumSeparation, blastSafetyRadius, reserveTargets);
                if (targets.Count >= targetCount)
                    return targets;
            }

            // If every valid point is already reserved by another submarine,
            // fall back to real targets only. The local separation check keeps
            // a MIRV/salvo spread intact; this merely avoids an entire class
            // going silent during a busy naval battle.
            if (targets.Count == 0 && reserveTargets)
            {
                List<Vector2> fallback = GetEnemyTargets(caster, targetCount, minimumSeparation,
                    blastSafetyRadius, false);
                foreach (Vector2 candidate in fallback)
                {
                    if (SubmarineTargetReservations.TryReserve(caster, candidate, minimumSeparation,
                            GetTargetReservationLane(caster), true))
                        targets.Add(candidate);
                }
            }
            return targets;
        }

        private static Vector2? GetCityPriorityTarget(City city)
        {
            if (city == null)
                return null;
            if (city.buildings != null && city.buildings.Count > 0)
            {
                Building building = city.buildings.GetRandom();
                if (building?.current_tile != null)
                    return building.current_tile.pos;
            }
            if (city.hasLeader() && city.leader != null && city.leader.isAlive())
                return city.leader.current_position;
            WorldTile tile = city.getTile();
            return tile == null ? (Vector2?)null : tile.pos;
        }

        private static void TryAddTarget(Actor caster, List<Vector2> targets, Vector2? candidate,
            float minimumSeparation, float blastSafetyRadius, bool reserveTarget)
        {
            if (candidate == null || !Vehicles.TryResolveWorldTarget(candidate.Value, out Vector2 resolved) ||
                !Vehicles.IsIntercontinentalMissileTargetInRange(caster, resolved) ||
                !Vehicles.IsStrategicMissileTargetSafe(caster?.kingdom, resolved, blastSafetyRadius))
                return;
            foreach (Vector2 target in targets)
            {
                if (Vector2.Distance(target, resolved) < minimumSeparation)
                    return;
            }
            if (reserveTarget && !SubmarineTargetReservations.TryReserve(caster, resolved, minimumSeparation,
                    GetTargetReservationLane(caster)))
                return;
            targets.Add(resolved);
        }

        private static bool LaunchAtAll(Actor caster, List<Vector2> targets, string projectileId)
        {
            bool launched = false;
            foreach (Vector2 target in targets)
                launched |= LaunchAt(caster, target, projectileId);
            return launched;
        }

        private static bool LaunchAt(Actor caster, Vector2 target, string projectileId, bool allowExplicitSeaThreat = false)
        {
            if (caster == null || !caster.isAlive() || World.world?.projectiles == null ||
                !Vehicles.TryResolveWorldTarget(target, out target))
                return false;

            // Hunter submarines retain their torpedo for close anti-ship work.
            // Every missile warhead, including the hunter's accompanying cruise
            // missiles, must travel beyond the strategic minimum range.
            bool closeRangeTorpedo = string.Equals(projectileId, TorpedoProjectileId, StringComparison.OrdinalIgnoreCase);
            if (!closeRangeTorpedo && !Vehicles.IsIntercontinentalMissileTargetInRange(caster, target))
                return false;

            float blastSafetyRadius = Vehicles.GetMissileBlastSafetyRadius(projectileId);
            if (!Vehicles.IsMissileTargetSafe(caster.kingdom, target, blastSafetyRadius) ||
                !IsValidLaunchTerritory(caster, target, allowExplicitSeaThreat))
                return false;

            Vector3 position = caster.current_position;
            float distance = Vector2.Distance(position, target);
            if (distance < 1f)
                return false;

            Vector3 vector = Toolbox.getNewPoint(position.x, position.y, target.x, target.y, distance);
            Vector3 start = Toolbox.getNewPoint(position.x, position.y, target.x, target.y, caster.stats["size"]);
            start.y += 0.5f;
            try
            {
                World.world.projectiles.spawn(caster, null, projectileId, start, vector);
            }
            catch
            {
                SubmarineTargetReservations.Release(caster, target, GetTargetReservationLane(caster));
                return false;
            }
            if (StatManager.Instance != null)
                StatManager.Instance.SpawnUnit();
            caster.punchTargetAnimation(vector, true, false, 45f);
            return true;
        }

        private static bool IsValidLaunchTerritory(Actor caster, Vector2 target, bool allowExplicitSeaThreat)
        {
            if (caster?.kingdom == null)
                return false;

            WorldTile tile = World.world.GetTile(Mathf.RoundToInt(target.x), Mathf.RoundToInt(target.y));
            City territoryCity = tile?.zone?.city;
            if (territoryCity?.kingdom != null)
                return caster.kingdom.isEnemy(territoryCity.kingdom);

            if (!allowExplicitSeaThreat || World.world?.units == null)
                return false;

            foreach (Actor other in World.world.units)
            {
                if (other == null || !other.isAlive() || other.kingdom == null ||
                    !caster.kingdom.isEnemy(other.kingdom) ||
                    Vector2.Distance(other.current_position, target) > 5f)
                    continue;

                string actorId = other.asset?.id;
                if ((other.asset != null && other.asset.is_boat) || other.hasTrait("boat") ||
                    (!string.IsNullOrEmpty(actorId) && actorId.IndexOf("tornado", StringComparison.OrdinalIgnoreCase) >= 0))
                    return true;
            }

            return false;
        }

        internal static void HandleSpecialWarheadImpact(Projectile projectile)
        {
            if (projectile?.asset == null)
                return;

            string id = projectile.asset.id;
            if (string.Equals(id, EmpProjectileId, StringComparison.OrdinalIgnoreCase))
            {
                DisableModernEnemies(projectile, 18f, 9f);
            }
            else if (string.Equals(id, NeutronProjectileId, StringComparison.OrdinalIgnoreCase))
            {
                DamageAndDisableEnemies(projectile, 8f, 360f, 5f, false, true);
            }
            else if (string.Equals(id, RuinProjectileId, StringComparison.OrdinalIgnoreCase))
            {
                DamageAndDisableEnemies(projectile, 11f, 140f, 6.5f, false, true);
            }
            else if (string.Equals(id, HammerProjectileId, StringComparison.OrdinalIgnoreCase))
            {
                DamageAndDisableEnemies(projectile, 30f, 1250f, 7f, false, false);
            }
        }

        private static void DisableModernEnemies(Projectile projectile, float radius, float duration)
        {
            if (projectile?.kingdom == null)
                return;
            WorldTile tile = projectile.getCurrentTilePosition();
            if (tile == null)
                return;

            int chunks = Mathf.Clamp(Mathf.CeilToInt(radius / 12f), 1, 4);
            foreach (Actor actor in Finder.getUnitsFromChunk(tile, chunks, radius, false))
            {
                if (!IsHostile(projectile, actor) || actor.asset == null ||
                    (!ModernCapPolicy.IsAllowedActor(actor.asset.id) && !actor.asset.is_boat && !actor.hasTrait("boat")))
                    continue;
                actor.makeWait(duration);
            }
        }

        private static void DamageAndDisableEnemies(Projectile projectile, float radius, float damage,
            float duration, bool modernOnly, bool combatOnly)
        {
            if (projectile?.kingdom == null)
                return;
            WorldTile tile = projectile.getCurrentTilePosition();
            if (tile == null)
                return;

            int chunks = Mathf.Clamp(Mathf.CeilToInt(radius / 12f), 1, 4);
            foreach (Actor actor in Finder.getUnitsFromChunk(tile, chunks, radius, false))
            {
                if (!IsHostile(projectile, actor) ||
                    (modernOnly && !ModernCapPolicy.IsAllowedActor(actor.asset?.id)) ||
                    (combatOnly && !IsCombatActor(actor)))
                    continue;
                actor.getHit(damage, true, AttackType.Explosion, projectile.by_who, true, false, true);
                actor.makeWait(duration);
            }
        }

        private static bool IsHostile(Projectile projectile, Actor actor)
        {
            return actor != null && actor.isAlive() && actor.kingdom != null && projectile.kingdom != null &&
                projectile.kingdom.isEnemy(actor.kingdom);
        }

        private static bool IsCombatActor(Actor actor)
        {
            return actor != null && (actor.isWarrior() || actor.asset?.is_boat == true ||
                ModernCapPolicy.IsAllowedActor(actor.asset?.id));
        }

        private static Vector2? FirstOrNull(this List<Vector2> points)
        {
            return points == null || points.Count == 0 ? (Vector2?)null : points[0];
        }
    }

    [HarmonyPatch(typeof(Projectile), "targetReached")]
    internal static class NavalRolesProjectilePatch
    {
        [HarmonyPrefix]
        private static void Prefix(Projectile __instance)
        {
            NavalRoles.HandleSpecialWarheadImpact(__instance);
        }
    }
}
