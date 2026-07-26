using System;
using System.Collections.Generic;
using System.Linq;

namespace ModernBox
{
    /// <summary>Common dock production for all visual eras; no era-specific dock is required.</summary>
    internal static class UnifiedNavalProduction
    {
        private static readonly string[] RealisticBoatTypes =
        {
            "cargo_alliance_boat", "fishing_alliance_boat", "transporter_alliance_boat",
            "destroyer_a_alliance_boat", "destroyer_b_alliance_boat", "carrier_alliance_boat", "submarine_alliance_boat",
            "cargo_harden_boat", "fishing_harden_boat", "transporter_harden_boat",
            "destroyer_a_harden_boat", "destroyer_b_harden_boat", "carrier_harden_boat", "submarine_harden_boat",
            "cargo_gaia_boat", "fishing_gaia_boat", "transporter_gaia_boat",
            "destroyer_a_gaia_boat", "destroyer_b_gaia_boat", "carrier_gaia_boat", "submarine_gaia_boat",
            "cargo_horde_boat", "fishing_horde_boat", "transporter_horde_boat",
            "destroyer_a_horde_boat", "destroyer_b_horde_boat", "carrier_horde_boat", "submarine_horde_boat"
        };

        private static readonly string[] CivilianBoatTypePrefixes =
        {
            "cargo", "fishing", "transporter"
        };

        private static readonly string[] MilitaryBoatTypePrefixes =
        {
            "destroyer_a", "destroyer_b", "carrier", "submarine"
        };

        private const int TotalBoatCapPerDock = 4;
        private const int MilitaryBoatCapPerDock = 2;

        internal static void EnableAllDocks()
        {
            foreach (BuildingAsset asset in AssetManager.buildings.list)
            {
                if (asset?.id == null || asset.id.IndexOf("docks", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                asset.boat_types = (asset.boat_types ?? Array.Empty<string>())
                    .Where(type => type.IndexOf("brawler", StringComparison.OrdinalIgnoreCase) < 0)
                    .Concat(RealisticBoatTypes)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
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

            // This callback has no Docks instance, so it cannot establish the
            // owner of the quota. Production is intentionally owned by TryBuild,
            // which receives the actual component and binds the new boat to it.
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

        private static List<string> GetPool(Docks dock, City city)
        {
            string faction = GetFaction(city);
            string[] ids =
            {
                // Modern military craft comes first in the shared pool.
                "aDestroyer_" + faction, "bDestroyer_" + faction,
                "CarrierVessel_" + faction, "Submarine_" + faction,
                "CargoShip_" + faction, "FishingBoat_" + faction,
                "Transporter_" + faction
            };

            int total = CountDockBoats(dock, faction, CivilianBoatTypePrefixes) +
                CountDockBoats(dock, faction, MilitaryBoatTypePrefixes);
            int military = CountDockBoats(dock, faction, MilitaryBoatTypePrefixes);
            if (total >= TotalBoatCapPerDock)
                return new List<string>();

            return ids.Where(id => AssetManager.actor_library.get(id) != null)
                .Where(id => !IsMilitary(id) || military < MilitaryBoatCapPerDock)
                .ToList();
        }

        private static int CountDockBoats(Docks dock, string faction, IEnumerable<string> typePrefixes)
        {
            if (dock == null || string.IsNullOrEmpty(faction))
                return 0;

            int total = 0;
            foreach (string prefix in typePrefixes)
            {
                total += dock.countBoatTypes(prefix + "_" + faction + "_boat");
            }
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
            List<string> civilian = affordable.Where(id => !IsMilitary(id)).ToList();
            bool preferMilitary = Randy.randomChance(.65f);
            List<string> selected = preferMilitary ? military : civilian;
            if (selected.Count == 0)
                selected = preferMilitary ? civilian : military;
            // Nuclear warfare needs an actual launch platform. The first
            // affordable military hull at each dock is therefore a submarine;
            // the per-dock military cap still prevents naval spam.
            if (preferMilitary)
            {
                string submarine = selected.FirstOrDefault(
                    id => id.StartsWith("Submarine_", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(submarine))
                    return submarine;
            }
            return selected.Count == 0 ? null : selected[Randy.randomInt(0, selected.Count)];
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
                id.Contains("Submarine_") || id.Contains("brawler_"));
        }

        private static ConstructionCost GetCost(string id)
        {
            // Same ceiling as land heavy systems: scarce, yet viable without
            // changing a city's visual era.
            if (IsMilitary(id)) return new ConstructionCost(6, 5, 4, 2);
            if (id.StartsWith("CargoShip_", StringComparison.OrdinalIgnoreCase)) return new ConstructionCost(7, 5, 3, 2);
            return new ConstructionCost(4, 3, 1, 1);
        }
    }
}
