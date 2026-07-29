using System;
using System.Collections.Generic;
using UnityEngine;

namespace ModernBox
{
    /// <summary>
    /// A low-frequency fallback for dock production. WorldBox still owns its
    /// normal harbour behaviour, while this controller guarantees that a
    /// mature dock is reconsidered even when that behaviour is delayed by a
    /// busy city AI cycle. It delegates every actual decision to
    /// UnifiedNavalProduction, so research, resources and one-hull-per-dock
    /// template limits stay identical on both routes.
    /// </summary>
    internal sealed class NavalProductionController : MonoBehaviour
    {
        private const float MinimumInterval = 10f;
        private const float MaximumInterval = 16f;
        private const int CitiesPerCycle = 3;

        private float _nextScan;
        private int _cursor;

        private void Awake()
        {
            ScheduleNextScan();
        }

        private void Update()
        {
            if (Time.time < _nextScan)
                return;

            ScheduleNextScan();
            if (World.world != null && !World.world.isPaused())
                TryBuildOneHull();
        }

        private void TryBuildOneHull()
        {
            try
            {
                IList<City> cities = World.world?.cities?.list;
                if (cities == null || cities.Count == 0)
                {
                    _cursor = 0;
                    return;
                }

                if (_cursor >= cities.Count)
                    _cursor = 0;

                int attempts = Math.Min(CitiesPerCycle, cities.Count);
                for (int i = 0; i < attempts; i++)
                {
                    if (_cursor >= cities.Count)
                        _cursor = 0;

                    City city = cities[_cursor++];
                    Docks dock = FindEligibleDock(city);
                    if (dock == null)
                        continue;

                    Actor built = null;
                    if (!UnifiedNavalProduction.TryBuild(dock, city, ref built) || built == null)
                        continue;

                    ModernBoxLogger.Log("[MX.NavalProduction] Built " +
                        (built.asset?.id ?? "naval hull") + " for " +
                        (city.name ?? "a city") + ".");
                    // A global cycle commissions at most one hull. This keeps
                    // the fallback subordinate to normal WorldBox pacing.
                    return;
                }
            }
            catch (Exception ex)
            {
                ModernBoxLogger.Error("[MX.NavalProduction] Exceptional production cycle failure: " + ex.Message);
            }
        }

        private static Docks FindEligibleDock(City city)
        {
            if (city == null || !city.isAlive() || city.buildings == null)
                return null;

            foreach (Building building in city.buildings)
            {
                Docks dock = building?.component_docks;
                if (dock == null || !dock.isDockGood())
                    continue;
                if (UnifiedNavalProduction.ShouldReplace(dock, city))
                    return dock;
            }

            return null;
        }

        private void ScheduleNextScan()
        {
            _nextScan = Time.time + UnityEngine.Random.Range(MinimumInterval, MaximumInterval);
        }
    }
}
