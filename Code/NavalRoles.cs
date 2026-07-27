using System;
using System.Collections.Generic;
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
        private const string NeutronProjectileId = "modernbox_neutron_warhead";
        private const string EmpProjectileId = "modernbox_emp_warhead";
        private const string HammerProjectileId = "modernbox_hammer_warhead";
        private const string RuinProjectileId = "modernbox_ruin_warhead";

        private static readonly string[] Factions = { "alliance", "harden", "gaia", "horde" };

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
            new RoleDefinition("HunterSubmarine", "SSN Cazador", "hunter_submarine", HunterDecisionId,
                new ConstructionCost(8, 7, 6, 3), false),
            new RoleDefinition("ArsenalSubmarine", "SSGN Arsenal", "arsenal_submarine", ArsenalDecisionId,
                new ConstructionCost(11, 9, 8, 4), true),
            new RoleDefinition("TridentSubmarine", "SSBN Tridente", "trident_submarine", TridentDecisionId,
                new ConstructionCost(15, 13, 12, 8), true),
            new RoleDefinition("NeutronSubmarine", "SSBN Neutrón", "neutron_submarine", NeutronDecisionId,
                new ConstructionCost(13, 11, 10, 6), true),
            new RoleDefinition("EmpSubmarine", "SSBN EMP", "emp_submarine", EmpDecisionId,
                new ConstructionCost(12, 10, 9, 5), true),
            new RoleDefinition("HammerSubmarine", "SSBN Martillo", "hammer_submarine", HammerDecisionId,
                new ConstructionCost(16, 14, 13, 8), true),
            new RoleDefinition("RuinSubmarine", "SSBN Ruina", "ruin_submarine", RuinDecisionId,
                new ConstructionCost(13, 11, 9, 5), true)
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

        internal static string GetRoleLabel(string actorId)
        {
            if (string.IsNullOrEmpty(actorId))
                return null;

            if (actorId.StartsWith("SalvoSubmarine_", StringComparison.OrdinalIgnoreCase))
                return "SSBN Apocalipsis";
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
                description = "Submarino de ataque. Lanza un torpedo convencional contra buques enemigos y una ráfaga de 2 misiles de crucero. No usa armas nucleares.";
            }
            else if (actorId.StartsWith("ArsenalSubmarine_", StringComparison.OrdinalIgnoreCase))
            {
                description = "Submarino de ataque de crucero. Dispara una salva de 6 a 10 misiles convencionales repartidos entre objetivos enemigos. No usa armas nucleares.";
            }
            else if (actorId.StartsWith("TridentSubmarine_", StringComparison.OrdinalIgnoreCase))
            {
                description = "SSBN MIRV. Lanza de 3 a 5 misiles nucleares sólo en una derrota extrema; requiere Guerra nuclear y oro. No deja terreno radiactivo.";
            }
            else if (actorId.StartsWith("NeutronSubmarine_", StringComparison.OrdinalIgnoreCase))
            {
                description = "SSBN de carga táctica. Su explosión nuclear pequeña prioriza unidades cercanas, aturde temporalmente y evita el cambio permanente del terreno.";
            }
            else if (actorId.StartsWith("EmpSubmarine_", StringComparison.OrdinalIgnoreCase))
            {
                description = "SSBN EMP. Detona en el aire e incapacita temporalmente vehículos y unidades modernas enemigas, sin dañar ni transformar el suelo.";
            }
            else if (actorId.StartsWith("HammerSubmarine_", StringComparison.OrdinalIgnoreCase))
            {
                description = "SSBN termonuclear de uso excepcional. Lanza una única carga grande sólo durante una derrota extrema; requiere Guerra nuclear y oro. No crateriza el terreno.";
            }
            else if (actorId.StartsWith("RuinSubmarine_", StringComparison.OrdinalIgnoreCase))
            {
                description = "SSBN radiológico de baja potencia. Afecta temporalmente a unidades cercanas sin crear bioma de radiación ni daño permanente al terreno.";
            }
            else
            {
                description = "SSBN de último recurso. Lanza 4 a 6 Bombas del Zar repartidas entre blancos reales sólo cuando el reino está por caer. Requiere Guerra nuclear y no genera terreno baldío.";
            }
            return true;
        }

        internal static bool IsHeavyWarhead(string projectileId)
        {
            return string.Equals(projectileId, NeutronProjectileId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(projectileId, HammerProjectileId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(projectileId, RuinProjectileId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(projectileId, EmpProjectileId, StringComparison.OrdinalIgnoreCase);
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
            submarine.addDecision(role.DecisionId);
            submarine.addDecision("random_swim");
            submarine.default_attack = "MissileSystemmissile";
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
            RegisterDecision(HunterDecisionId, 55, HunterEffect);
            RegisterDecision(ArsenalDecisionId, 180, ArsenalEffect);
            RegisterDecision(TridentDecisionId, 720, TridentEffect);
            RegisterDecision(NeutronDecisionId, 360, NeutronEffect);
            RegisterDecision(EmpDecisionId, 300, EmpEffect);
            RegisterDecision(HammerDecisionId, 900, HammerEffect);
            RegisterDecision(RuinDecisionId, 420, RuinEffect);
        }

        private static void RegisterDecision(string id, int cooldown, Func<Actor, bool> action)
        {
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
            CreateSafeBlast("modernbox_neutron_blast", 260, 6, false);
            CreateSafeBlast("modernbox_ruin_blast", 110, 4, false);
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
            CreateProjectile(TorpedoProjectileId, "missileartillery", "modern_cap_missile_blast", 4, 62f,
                0.42f, "fx_firebomb_explosion", false);
            CreateProjectile(NeutronProjectileId, "NUKER", "modernbox_neutron_blast", 7, 130f,
                0.48f, "fx_explosion_nuke_atomic", true);
            CreateProjectile(EmpProjectileId, "NUKER", null, 0, 145f,
                0.46f, "fx_explosion_nuke_atomic", true);
            CreateProjectile(HammerProjectileId, "NUKER", "modernbox_hammer_blast", 34, 110f,
                0.72f, "fx_explosion_huge", true);
            CreateProjectile(RuinProjectileId, "NUKER", "modernbox_ruin_blast", 9, 125f,
                0.50f, "fx_explosion_nuke_atomic", true);
        }

        private static void CreateProjectile(string id, string texture, string terraformId, int terraformRange,
            float speed, float scale, string effect, bool nuclearSound)
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
            projectile.end_effect_scale = nuclearSound ? 0.85f : 0.55f;
            projectile.trail_effect_enabled = true;
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
            if (!CanLaunchConventional(caster, 15))
                return false;

            Vector2? target = GetNearestEnemyBoatTarget(caster) ?? GetEnemyTargets(caster, 1, 6f).FirstOrNull();
            if (target == null)
                return false;

            SpendGold(caster.city, 15);
            bool launched = LaunchAt(caster, target.Value, TorpedoProjectileId);
            launched |= LaunchAt(caster, target.Value + new Vector2(2.5f, 1.5f), "missileartillery");
            launched |= LaunchAt(caster, target.Value + new Vector2(-2.5f, -1.5f), "missileartillery");
            return launched;
        }

        private static bool ArsenalEffect(Actor caster)
        {
            if (!CanLaunchConventional(caster, 45))
                return false;

            int count = UnityEngine.Random.Range(6, 11);
            List<Vector2> targets = GetEnemyTargets(caster, count, 8f);
            if (targets.Count == 0)
                return false;

            SpendGold(caster.city, 45);
            return LaunchAtAll(caster, targets, "missileartillery");
        }

        private static bool TridentEffect(Actor caster)
        {
            if (!CanLaunchNuclear(caster, 180, true))
                return false;

            int count = UnityEngine.Random.Range(3, 6);
            List<Vector2> targets = GetEnemyTargets(caster, count, 12f);
            if (targets.Count == 0)
                return false;

            SpendGold(caster.city, 180);
            return LaunchAtAll(caster, targets, "NUKER");
        }

        private static bool NeutronEffect(Actor caster)
        {
            if (!CanLaunchNuclear(caster, 90, false))
                return false;

            Vector2? target = GetEnemyTargets(caster, 1, 6f).FirstOrNull();
            if (target == null)
                return false;

            SpendGold(caster.city, 90);
            return LaunchAt(caster, target.Value, NeutronProjectileId);
        }

        private static bool EmpEffect(Actor caster)
        {
            if (!CanLaunchConventional(caster, 70))
                return false;

            Vector2? target = GetEnemyTargets(caster, 1, 8f).FirstOrNull();
            if (target == null)
                return false;

            SpendGold(caster.city, 70);
            return LaunchAt(caster, target.Value, EmpProjectileId);
        }

        private static bool HammerEffect(Actor caster)
        {
            if (!CanLaunchNuclear(caster, 240, true))
                return false;

            Vector2? target = GetEnemyTargets(caster, 1, 12f).FirstOrNull();
            if (target == null)
                return false;

            SpendGold(caster.city, 240);
            return LaunchAt(caster, target.Value, HammerProjectileId);
        }

        private static bool RuinEffect(Actor caster)
        {
            if (!CanLaunchNuclear(caster, 65, false))
                return false;

            Vector2? target = GetEnemyTargets(caster, 1, 8f).FirstOrNull();
            if (target == null)
                return false;

            SpendGold(caster.city, 65);
            return LaunchAt(caster, target.Value, RuinProjectileId);
        }

        private static bool CanLaunchConventional(Actor caster, int gold)
        {
            return caster != null && caster.isAlive() && caster.kingdom != null && caster.kingdom.hasEnemies() &&
                caster.city != null && caster.city.amount_gold >= gold;
        }

        private static bool CanLaunchNuclear(Actor caster, int gold, bool requiresLastResort)
        {
            if (!Vehicles.nukesEnabled || !CanLaunchConventional(caster, gold))
                return false;
            return !requiresLastResort || Vehicles.IsKingdomInNuclearLastResort(caster.kingdom);
        }

        private static void SpendGold(City city, int gold)
        {
            if (city != null)
                city.takeResource("gold", gold);
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

        private static List<Vector2> GetEnemyTargets(Actor caster, int targetCount, float minimumSeparation)
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
                TryAddTarget(targets, GetCityPriorityTarget(city), minimumSeparation);
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
                            TryAddTarget(targets, building.current_tile.pos, minimumSeparation);
                        if (targets.Count >= targetCount)
                            return targets;
                    }
                }

                if (city.hasLeader() && city.leader != null && city.leader.isAlive())
                    TryAddTarget(targets, city.leader.current_position, minimumSeparation);
                if (targets.Count >= targetCount)
                    return targets;

                WorldTile tile = city.getTile();
                if (tile != null)
                    TryAddTarget(targets, tile.pos, minimumSeparation);
                if (targets.Count >= targetCount)
                    return targets;
            }

            // A tiny city may not provide enough distinct buildings. Fallbacks
            // remain centred on a real enemy position and are separated enough
            // to keep multi-warhead strikes from collapsing into one pixel.
            if (targets.Count > 0)
            {
                Vector2 center = targets[0];
                Vector2[] offsets =
                {
                    new Vector2(15f, 0f), new Vector2(-15f, 0f), new Vector2(0f, 15f), new Vector2(0f, -15f),
                    new Vector2(12f, 12f), new Vector2(-12f, 12f), new Vector2(12f, -12f), new Vector2(-12f, -12f),
                    new Vector2(24f, 0f), new Vector2(-24f, 0f)
                };
                foreach (Vector2 offset in offsets)
                {
                    TryAddTarget(targets, center + offset, minimumSeparation);
                    if (targets.Count >= targetCount)
                        break;
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

        private static void TryAddTarget(List<Vector2> targets, Vector2? candidate, float minimumSeparation)
        {
            if (candidate == null)
                return;
            foreach (Vector2 target in targets)
            {
                if (Vector2.Distance(target, candidate.Value) < minimumSeparation)
                    return;
            }
            targets.Add(candidate.Value);
        }

        private static bool LaunchAtAll(Actor caster, List<Vector2> targets, string projectileId)
        {
            bool launched = false;
            foreach (Vector2 target in targets)
                launched |= LaunchAt(caster, target, projectileId);
            return launched;
        }

        private static bool LaunchAt(Actor caster, Vector2 target, string projectileId)
        {
            if (caster == null || !caster.isAlive() || World.world?.projectiles == null)
                return false;

            Vector3 position = caster.current_position;
            float distance = Vector2.Distance(position, target);
            if (distance < 1f)
                return false;

            Vector3 vector = Toolbox.getNewPoint(position.x, position.y, target.x, target.y, distance);
            Vector3 start = Toolbox.getNewPoint(position.x, position.y, target.x, target.y, caster.stats["size"]);
            start.y += 0.5f;
            World.world.projectiles.spawn(caster, null, projectileId, start, vector);
            if (StatManager.Instance != null)
                StatManager.Instance.SpawnUnit();
            caster.punchTargetAnimation(vector, true, false, 45f);
            return true;
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
