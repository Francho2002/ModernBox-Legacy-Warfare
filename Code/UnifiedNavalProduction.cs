using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace ModernBox
{
    /// <summary>
    /// Common dock production for all visual eras. Each dock completes one
    /// finite combat template; costs and normal construction cadence control
    /// when its missing hulls are acquired or replaced.
    /// </summary>
    internal static class UnifiedNavalProduction
    {
        private static readonly string[] Factions = { "alliance", "harden", "gaia", "horde" };
        private static readonly string[] RoleBoatPrefixes =
        {
            "interceptor_submarine", "hunter_submarine", "arsenal_submarine", "trident_submarine", "neutron_submarine",
            "emp_submarine", "hammer_submarine", "ruin_submarine"
        };

        // Legacy civilian/bomb boats stay registered for save compatibility, but
        // ModernBox docks build only escorts and missile-capable combat platforms.
        private static readonly string[] CombatBoatTypes = BuildCombatBoatTypes();
        private static readonly ConditionalWeakTable<City, DockRotationState> DockRotation =
            new ConditionalWeakTable<City, DockRotationState>();

        private sealed class DockRotationState
        {
            internal Docks LastDock;
        }

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
            Docks selectedDock = SelectNextCityDock(city, dock);
            if (selectedDock == null)
                return false;

            if (selectedDock.tiles_ocean == null || selectedDock.tiles_ocean.Count == 0)
            {
                selectedDock.recalculateOceanTiles();
                return false;
            }

            string id = SelectAffordableId(selectedDock, city);
            if (string.IsNullOrEmpty(id))
                return false;

            WorldTile tile = selectedDock.tiles_ocean.GetRandom();
            if (tile?.region?.island == null || !tile.region.island.goodForDocks())
                return false;
            Actor boat = World.world.units.createNewUnit(id, tile);
            if (boat == null)
                return false;

            boat.setKingdom(city.kingdom);
            boat.setCity(city);
            selectedDock.addBoatToDock(boat);
            city.spendResourcesForBuildingAsset(GetCost(id));
            ModernDiplomacyController.ApplyArmsCredit(city);
            result = boat;
            return true;
        }

        private static Docks SelectNextCityDock(City city, Docks fallback)
        {
            List<Docks> docks = GetValidCityDocks(city);
            if (docks.Count == 0)
                return ShouldReplace(fallback, city) ? fallback : null;

            DockRotationState state = DockRotation.GetValue(city, _ => new DockRotationState());
            int lastIndex = state.LastDock == null ? -1 : docks.IndexOf(state.LastDock);
            int nextIndex = lastIndex < 0 ? 0 : (lastIndex + 1) % docks.Count;
            Docks selected = docks[nextIndex];
            state.LastDock = selected;
            return selected;
        }

        private static List<Docks> GetValidCityDocks(City city)
        {
            List<Docks> docks = new List<Docks>();
            if (city?.buildings == null)
                return docks;

            foreach (Building building in city.buildings)
            {
                Docks candidate = building?.component_docks;
                if (candidate != null && ShouldReplace(candidate, city) && !docks.Contains(candidate))
                    docks.Add(candidate);
            }
            return docks;
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

        internal static bool IsManagedCombatBoat(Actor actor)
        {
            string boatType = actor?.asset?.boat_type;
            return actor?.asset?.is_boat == true && !string.IsNullOrEmpty(boatType) &&
                CombatBoatTypes.Contains(boatType, StringComparer.OrdinalIgnoreCase);
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
                "InterceptorSubmarine_" + faction, "HunterSubmarine_" + faction, "Submarine_" + faction,
                "ArsenalSubmarine_" + faction, "TridentSubmarine_" + faction,
                "NeutronSubmarine_" + faction, "EmpSubmarine_" + faction,
                "HammerSubmarine_" + faction, "RuinSubmarine_" + faction,
                "SalvoSubmarine_" + faction
            };

            return ids.Where(id => AssetManager.actor_library.get(id) != null)
                // This is the complete, per-home-dock template. Existing
                // save-game duplicates are left untouched, but production
                // never creates a second managed hull for this dock.
                .Where(id => !DockOwnsVariant(dock, id))
                .ToList();
        }

        private static string SelectAffordableId(Docks dock, City city)
        {
            List<string> pool = GetPool(dock, city);
            string carrierId = "CarrierVessel_" + GetFaction(city);
            string hunterId = "HunterSubmarine_" + GetFaction(city);

            bool carrierMissing = pool.Contains(carrierId);
            bool hunterMissing = pool.Contains(hunterId);

            // The carrier always wins when it is affordable. Before acquiring
            // the hunter, a dock may still establish that cheap escort. Once
            // the hunter exists, it saves every resource until its carrier can
            // be completed instead of filling the remaining template early.
            if (carrierMissing)
            {
                if (city.hasEnoughResourcesFor(GetCost(carrierId)))
                    return carrierId;
                if (hunterMissing && city.hasEnoughResourcesFor(GetCost(hunterId)))
                    return hunterId;
                return null;
            }

            // City resources are shared. Do not let a dock that already owns
            // its carrier fill lower-priority template slots while another
            // valid dock is still getting its Hunter and saving for a carrier.
            if (!carrierMissing && CityHasDockMissingCarrier(city))
                return null;

            // Every dock obtains its one non-strategic hunter before entering
            // the normal finite-template stage.
            if (hunterMissing &&
                city.hasEnoughResourcesFor(GetCost(hunterId)))
                return hunterId;

            List<string> affordable = pool
                .Where(id => city.hasEnoughResourcesFor(GetCost(id)))
                .ToList();
            if (affordable.Count == 0)
                return null;

            // The pool already contains only this dock's missing template
            // entries. Keep the canonical order deterministic and never
            // select a repeat after all twelve are present.
            return affordable[0];
        }

        private static bool CityHasDockMissingCarrier(City city)
        {
            string carrierId = "CarrierVessel_" + GetFaction(city);
            foreach (Docks cityDock in GetValidCityDocks(city))
            {
                if (!DockOwnsVariant(cityDock, carrierId))
                    return true;
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

        private static bool IsSalvoSubmarine(string id)
        {
            return !string.IsNullOrEmpty(id) && id.StartsWith("SalvoSubmarine_", StringComparison.OrdinalIgnoreCase);
        }

        private static ConstructionCost GetCost(string id)
        {
            if (id.StartsWith("CarrierVessel_", StringComparison.OrdinalIgnoreCase)) return new ConstructionCost(16, 14, 11, 6);
            if (IsEscortDestroyer(id)) return new ConstructionCost(7, 6, 4, 2);
            if (id.StartsWith("InterceptorSubmarine_", StringComparison.OrdinalIgnoreCase)) return new ConstructionCost(10, 9, 7, 3);
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
