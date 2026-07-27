using System;
using UnityEngine;

namespace ModernBox
{
    /// <summary>
    /// Guarantees a slow, era-independent route to one defensive missile
    /// launcher per sufficiently developed city.
    /// </summary>
    internal sealed class GroundMissileProductionController : MonoBehaviour
    {
        private const float MinimumInterval = 10f;
        private const float MaximumInterval = 16f;
        private const int CitiesPerCycle = 6;
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
            TryBuildOneLauncher();
        }

        private void TryBuildOneLauncher()
        {
            try
            {
                if (World.world?.cities?.list == null)
                    return;

                var cities = World.world.cities.list;
                if (cities.Count == 0)
                    return;

                if (_cursor >= cities.Count)
                    _cursor = 0;

                int attempts = Math.Min(CitiesPerCycle, cities.Count);
                for (int i = 0; i < attempts; i++)
                {
                    if (_cursor >= cities.Count)
                        _cursor = 0;
                    City city = cities[_cursor++];
                    if (UnifiedMilitaryProduction.TryBuildDefensiveLauncher(city))
                        return;
                }
            }
            catch (Exception ex)
            {
                ModernBoxLogger.Error("[MX.DefenseProduction] Exceptional production cycle failure: " + ex.Message);
            }
        }

        private void ScheduleNextScan()
        {
            _nextScan = Time.time + UnityEngine.Random.Range(MinimumInterval, MaximumInterval);
        }
    }
}
