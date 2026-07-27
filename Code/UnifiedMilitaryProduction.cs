using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ModernBox
{
    /// <summary>
    /// One-shot production for Unitpotential units.  This is intentionally not a
    /// scheduler: WorldBox asks for a unit and the city either pays for one valid
    /// vehicle or keeps its base unit.
    /// </summary>
    internal static class UnifiedMilitaryProduction
    {
        private sealed class Candidate
        {
            internal string id;
            internal string tier;
            internal ConstructionCost cost;
        }

        internal static bool TryTransform(Actor actor, WorldTile requestedTile)
        {
            if (actor?.asset == null || actor.asset.id != "baseWarUnit" ||
                !actor.inMapBorder() || actor.kingdom == null || actor.city == null ||
                !actor.city.hasLeader() || !Traits.vehiclesAllowed)
                return false;

            Actor leader = actor.city.leader;
            if (leader?.asset == null)
                return false;

            City city = actor.city;
            string species = leader.subspecies?.data?.species_id;
            if (string.IsNullOrEmpty(species))
                species = leader.asset.id;

            int militaryLevel = MilitaryProgressionController.GetLevel(city);
            // A mature city without even one artillery piece used to be at the
            // mercy of the weighted role roll. Its small vehicle quota would
            // often fill with regular units first and artillery never got a
            // production opportunity. Reserve only the first artillery slot;
            // later choices remain fully weighted and subject to normal caps.
            Candidate selected = SelectMissingArtillery(species, city, actor, militaryLevel);
            if (selected != null)
            {
                // The guaranteed first artillery platform is deliberately the
                // whole exception. It consumes the same resources as any other
                // heavy unit and does not bypass the artillery cap.
            }
            else if (NeedsDefensiveLauncher(city) &&
                MilitaryDoctrineService.ShouldReserveDefensiveLauncher(city))
            {
                // Keep this reserved chassis pending until the city can pay for
                // its launcher for defensive/strategic doctrines; otherwise a
                // cheaper vehicle could consume the one defensive slot forever.
                selected = SelectDefensiveLauncher(city);
                if (selected == null)
                    return false;
            }
            else
            {
                string role = PickRole(city, militaryLevel);
                List<Candidate> candidates = CollectCandidates(species, role, city, actor, militaryLevel);
                if (candidates.Count == 0 && role != "offensive")
                    candidates = CollectCandidates(species, "offensive", city, actor, militaryLevel);
                if (candidates.Count == 0 && role != "heavy")
                    candidates = CollectCandidates(species, "heavy", city, actor, militaryLevel);
                if (candidates.Count == 0)
                    return false;
                selected = SelectWeightedAffordableCandidate(candidates, city);
            }
            if (selected == null)
                return false;

            WorldTile spawnTile = requestedTile ?? actor.current_tile;
            Actor produced = World.world.units.createNewUnit(
                selected.id, spawnTile, pMiracleSpawn: false, 0f, null, null,
                pSpawnWithItems: false);
            if (produced == null)
                return false;

            produced.setKingdom(actor.kingdom);
            produced.setCity(city);
            city.spendResourcesForBuildingAsset(selected.cost);
            ModernDiplomacyController.ApplyArmsCredit(city);
            EffectsLibrary.spawn("fx_spawn", produced.current_tile);
            ActionLibrary.removeUnit(actor);
            actor.setTransformed();
            return true;
        }

        /// <summary>
        /// Civilian, non-fantasy route into the existing vehicle transformation
        /// rules.  ModernBox used to receive baseWarUnit chassis exclusively
        /// from ideology special effects; those effects are correctly disabled
        /// with fantasy systems.  This method commissions a temporary normal
        /// chassis, applies the same costs/caps/tiers as TryTransform, and
        /// removes it immediately if no legal vehicle can be paid for.
        /// </summary>
        internal static bool TryCommissionCityUnit(City city)
        {
            if (city == null || !city.isAlive() || city.kingdom == null ||
                !city.kingdom.isCiv() || !city.hasLeader() ||
                MilitaryProgressionController.GetLevel(city) < 2 ||
                !Traits.vehiclesAllowed || World.world?.units == null)
                return false;

            WorldTile spawnTile = FindSafeLandTile(city);
            if (spawnTile == null)
                return false;

            Actor chassis = World.world.units.createNewUnit(
                "baseWarUnit", spawnTile, pMiracleSpawn: false, 0f, null, null,
                pSpawnWithItems: false);
            if (chassis == null)
                return false;

            chassis.setKingdom(city.kingdom);
            chassis.setCity(city);

            bool transformed = false;
            try
            {
                transformed = TryTransform(chassis, spawnTile);
                return transformed;
            }
            finally
            {
                // A failed candidate check must never leave a free infantry
                // chassis behind in the city.
                if (!transformed && chassis != null && chassis.isAlive())
                    ActionLibrary.removeUnit(chassis);
            }
        }

        internal static bool NeedsDefensiveLauncher(City city)
        {
            return IsValidLauncherCity(city) &&
                MilitaryProgressionController.CanBuildDefensiveLauncher(city) &&
                ModernCapPolicy.CountMissileLaunchers(city) < MilitaryQuotaService.GetMissileLauncherCap(city);
        }

        private static Candidate SelectDefensiveLauncher(City city)
        {
            if (!TryGetDefensiveLauncher(city, out string launcherId, out ConstructionCost cost))
                return null;

            return new Candidate { id = launcherId, tier = "modern", cost = cost };
        }

        internal static bool TryBuildDefensiveLauncher(City city)
        {
            if (!TryGetDefensiveLauncher(city, out string launcherId, out ConstructionCost cost))
                return false;

            return TryBuildDirectDefenseAsset(
                city,
                new Candidate { id = launcherId, tier = "modern", cost = cost },
                "lanzamisiles");
        }

        /// <summary>
        /// The slow launcher scheduler chooses between the missing launcher,
        /// fixed-wing and rotary-wing slots. The production rate does not
        /// increase: one global cycle still creates at most one paid asset.
        /// A developed kingdom works toward a mixed air arm: at least one
        /// fighter and one bomber, distributed across its cities whenever
        /// possible. A mature single-city kingdom may eventually keep both.
        /// Neither consumes the ordinary land quota.
        /// </summary>
        internal static bool TryBuildDefensiveOrAirAsset(City city)
        {
            Candidate launcher = SelectDefensiveLauncher(city);
            Candidate fixedWing = SelectDefensiveAirframe(city);
            Candidate helicopter = SelectDefensiveHelicopter(city);
            if (launcher == null && fixedWing == null && helicopter == null)
                return false;

            Candidate selected;
            string role;
            if (launcher == null && helicopter == null)
            {
                selected = fixedWing;
                role = "aviaciÃ³n fija";
            }
            else if (launcher == null && fixedWing == null)
            {
                selected = helicopter;
                role = "helicÃ³ptero";
            }
            else if (fixedWing == null && helicopter == null)
            {
                selected = launcher;
                role = "lanzamisiles";
            }
            else
            {
                float launcherWeight = MilitaryDoctrineService.GetDefensiveLauncherPreference(city?.kingdom);
                float fixedWingWeight = fixedWing == null ? 0f :
                    MilitaryDoctrineService.GetRoleWeight(city?.kingdom, "air");
                float helicopterWeight = helicopter == null ? 0f :
                    Mathf.Max(0.75f, MilitaryDoctrineService.GetRoleWeight(city?.kingdom, "air") * 0.85f);

                // A port already fielding missile submarines should answer a
                // war with conventional bomber support as well. This only
                // biases the empty fixed-wing slot; it neither spawns a free
                // bomber nor removes the city's fighter/helicopter capacity.
                if (fixedWing != null && IsBomber(fixedWing.id) && HasMissileFleetInWar(city))
                    fixedWingWeight *= 1.75f;

                float totalWeight = (launcher == null ? 0f : launcherWeight) +
                    fixedWingWeight + helicopterWeight;
                float roll = UnityEngine.Random.Range(0f, totalWeight);
                if (launcher != null && (roll -= launcherWeight) < 0f)
                {
                    selected = launcher;
                    role = "lanzamisiles";
                }
                else if (fixedWing != null && (roll -= fixedWingWeight) < 0f)
                {
                    selected = fixedWing;
                    role = "aviaciÃ³n fija";
                }
                else
                {
                    selected = helicopter;
                    role = "helicÃ³ptero";
                }
            }

            return TryBuildDirectDefenseAsset(city, selected, role);
        }

        internal static bool TryGetDefensiveLauncher(City city, out string launcherId, out ConstructionCost cost)
        {
            launcherId = null;
            cost = default(ConstructionCost);
            if (!NeedsDefensiveLauncher(city))
                return false;

            Actor leader = city.leader;
            string species = leader.subspecies?.data?.species_id;
            if (string.IsNullOrEmpty(species))
                species = leader.asset?.id;

            if (string.IsNullOrEmpty(species) ||
                !Traits.CartTransformations.CartTransformationsModernRoles.TryGetValue(species, out var roles) ||
                !roles.TryGetValue("heavy", out var ids))
                return false;

            launcherId = ids.FirstOrDefault(id =>
                !string.IsNullOrEmpty(id) && id.StartsWith("MissileSystem_", StringComparison.OrdinalIgnoreCase));
            ActorAsset launcher = AssetManager.actor_library.get(launcherId);
            if (launcher == null || string.IsNullOrEmpty(launcher.default_attack) ||
                AssetManager.items.get(launcher.default_attack) == null)
                return false;

            cost = GetCost(launcherId, "modern");
            return city.hasEnoughResourcesFor(cost);
        }

        private static Candidate SelectDefensiveAirframe(City city)
        {
            if (!NeedsFixedWingAirframe(city))
                return null;

            Actor leader = city.leader;
            string species = leader.subspecies?.data?.species_id;
            if (string.IsNullOrEmpty(species))
                species = leader.asset?.id;

            if (string.IsNullOrEmpty(species) ||
                !Traits.CartTransformations.CartTransformationsModernRoles.TryGetValue(species, out var roles) ||
                !roles.TryGetValue("air", out var ids))
                return null;

            List<Candidate> affordable = ids
                .Where(IsLauncherCycleAircraft)
                .Distinct()
                .Select(id => new
                {
                    id,
                    asset = AssetManager.actor_library.get(id),
                    cost = GetCost(id, "modern")
                })
                .Where(entry => entry.asset != null &&
                    !string.IsNullOrEmpty(entry.asset.default_attack) &&
                    AssetManager.items.get(entry.asset.default_attack) != null &&
                    CanAddFixedWingAirframe(city, entry.id, null) &&
                    city.hasEnoughResourcesFor(entry.cost))
                .Select(entry => new Candidate
                {
                    id = entry.id,
                    tier = "modern",
                    cost = entry.cost
                })
                .ToList();
            if (affordable.Count == 0)
                return null;

            List<Candidate> fighters = affordable
                .Where(candidate => candidate.id.StartsWith("FighterJet_", StringComparison.OrdinalIgnoreCase))
                .ToList();
            List<Candidate> bombers = affordable
                .Where(candidate => candidate.id.StartsWith("Bomber_", StringComparison.OrdinalIgnoreCase))
                .ToList();

            bool kingdomNeedsFighter = !KingdomHasFixedWingRole(city.kingdom, true);
            bool kingdomNeedsBomber = !KingdomHasFixedWingRole(city.kingdom, false);
            if (kingdomNeedsFighter || kingdomNeedsBomber)
            {
                // A realm completes the missing part of its air arm before it
                // starts duplicating airframes. The normal doctrine weights
                // decide only when both roles are still absent.
                if (kingdomNeedsFighter && !kingdomNeedsBomber && fighters.Count > 0)
                    return fighters[Randy.randomInt(0, fighters.Count)];
                if (kingdomNeedsBomber && !kingdomNeedsFighter && bombers.Count > 0)
                    return bombers[Randy.randomInt(0, bombers.Count)];
            }

            // Fighters remain the ordinary choice. Air/strategic doctrines make
            // the more expensive bomber somewhat likelier. A missile fleet at
            // war raises that preference so sea-launched strikes are naturally
            // accompanied by conventional bombing instead of replacing it.
            float bomberChance = Mathf.Clamp(
                0.25f * MilitaryDoctrineService.GetRoleWeight(city.kingdom, "air"),
                0.20f,
                0.40f);
            if (HasMissileFleetInWar(city))
                bomberChance = Mathf.Max(bomberChance, 0.58f);
            List<Candidate> preferred = bombers.Count > 0 &&
                (fighters.Count == 0 || UnityEngine.Random.value < bomberChance)
                    ? bombers
                    : fighters;
            if (preferred.Count == 0)
                preferred = affordable;
            return preferred[Randy.randomInt(0, preferred.Count)];
        }

        private static bool NeedsFixedWingAirframe(City city)
        {
            if (!IsValidLauncherCity(city) ||
                !MilitaryProgressionController.CanBuildDefensiveLauncher(city))
                return false;

            if (!HasFixedWingAirframe(city, null))
                return true;

            // Let a mature one-city realm complete its bomber/fighter pair,
            // but do not give every settlement a second fixed-wing slot.
            return MilitaryProgressionController.GetLevel(city) >= 4 &&
                ((!HasFixedWingRole(city, true, null) && !KingdomHasFixedWingRole(city.kingdom, true)) ||
                 (!HasFixedWingRole(city, false, null) && !KingdomHasFixedWingRole(city.kingdom, false)));
        }

        private static Candidate SelectDefensiveHelicopter(City city)
        {
            if (!NeedsHelicopterAirframe(city))
                return null;

            Actor leader = city.leader;
            string species = leader.subspecies?.data?.species_id;
            if (string.IsNullOrEmpty(species))
                species = leader.asset?.id;

            if (string.IsNullOrEmpty(species) ||
                !Traits.CartTransformations.CartTransformationsModernRoles.TryGetValue(species, out var roles) ||
                !roles.TryGetValue("air", out var ids))
                return null;

            List<Candidate> affordable = ids
                .Where(IsHelicopter)
                .Distinct()
                .Select(id => new
                {
                    id,
                    asset = AssetManager.actor_library.get(id),
                    cost = GetCost(id, "modern")
                })
                .Where(entry => entry.asset != null &&
                    !string.IsNullOrEmpty(entry.asset.default_attack) &&
                    AssetManager.items.get(entry.asset.default_attack) != null &&
                    city.hasEnoughResourcesFor(entry.cost))
                .Select(entry => new Candidate
                {
                    id = entry.id,
                    tier = "modern",
                    cost = entry.cost
                })
                .ToList();
            if (affordable.Count == 0)
                return null;

            return affordable[Randy.randomInt(0, affordable.Count)];
        }

        private static bool NeedsHelicopterAirframe(City city)
        {
            // Helicopters are the second, advanced air slot. They require the
            // same population/infrastructure gate as general aviation, but do
            // not share the fighter/bomber slot.
            return IsValidLauncherCity(city) &&
                MilitaryProgressionController.GetLevel(city) >= 4 &&
                !HasHelicopterAirframe(city, null);
        }

        private static bool HasFixedWingAirframe(City city, Actor transforming)
        {
            if (city?.units == null)
                return false;

            foreach (Actor unit in city.units)
            {
                if (unit == null || unit == transforming || !unit.isAlive())
                    continue;
                if (IsFixedWingAircraft(unit.asset?.id))
                    return true;
            }
            return false;
        }

        private static bool HasFixedWingRole(City city, bool fighter, Actor transforming)
        {
            if (city?.units == null)
                return false;

            foreach (Actor unit in city.units)
            {
                if (unit == null || unit == transforming || !unit.isAlive())
                    continue;
                string id = unit.asset?.id;
                if (fighter ? IsFighter(id) : IsBomber(id))
                    return true;
            }
            return false;
        }

        private static bool KingdomHasFixedWingRole(Kingdom kingdom, bool fighter)
        {
            if (kingdom?.cities == null)
                return false;

            foreach (City city in kingdom.cities)
            {
                if (HasFixedWingRole(city, fighter, null))
                    return true;
            }
            return false;
        }

        private static bool CanAddFixedWingAirframe(City city, string id, Actor transforming)
        {
            bool fighter = IsFighter(id);
            bool bomber = IsBomber(id);
            if (!fighter && !bomber)
                return false;
            if (HasFixedWingRole(city, fighter, transforming))
                return false;
            if (!HasFixedWingAirframe(city, transforming))
                return true;

            // A second airframe is only the missing national role, and only
            // for an advanced city when no sister city can provide it yet.
            return MilitaryProgressionController.GetLevel(city) >= 4 &&
                !KingdomHasFixedWingRole(city?.kingdom, fighter);
        }

        private static bool HasHelicopterAirframe(City city, Actor transforming)
        {
            if (city?.units == null)
                return false;

            foreach (Actor unit in city.units)
            {
                if (unit == null || unit == transforming || !unit.isAlive())
                    continue;
                if (IsHelicopter(unit.asset?.id))
                    return true;
            }
            return false;
        }

        private static bool IsFixedWingAircraft(string id)
        {
            return !string.IsNullOrEmpty(id) &&
                (IsLauncherCycleAircraft(id) ||
                 id == "F55FighterJet" ||
                 id == "americanbomberww" ||
                 id == "biplane" ||
                 id == "fighterww");
        }

        private static bool IsLauncherCycleAircraft(string id)
        {
            return !string.IsNullOrEmpty(id) &&
                (id.StartsWith("FighterJet_", StringComparison.OrdinalIgnoreCase) ||
                 id.StartsWith("Bomber_", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsHelicopter(string id)
        {
            return !string.IsNullOrEmpty(id) &&
                id.StartsWith("Heli_", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsBomber(string id)
        {
            return !string.IsNullOrEmpty(id) &&
                id.StartsWith("Bomber_", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsFighter(string id)
        {
            return !string.IsNullOrEmpty(id) &&
                (id.StartsWith("FighterJet_", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(id, "F55FighterJet", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(id, "fighterww", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(id, "biplane", StringComparison.OrdinalIgnoreCase));
        }

        private static bool HasMissileFleetInWar(City city)
        {
            if (city?.kingdom == null || !city.kingdom.hasEnemies() || city.units == null)
                return false;

            foreach (Actor unit in city.units)
            {
                if (unit != null && unit.isAlive() &&
                    NavalRoles.IsAnyModernSubmarine(unit.asset?.id))
                    return true;
            }
            return false;
        }

        internal static bool IsDedicatedDefenseSlot(string id)
        {
            return IsFixedWingAircraft(id) ||
                IsHelicopter(id) ||
                ModernCapPolicy.IsMissileLauncher(id);
        }

        private static bool TryBuildDirectDefenseAsset(City city, Candidate candidate, string role)
        {
            if (city == null || candidate == null || string.IsNullOrEmpty(candidate.id))
                return false;

            WorldTile spawnTile = FindSafeLandTile(city);
            if (spawnTile == null)
                return false;

            Actor produced;
            try
            {
                produced = World.world.units.createNewUnit(
                    candidate.id, spawnTile, pMiracleSpawn: false, 0f, null, null,
                    pSpawnWithItems: false);
            }
            catch (Exception ex)
            {
                ModernBoxLogger.Error("[MX.DefenseProduction] " + role +
                    " spawn failed exceptionally: " + ex.Message);
                return false;
            }

            if (produced == null)
            {
                ModernBoxLogger.Warning("[MX.DefenseProduction] WorldBox rejected " +
                    role + " spawn for " + candidate.id + ".");
                return false;
            }

            produced.setKingdom(city.kingdom);
            produced.setCity(city);
            city.spendResourcesForBuildingAsset(candidate.cost);
            ModernDiplomacyController.ApplyArmsCredit(city);
            EffectsLibrary.spawn("fx_spawn", produced.current_tile);
            ModernBoxLogger.Log("[MX.DefenseProduction] Built " + candidate.id +
                " (" + role + ") for a city with population " +
                city.getPopulationPeople() + ".");
            return true;
        }

        internal static bool HasMissileLauncher(City city, Actor transforming)
        {
            foreach (Actor unit in city.units)
            {
                if (unit == null || unit == transforming || !unit.isAlive())
                    continue;
                if (unit.asset?.id?.StartsWith("MissileSystem_", StringComparison.OrdinalIgnoreCase) == true)
                    return true;
            }
            return false;
        }

        private static bool IsValidLauncherCity(City city)
        {
            if (!Traits.vehiclesAllowed || city == null || !city.isAlive() ||
                city.kingdom == null || !city.kingdom.isCiv() || !city.hasLeader())
                return false;

            Actor leader = city.leader;
            return leader != null && leader.isAlive() && leader.asset != null &&
                World.world?.units != null;
        }

        private static WorldTile FindSafeLandTile(City city)
        {
            WorldTile center = city.getTile();
            if (IsSafeLandTile(center))
                return center;

            WorldTile leaderTile = city.leader?.current_tile;
            if (IsSafeLandTile(leaderTile))
                return leaderTile;

            if (city.buildings != null)
            {
                foreach (Building building in city.buildings)
                {
                    if (IsSafeLandTile(building?.current_tile))
                        return building.current_tile;
                }
            }

            if (center?.region?.tiles != null)
            {
                for (int attempt = 0; attempt < 16; attempt++)
                {
                    WorldTile candidate = center.region.tiles.GetRandom();
                    if (IsSafeLandTile(candidate))
                        return candidate;
                }
            }

            return null;
        }

        private static bool IsSafeLandTile(WorldTile tile)
        {
            return tile?.Type != null && tile.Type.ground &&
                !tile.Type.block && !tile.Type.ocean && !tile.Type.liquid;
        }

        private static List<Candidate> CollectCandidates(
            string species,
            string role,
            City city,
            Actor transforming,
            int militaryLevel)
        {
            var result = new List<Candidate>();
            AddTier(result, Traits.CartTransformations.CartTransformationsModernRoles, "modern", species, role, city, transforming, militaryLevel);
            AddTier(result, Traits.CartTransformations.CartTransformationsRenaissanceRoles, "renaissance", species, role, city, transforming, militaryLevel);
            AddTier(result, Traits.CartTransformations.CartTransformationsMedievalRoles, "medieval", species, role, city, transforming, militaryLevel);
            return result;
        }

        private static void AddTier(List<Candidate> result,
            Dictionary<string, Dictionary<string, List<string>>> table,
            string tier, string species, string role, City city, Actor transforming,
            int militaryLevel)
        {
            if (table == null || !table.TryGetValue(species, out var roles) ||
                !roles.TryGetValue(role, out var ids))
                return;

            foreach (string id in ids.Distinct())
            {
                ActorAsset asset = AssetManager.actor_library.get(id);
                if (!ModernCapPolicy.IsLandMilitaryActor(id) ||
                    !MilitaryProgressionController.IsRoleUnlocked(militaryLevel, tier, role, id) ||
                    !WithinCityCaps(city, id, transforming) || asset == null ||
                    string.IsNullOrEmpty(asset.default_attack) ||
                    AssetManager.items.get(asset.default_attack) == null)
                    continue;

                result.Add(new Candidate { id = id, tier = tier, cost = GetCost(id, tier) });
            }
        }

        private static Candidate SelectWeightedAffordableCandidate(List<Candidate> candidates, City city)
        {
            var affordable = candidates.Where(c => city.hasEnoughResourcesFor(c.cost)).ToList();
            if (affordable.Count == 0)
                return null;

            float roll = UnityEngine.Random.Range(0f, 1f);
            string chosenTier = roll < .65f ? "modern" : roll < .90f ? "renaissance" : "medieval";
            string[] fallback = new[] { chosenTier, "modern", "renaissance", "medieval" }.Distinct().ToArray();

            foreach (string tier in fallback)
            {
                List<Candidate> tierCandidates = affordable.Where(c => c.tier == tier).ToList();
                if (tierCandidates.Count > 0)
                    return tierCandidates[Randy.randomInt(0, tierCandidates.Count)];
            }
            return null;
        }

        private static Candidate SelectMissingArtillery(string species, City city,
            Actor transforming, int militaryLevel)
        {
            if (militaryLevel < 3 || HasConventionalArtillery(city, transforming))
                return null;

            List<Candidate> artillery = CollectCandidates(
                    species, "offensive", city, transforming, militaryLevel)
                .Where(candidate => ModernCapPolicy.IsConventionalArtillery(candidate.id))
                .Where(candidate => city.hasEnoughResourcesFor(candidate.cost))
                .ToList();
            if (artillery.Count == 0)
                return null;

            return artillery[Randy.randomInt(0, artillery.Count)];
        }

        private static bool HasConventionalArtillery(City city, Actor transforming)
        {
            if (city?.units == null)
                return false;

            foreach (Actor unit in city.units)
            {
                if (unit == null || unit == transforming || !unit.isAlive())
                    continue;
                if (ModernCapPolicy.IsConventionalArtillery(unit.asset?.id))
                    return true;
            }
            return false;
        }

        private static bool WithinCityCaps(City city, string candidateId, Actor transforming)
        {
            int totalCap = MilitaryQuotaService.GetLandUnitCap(city);
            int artilleryCap = MilitaryQuotaService.GetArtilleryCap(city);
            int total = 0;
            int artillery = 0;

            foreach (Actor unit in city.units)
            {
                if (unit == null || unit == transforming || !unit.isAlive())
                    continue;
                string id = unit.asset?.id;
                bool dedicatedSlot = IsDedicatedDefenseSlot(id);
                if (id == "baseWarUnit" ||
                    (ModernCapPolicy.IsLandMilitaryActor(id) && !dedicatedSlot))
                    total++;
                if (ModernCapPolicy.IsConventionalArtillery(id))
                    artillery++;
            }

            if (ModernCapPolicy.IsMissileLauncher(candidateId))
            {
                // Launchers have their own two-per-city budget and must
                // not consume the conventional howitzer/cannon slot.
                return ModernCapPolicy.CountMissileLaunchers(city, transforming) <
                    MilitaryQuotaService.GetMissileLauncherCap(city);
            }

            if (IsFixedWingAircraft(candidateId))
            {
                return CanAddFixedWingAirframe(city, candidateId, transforming);
            }

            if (IsHelicopter(candidateId))
            {
                // Rotary aircraft form a separate, one-per-city support slot.
                // It is deliberately not counted as a land vehicle, so adding
                // it does not crowd out normal WorldBox soldiers or artillery.
                return !HasHelicopterAirframe(city, transforming);
            }

            if (ModernCapPolicy.IsConventionalArtillery(candidateId))
            {
                // Every mature city can fit one expensive artillery platform
                // even when its ordinary vehicle budget is full. A second one
                // still needs both its population quota and spare normal room.
                // More importantly, it waits until eligible sister cities have
                // their first gun, so a rich capital cannot hoard artillery.
                if (artillery >= artilleryCap)
                    return false;
                if (artillery > 0 && !AllEligibleKingdomCitiesHaveArtillery(city.kingdom))
                    return false;
                return total < totalCap || artillery == 0;
            }

            return total < totalCap;
        }

        private static bool AllEligibleKingdomCitiesHaveArtillery(Kingdom kingdom)
        {
            if (kingdom?.cities == null)
                return true;

            foreach (City sibling in kingdom.cities)
            {
                if (sibling == null || !sibling.isAlive() || !sibling.hasLeader() ||
                    MilitaryProgressionController.GetLevel(sibling) < 3)
                    continue;
                if (!HasConventionalArtillery(sibling, null))
                    return false;
            }
            return true;
        }

        private static string PickRole(City city, int militaryLevel)
        {
            return MilitaryDoctrineService.PickLandRole(city?.kingdom, militaryLevel);
        }

        private static ConstructionCost GetCost(string id, string tier)
        {
            if (id.StartsWith("Bomber_", StringComparison.OrdinalIgnoreCase))
                return new ConstructionCost(9, 8, 6, 3);
            if (id.StartsWith("howitzer_", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith("Heli_", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith("FighterJet_", StringComparison.OrdinalIgnoreCase) ||
                id == "F55FighterJet" || id == "americanbomberww" ||
                id == "biplane" || id == "fighterww" || id == "Zeppelin" || id == "EliteZeppelin")
                return new ConstructionCost(7, 6, 4, 2);
            if (id.StartsWith("Tank_", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith("MissileSystem_", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith("wheeledtank_", StringComparison.OrdinalIgnoreCase) ||
                id == "AbramTank")
                // Keep heavy systems rare, but do not require a stockpile that
                // the fixed Medieval economic buildings can never hold.
                return new ConstructionCost(7, 6, 4, 2);
            if (tier == "renaissance")
                return new ConstructionCost(5, 4, 2, 1);
            if (tier == "medieval")
                return new ConstructionCost(4, 3, 0, 0);
            return new ConstructionCost(6, 5, 3, 2);
        }
    }
}
