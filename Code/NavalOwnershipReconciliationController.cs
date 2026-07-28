using System;
using System.Collections.Generic;
using System.Linq;
using tools;
using UnityEngine;

namespace ModernBox
{
    /// <summary>
    /// Keeps ModernBox combat boats from surviving as ownerless actors after a
    /// kingdom collapses or a saved home dock changes hands. It is deliberately
    /// sampled: the controller never performs a full-world scan every frame.
    /// </summary>
    internal sealed class NavalOwnershipReconciliationController : MonoBehaviour
    {
        private const float CycleSeconds = 6f;
        private const float RosterSeconds = 18f;
        private const int BoatsPerCycle = 12;

        private readonly List<Actor> _boats = new List<Actor>();
        private float _nextCycle;
        private float _nextRoster;
        private int _cursor;

        private void Awake()
        {
            _nextCycle = Time.time + CycleSeconds;
        }

        private void Update()
        {
            if (Time.time < _nextCycle)
                return;

            _nextCycle = Time.time + CycleSeconds;
            try
            {
                if (World.world == null || World.world.isPaused())
                    return;

                if (Time.time >= _nextRoster)
                    RefreshRoster();
                ReconcileSlice();
            }
            catch (Exception ex)
            {
                ModernBoxLogger.Error("[MX.NavalOwnership] Reconciliation cycle failed: " + ex.Message);
            }
        }

        private void RefreshRoster()
        {
            _boats.Clear();
            _cursor = 0;
            _nextRoster = Time.time + RosterSeconds;
            if (World.world?.units == null)
                return;

            foreach (Actor actor in World.world.units.Cast<Actor>())
            {
                if (UnifiedNavalProduction.IsManagedCombatBoat(actor))
                    _boats.Add(actor);
            }
        }

        private void ReconcileSlice()
        {
            if (_boats.Count == 0)
                return;

            int count = Math.Min(BoatsPerCycle, _boats.Count);
            for (int i = 0; i < count; i++)
            {
                if (_cursor >= _boats.Count)
                    _cursor = 0;

                ReconcileBoat(_boats[_cursor++]);
            }
        }

        private static void ReconcileBoat(Actor boat)
        {
            if (boat == null || !boat.isAlive() || !UnifiedNavalProduction.IsManagedCombatBoat(boat))
                return;

            // A kingdom that still owns any live city remains the boat's owner
            // even if its original port was conquered. Re-home its city link so
            // a stale captured city cannot turn the vessel neutral later.
            City survivingOwnerCity = FindLiveCity(boat.kingdom, boat.city);
            if (survivingOwnerCity != null)
            {
                if (boat.city != survivingOwnerCity)
                    boat.setCity(survivingOwnerCity);

                if (!HasLiveHomeDock(boat) && boat.hasHomeBuilding())
                    boat.clearHomeBuilding();
                return;
            }

            // If the former realm is gone but the home dock survived under a
            // civilian city, hand the hull to that dock's current owner. This
            // only runs after the former owner has no live city, so ordinary
            // port conquests do not steal a still-operational fleet.
            City dockCity = GetLiveHomeDockCity(boat);
            if (dockCity != null)
            {
                boat.setKingdom(dockCity.kingdom);
                boat.setCity(dockCity);
                return;
            }

            // A hull without a living realm or a surviving civilian dock has
            // no coherent owner, target set or production ledger. Removing it
            // avoids neutral nuclear submarines and stale AI decisions.
            ActionLibrary.removeUnit(boat);
        }

        private static City FindLiveCity(Kingdom kingdom, City preferredCity)
        {
            if (kingdom == null || !kingdom.isCiv())
                return null;

            if (IsLiveCityOf(preferredCity, kingdom))
                return preferredCity;

            if (kingdom.cities == null)
                return null;

            foreach (City city in kingdom.cities)
            {
                if (IsLiveCityOf(city, kingdom))
                    return city;
            }
            return null;
        }

        private static City GetLiveHomeDockCity(Actor boat)
        {
            if (boat == null || !boat.hasHomeBuilding())
                return null;

            Building dock = boat.getHomeBuilding();
            if (dock == null || dock.isRekt() || dock.current_tile == null || !dock.isUsable() ||
                dock.isAbandoned() || dock.isUnderConstruction() || dock.component_docks == null ||
                !dock.component_docks.isDockGood())
                return null;

            City city = dock.current_tile.zone?.city;
            return IsLiveCityOf(city, city?.kingdom) ? city : null;
        }

        private static bool HasLiveHomeDock(Actor boat)
        {
            City dockCity = GetLiveHomeDockCity(boat);
            return dockCity != null && dockCity.kingdom == boat.kingdom;
        }

        private static bool IsLiveCityOf(City city, Kingdom kingdom)
        {
            return city != null && city.isAlive() && kingdom != null && kingdom.isCiv() &&
                city.kingdom == kingdom;
        }
    }
}
