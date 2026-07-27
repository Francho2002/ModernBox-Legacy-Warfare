using System;
using System.Collections.Generic;
using System.Linq;

namespace ModernBox
{
    /// <summary>
    /// Common dock production for all visual eras. Each dock receives a
    /// deterministic small quota from MilitaryQuotaService, while strategic
    /// submarines are additionally constrained at kingdom level.
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
        // ModernBox docks only produce missile-capable combat platforms.
        private static readonly string[] MissileBoatTypes = BuildMissileBoatTypes();
        private static readonly string[] MilitaryBoatTypePrefixes =
        {
            "destroyer_a", "destroyer_b", "submarine", "hunter_submarine",
            "arsenal_submarine", "trident_submarine", "neutron_submarine", "emp_submarine",
            "hammer_submarine", "ruin_submarine", "salvo_submarine"
        };
        private static readonly string[] NormalMilitaryBoatTypePrefixes =
        {
            "destroyer_a", "destroyer_b", "hunter_submarine"
        };
        private static readonly string[] StrategicBoatTypePrefixes =
        {
            "submarine", "arsenal_submarine", "trident_submarine", "neutron_submarine",
            "emp_submarine", "hammer_submarine", "ruin_submarine", "salvo_submarine"
        };

        internal static void EnableAllDocks()
        {
            foreach (BuildingAsset asset in AssetManager.buildings.list)
            {
                if (asset?.id == null || asset.id.IndexOf("docks", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                // Replacing, not appending, prevents the native dock picker from
                // reintroducing bomb/civilian hulls outside our production pool.
                asset.boat_types = MissileBoatTypes;
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
            result = boat;
            return true;
        }

        internal static ActorAsset SelectAffordableAsset(BuildingAsset dockAsset, City city)
        {
            if (!ShouldReplace(dockAsset, city))
                return null;

            // The callback lacks a Docks component. Quotas are therefore owned
            // exclusively by TryBuild, which can bind the created boat to port.
            return null;
        }

        internal static bool ShouldReplace(BuildingAsset asset, City city)
        {
            return Traits.vehiclesAllowed && city != null && asset != null &&
                asset.id != null && asset.id.IndexOf("docks", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static bool ShouldReplace(Docks dock, City city)
        {
            return Traits.vehiclesAllowed && dock?.building?.asset != null &&
                ShouldReplace(dock.building.asset, city);
        }

        private static string[] BuildMissileBoatTypes()
        {
            List<string> types = new List<string>();
            foreach (string faction in Factions)
            {
                types.Add("destroyer_a_" + faction + "_boat");
                types.Add("destroyer_b_" + faction + "_boat");
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
                "aDestroyer_" + faction, "bDestroyer_" + faction,
                "HunterSubmarine_" + faction, "Submarine_" + faction,
                "ArsenalSubmarine_" + faction, "TridentSubmarine_" + faction,
                "NeutronSubmarine_" + faction, "EmpSubmarine_" + faction,
                "HammerSubmarine_" + faction, "RuinSubmarine_" + faction,
                "SalvoSubmarine_" + faction
            };

            int total = CountDockBoats(dock, faction, MilitaryBoatTypePrefixes);
            int military = CountDockBoats(dock, faction, MilitaryBoatTypePrefixes);
            int normalMilitary = CountDockBoats(dock, faction, NormalMilitaryBoatTypePrefixes);
            int strategic = CountDockBoats(dock, faction, StrategicBoatTypePrefixes);
            MilitaryQuotaService.DockQuota quota = MilitaryQuotaService.GetDockQuota(dock, city);
            // A one-ship military budget made the first destroyer/hunter block
            // every strategic submarine at that port. A dock may now reserve a
            // second military berth: one conventional escort and one special
            // submarine, never a fleet-wide flood.
            int militaryLimit = Math.Max(2, quota.MilitaryBoats);
            int kingdomStrategic = MilitaryQuotaService.CountKingdomStrategicAssets(city?.kingdom);
            int kingdomStrategicCap = MilitaryQuotaService.GetKingdomStrategicCap(city?.kingdom);
            if (total >= quota.TotalBoats)
                return new List<string>();

            return ids.Where(id => AssetManager.actor_library.get(id) != null)
                .Where(id => !IsMilitary(id) || military < militaryLimit)
                // A strategic hull first requires a normal escort/attack hull.
                // No more than one strategic hull can belong to this port, and
                // a kingdom's deterministic 1-2 strategic budget is respected
                // across all of its ports.
                .Where(id => !NavalRoles.IsStrategicSubmarine(id) ||
                    (normalMilitary > 0 && strategic < quota.StrategicBoatsAtThisPort &&
                     kingdomStrategic < kingdomStrategicCap))
                .ToList();
        }

        private static int CountDockBoats(Docks dock, string faction, IEnumerable<string> typePrefixes)
        {
            if (dock == null || string.IsNullOrEmpty(faction))
                return 0;

            int total = 0;
            foreach (string prefix in typePrefixes)
                total += dock.countBoatTypes(prefix + "_" + faction + "_boat");
            return total;
        }

        private static string SelectAffordableId(Docks dock, City city)
        {
            List<string> affordable = GetPool(dock, city)
                .Where(id => city.hasEnoughResourcesFor(GetCost(id)))
                .ToList();
            if (affordable.Count == 0)
                return null;

            List<string> military = affordable.Where(IsMilitary).ToList();
            List<string> strategic = military.Where(NavalRoles.IsStrategicSubmarine).ToList();
            List<string> normalMilitary = military.Where(id => !NavalRoles.IsStrategicSubmarine(id)).ToList();

            if (Randy.randomChance(.70f))
            {
                // Strategic assets are available together but do not all become
                // production candidates at once: a port can commission one and
                // reaches for it only after a normal warship exists.
                if (strategic.Count > 0 && normalMilitary.Count > 0 && Randy.randomChance(.35f))
                {
                    List<string> nonApocalypse = strategic
                        .Where(id => !IsSalvoSubmarine(id) || Randy.randomChance(.15f))
                        .ToList();
                    if (nonApocalypse.Count > 0)
                        return nonApocalypse[Randy.randomInt(0, nonApocalypse.Count)];
                }

                string hunter = normalMilitary.FirstOrDefault(
                    id => id.StartsWith("HunterSubmarine_", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(hunter) && Randy.randomChance(.45f))
                    return hunter;
                if (normalMilitary.Count > 0)
                    return normalMilitary[Randy.randomInt(0, normalMilitary.Count)];
            }

            if (normalMilitary.Count > 0)
                return normalMilitary[Randy.randomInt(0, normalMilitary.Count)];
            if (strategic.Count > 0)
                return strategic[Randy.randomInt(0, strategic.Count)];
            return null;
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
            return !string.IsNullOrEmpty(id) && (id.Contains("Destroyer_") || id.Contains("Vessel_") ||
                NavalRoles.IsAnyModernSubmarine(id) || id.Contains("brawler_"));
        }

        private static bool IsSalvoSubmarine(string id)
        {
            return !string.IsNullOrEmpty(id) && id.StartsWith("SalvoSubmarine_", StringComparison.OrdinalIgnoreCase);
        }

        private static ConstructionCost GetCost(string id)
        {
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
