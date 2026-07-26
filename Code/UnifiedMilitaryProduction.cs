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
                !actor.city.hasLeader())
                return false;

            Actor leader = actor.city.leader;
            if (leader?.asset == null)
                return false;

            City city = actor.city;
            string species = leader.subspecies?.data?.species_id;
            if (string.IsNullOrEmpty(species))
                species = leader.asset.id;

            string role = PickRole();
            List<Candidate> candidates = CollectCandidates(species, role, city, actor);
            if (candidates.Count == 0)
                return false;

            Candidate selected = SelectWeightedAffordableCandidate(candidates, city);
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
            EffectsLibrary.spawn("fx_spawn", produced.current_tile);
            ActionLibrary.removeUnit(actor);
            actor.setTransformed();
            return true;
        }

        private static List<Candidate> CollectCandidates(string species, string role, City city, Actor transforming)
        {
            var result = new List<Candidate>();
            AddTier(result, Traits.CartTransformations.CartTransformationsModernRoles, "modern", species, role, city, transforming);
            AddTier(result, Traits.CartTransformations.CartTransformationsRenaissanceRoles, "renaissance", species, role, city, transforming);
            AddTier(result, Traits.CartTransformations.CartTransformationsMedievalRoles, "medieval", species, role, city, transforming);
            return result;
        }

        private static void AddTier(List<Candidate> result,
            Dictionary<string, Dictionary<string, List<string>>> table,
            string tier, string species, string role, City city, Actor transforming)
        {
            if (table == null || !table.TryGetValue(species, out var roles) ||
                !roles.TryGetValue(role, out var ids))
                return;

            foreach (string id in ids.Distinct())
            {
                ActorAsset asset = AssetManager.actor_library.get(id);
                if (!ModernCapPolicy.IsLandMilitaryActor(id) ||
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

        private static bool WithinCityCaps(City city, string candidateId, Actor transforming)
        {
            int population = city.getPopulationPeople();
            int totalCap = population < 80 ? 3 : 6;
            int artilleryCap = population < 120 ? 1 : 2;
            int total = 0;
            int artillery = 0;

            foreach (Actor unit in city.units)
            {
                if (unit == null || unit == transforming || !unit.isAlive())
                    continue;
                string id = unit.asset?.id;
                if (id == "baseWarUnit" || ModernCapPolicy.IsLandMilitaryActor(id))
                    total++;
                if (ModernCapPolicy.IsArtillery(id))
                    artillery++;
            }

            return total < totalCap && (!ModernCapPolicy.IsArtillery(candidateId) || artillery < artilleryCap);
        }

        private static string PickRole()
        {
            float roll = UnityEngine.Random.Range(0f, 1f);
            if (roll < .10f) return "support";
            if (roll < .20f) return "air";
            if (roll < .35f) return "heavy";
            return "offensive";
        }

        private static ConstructionCost GetCost(string id, string tier)
        {
            if (id.StartsWith("howitzer_", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith("Heli_", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith("FighterJet_", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith("Bomber_", StringComparison.OrdinalIgnoreCase) ||
                id == "F55FighterJet" || id == "americanbomberww" ||
                id == "biplane" || id == "fighterww" || id == "Zeppelin" || id == "EliteZeppelin")
                return new ConstructionCost(9, 7, 6, 3);
            if (id.StartsWith("Tank_", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith("MissileSystem_", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith("wheeledtank_", StringComparison.OrdinalIgnoreCase) ||
                id == "AbramTank")
                // Keep heavy systems rare, but do not require a stockpile that
                // the fixed Medieval economic buildings can never hold.
                return new ConstructionCost(9, 7, 6, 3);
            if (tier == "renaissance")
                return new ConstructionCost(5, 4, 2, 1);
            if (tier == "medieval")
                return new ConstructionCost(4, 3, 0, 0);
            return new ConstructionCost(6, 5, 3, 2);
        }
    }
}
