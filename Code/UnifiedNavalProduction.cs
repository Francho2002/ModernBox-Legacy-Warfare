using System;
using System.Collections.Generic;
using System.Linq;

namespace ModernBox
{
    /// <summary>
    /// Common dock production for all visual eras. Docks have no artificial
    /// berth quota: costs and normal construction cadence control fleet growth,
    /// while strategic submarines retain a kingdom-wide stability ceiling.
    /// </summary>
    internal static class UnifiedNavalProduction
    {
        private static readonly string[] Factions = { "alliance", "harden", "gaia", "horde" };
        private static readonly string[] RoleBoatPrefixes =
        {
            "hunter_submarine", "arsenal_submarine", "trident_submarine", "neutron_submarine",
            "emp_submarine", "hammer_submarine", "ruin_submarine"
        };

        // Legacy civilian/bomb boats stay registered for save compatibility, but
        // ModernBox docks build only escorts and missile-capable combat platforms.
        private static readonly string[] CombatBoatTypes = BuildCombatBoatTypes();

        internal static void EnableAllDocks()
        {
            foreach (BuildingAsset asset in AssetManager.buildings.list)
            {
                if (asset?.id == null || asset.id.IndexOf("docks", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                // Replacing, not appending, prevents the native dock picker from
                // reintroducing bomb/civilian hulls outside our production pool.
                asset.boat_types = CombatBoatTypes;
            }
        }

        internal static bool TryBuild(Docks dock, City city, ref Actor result)
        {
            if (!ShouldReplace(dock, city))
                return false;

            result = null;
            if (dock.tiles_ocean == null || dock.tiles_ocean.Count == 0)
            {
                dock.recalculateOceanTiles();
                return false;
            }

            string id = SelectAffordableId(dock, city);
            if (string.IsNullOrEmpty(id))
                return false;

            WorldTile tile = dock.tiles_ocean.GetRandom();
            if (tile?.region?.island == null || !tile.region.island.goodForDocks())
                return false;
            Actor boat = World.world.units.createNewUnit(id, tile);
            if (boat == null)
                return false;

            boat.setKingdom(city.kingdom);
            boat.setCity(city);
            dock.addBoatToDock(boat);
            city.spendResourcesForBuildingAsset(GetCost(id));
            ModernDiplomacyController.ApplyArmsCredit(city);
            result = boat;
            return true;
        }

        internal static ActorAsset SelectAffordableAsset(BuildingAsset dockAsset, City city)
        {
            if (!ShouldReplace(dockAsset, city))
                return null;

            // The callback lacks a Docks component. Direct construction remains
            // exclusively in TryBuild, which can bind the created boat to port.
            return null;
        }

        internal static bool ShouldReplace(BuildingAsset asset, City city)
        {
            return Traits.vehiclesAllowed && city != null && IsDockAsset(asset);
        }

        internal static bool ShouldReplace(Docks dock, City city)
        {
            return Traits.vehiclesAllowed && dock?.building?.asset != null &&
                ShouldReplace(dock.building.asset, city);
        }

        // All dock entry points use this single test. The legacy patches in
        // Buildings.cs contain era-specific pickers; the unified system owns
        // their production while vehicle mode is enabled.
        internal static bool IsDockAsset(BuildingAsset asset)
        {
            return asset?.id != null &&
                asset.id.IndexOf("docks", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string[] BuildCombatBoatTypes()
        {
            List<string> types = new List<string>();
            foreach (string faction in Factions)
            {
                types.Add("destroyer_a_" + faction + "_boat");
                types.Add("destroyer_b_" + faction + "_boat");
                types.Add("carrier_" + faction + "_boat");
                types.Add("submarine_" + faction + "_boat");
                types.Add("salvo_submarine_" + faction + "_boat");
                foreach (string rolePrefix in RoleBoatPrefixes)
                    types.Add(rolePrefix + "_" + faction + "_boat");
            }
            return types.ToArray();
        }

        private static List<string> GetPool(Docks dock, City city)
        {
            string faction = GetFaction(city);
            string[] ids =
            {
                "aDestroyer_" + faction, "bDestroyer_" + faction, "CarrierVessel_" + faction,
                "HunterSubmarine_" + faction, "Submarine_" + faction,
                "ArsenalSubmarine_" + faction, "TridentSubmarine_" + faction,
                "NeutronSubmarine_" + faction, "EmpSubmarine_" + faction,
                "HammerSubmarine_" + faction, "RuinSubmarine_" + faction,
                "SalvoSubmarine_" + faction
            };

            int kingdomStrategic = MilitaryQuotaService.CountKingdomStrategicAssets(city?.kingdom);
            int kingdomStrategicCap = MilitaryQuotaService.GetKingdomStrategicCap(city?.kingdom);

            return ids.Where(id => AssetManager.actor_library.get(id) != null)
                // A port must be allowed to complete its first varied combat
                // template. The kingdom-wide strategic ceiling resumes once
                // that exact dock already has the hull in question.
                .Where(id => !NavalRoles.IsStrategicSubmarine(id) ||
                    kingdomStrategic < kingdomStrategicCap || !DockOwnsVariant(dock, id))
                // A carrier is a dock's capital ship: exactly one per home
                // dock, while other hulls may repeat after the template.
                .Where(id => !IsCarrier(id) || !DockOwnsVariant(dock, id))
                .ToList();
        }

        private static string SelectAffordableId(Docks dock, City city)
        {
            List<string> pool = GetPool(dock, city);
            string carrierId = "CarrierVessel_" + GetFaction(city);

            // Every home dock reserves its first capital ship. An empty dock
            // may establish itself with one cheap escort; from then on it
            // spends nothing on other hulls until its carrier is affordable.
            // GetPool removes the carrier after the dock owns one, preserving
            // the exact one-carrier-per-dock ceiling.
            if (pool.Contains(carrierId) && !DockOwnsVariant(dock, carrierId))
            {
                if (city.hasEnoughResourcesFor(GetCost(carrierId)))
                    return carrierId;

                if (!DockHasCombatFleet(dock))
                {
                    List<string> starterEscorts = pool
                        .Where(IsEscortDestroyer)
                        .Where(id => city.hasEnoughResourcesFor(GetCost(id)))
                        .ToList();
                    if (starterEscorts.Count > 0)
                        return starterEscorts[Randy.randomInt(0, starterEscorts.Count)];
                }

                return null;
            }

            List<string> affordable = pool
                .Where(id => city.hasEnoughResourcesFor(GetCost(id)))
                .ToList();
            if (affordable.Count == 0)
                return null;

            List<string> military = affordable.Where(IsMilitary).ToList();
            List<string> strategic = military.Where(NavalRoles.IsStrategicSubmarine).ToList();
            List<string> normalMilitary = military.Where(id => !NavalRoles.IsStrategicSubmarine(id)).ToList();
            List<string> escorts = normalMilitary.Where(IsEscortDestroyer).ToList();

            // Home-dock template: do not use kingdom ownership here. A fleet
            // based at another port is not a substitute for this dock having
            // its own example of each hull. Resources remain the normal gate.
            List<string> missingAtDock = military
                .Where(id => !DockOwnsVariant(dock, id))
                .ToList();
            if (missingAtDock.Count > 0)
                return missingAtDock[Randy.randomInt(0, missingAtDock.Count)];

            // Put an escort into a young fleet early. It remains a short-range
            // anti-submarine ship; it never uses the retired bomb-boat attack.
            List<string> missingEscorts = escorts.Where(id => !KingdomOwnsVariant(city?.kingdom, id)).ToList();
            if (missingEscorts.Count > 0 && Randy.randomChance(.65f))
                return missingEscorts[Randy.randomInt(0, missingEscorts.Count)];

            // Before repeating a hull, a kingdom deliberately fills gaps in
            // its available fleet catalogue. This makes the nuclear/naval arm
            // useful in play without making any single weapon the only answer.
            List<string> missingVariants = military
                .Where(id => !KingdomOwnsVariant(city?.kingdom, id))
                .ToList();
            if (missingVariants.Count > 0 && Randy.randomChance(.78f))
            {
                List<string> missingStrategic = missingVariants
                    .Where(NavalRoles.IsStrategicSubmarine)
                    .ToList();
                if (missingStrategic.Count > 0 && Randy.randomChance(.70f))
                {
                    List<string> measured = missingStrategic
                        .Where(id => !IsSalvoSubmarine(id) || Randy.randomChance(.40f))
                        .ToList();
                    if (measured.Count > 0)
                        return measured[Randy.randomInt(0, measured.Count)];
                }

                return missingVariants[Randy.randomInt(0, missingVariants.Count)];
            }

            if (Randy.randomChance(.70f))
            {
                // Strategic assets are available together but do not all become
                // production candidates at once. The kingdom-wide cap remains
                // the only fleet-size gate; ports themselves are unrestricted.
                if (strategic.Count > 0 && Randy.randomChance(.55f))
                {
                    List<string> nonApocalypse = strategic
                        .Where(id => !IsSalvoSubmarine(id) || Randy.randomChance(.30f))
                        .ToList();
                    if (nonApocalypse.Count > 0)
                        return nonApocalypse[Randy.randomInt(0, nonApocalypse.Count)];
                }

                if (escorts.Count > 0 && Randy.randomChance(.45f))
                    return escorts[Randy.randomInt(0, escorts.Count)];
                if (normalMilitary.Count > 0)
                    return normalMilitary[Randy.randomInt(0, normalMilitary.Count)];
            }

            if (normalMilitary.Count > 0)
                return normalMilitary[Randy.randomInt(0, normalMilitary.Count)];
            if (strategic.Count > 0)
                return strategic[Randy.randomInt(0, strategic.Count)];
            return null;
        }

        private static bool KingdomOwnsVariant(Kingdom kingdom, string assetId)
        {
            if (kingdom?.cities == null || string.IsNullOrEmpty(assetId))
                return false;

            foreach (City city in kingdom.cities)
            {
                if (city?.units == null)
                    continue;
                foreach (Actor unit in city.units)
                {
                    if (unit != null && unit.isAlive() &&
                        string.Equals(unit.asset?.id, assetId, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            return false;
        }

        private static bool DockOwnsVariant(Docks dock, string assetId)
        {
            if (dock == null || string.IsNullOrEmpty(assetId))
                return false;

            ActorAsset asset = AssetManager.actor_library.get(assetId);
            string boatType = asset?.boat_type;
            return !string.IsNullOrEmpty(boatType) && dock.countBoatTypes(boatType) > 0;
        }

        private static bool DockHasCombatFleet(Docks dock)
        {
            if (dock == null)
                return false;

            foreach (string boatType in CombatBoatTypes)
            {
                if (dock.countBoatTypes(boatType) > 0)
                    return true;
            }
            return false;
        }

        private static string GetFaction(City city)
        {
            string leader = city?.leader?.asset?.id ?? "";
            if (leader == "dwarf" || leader.Contains("cold") || leader.Contains("penguin")) return "harden";
            if (leader == "elf" || leader.Contains("druid") || leader.Contains("fairy")) return "gaia";
            if (leader == "orc" || leader.Contains("necromancer") || leader.Contains("wolf")) return "horde";
            return "alliance";
        }

        private static bool IsMilitary(string id)
        {
            return !string.IsNullOrEmpty(id) && (id.Contains("Vessel_") ||
                NavalRoles.IsAnyModernSubmarine(id) || IsEscortDestroyer(id) || id.Contains("brawler_"));
        }

        private static bool IsEscortDestroyer(string id)
        {
            return !string.IsNullOrEmpty(id) &&
                (id.StartsWith("aDestroyer_", StringComparison.OrdinalIgnoreCase) ||
                 id.StartsWith("bDestroyer_", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsCarrier(string id)
        {
            return !string.IsNullOrEmpty(id) && id.StartsWith("CarrierVessel_", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSalvoSubmarine(string id)
        {
            return !string.IsNullOrEmpty(id) && id.StartsWith("SalvoSubmarine_", StringComparison.OrdinalIgnoreCase);
        }

        private static ConstructionCost GetCost(string id)
        {
            if (id.StartsWith("CarrierVessel_", StringComparison.OrdinalIgnoreCase)) return new ConstructionCost(16, 14, 11, 6);
            if (IsEscortDestroyer(id)) return new ConstructionCost(7, 6, 4, 2);
            if (id.StartsWith("HunterSubmarine_", StringComparison.OrdinalIgnoreCase)) return new ConstructionCost(6, 5, 3, 1);
            if (id.StartsWith("ArsenalSubmarine_", StringComparison.OrdinalIgnoreCase)) return new ConstructionCost(8, 7, 5, 2);
            if (id.StartsWith("TridentSubmarine_", StringComparison.OrdinalIgnoreCase)) return new ConstructionCost(12, 10, 8, 4);
            if (id.StartsWith("NeutronSubmarine_", StringComparison.OrdinalIgnoreCase)) return new ConstructionCost(9, 8, 6, 3);
            if (id.StartsWith("EmpSubmarine_", StringComparison.OrdinalIgnoreCase)) return new ConstructionCost(9, 8, 6, 3);
            if (id.StartsWith("HammerSubmarine_", StringComparison.OrdinalIgnoreCase)) return new ConstructionCost(13, 11, 9, 5);
            if (id.StartsWith("RuinSubmarine_", StringComparison.OrdinalIgnoreCase)) return new ConstructionCost(9, 8, 6, 3);
            if (IsSalvoSubmarine(id)) return new ConstructionCost(15, 13, 11, 6);
            if (NavalRoles.IsAnyModernSubmarine(id)) return new ConstructionCost(8, 7, 5, 2);
            if (IsMilitary(id)) return new ConstructionCost(6, 5, 4, 2);
            if (id.StartsWith("CargoShip_", StringComparison.OrdinalIgnoreCase)) return new ConstructionCost(7, 5, 3, 2);
            return new ConstructionCost(4, 3, 1, 1);
        }
    }
}
