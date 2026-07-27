using System;
using System.Collections.Generic;
using UnityEngine;

namespace ModernBox
{
    /// <summary>
    /// Guarantees a slow, era-independent route to one defensive missile
    /// launcher per sufficiently developed city.
    /// </summary>
    internal sealed class GroundMissileProductionController : MonoBehaviour
    {
        private const float MinimumInterval = 8f;
        private const float MaximumInterval = 12f;
        private float _nextScan;

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

        private static void TryBuildOneLauncher()
        {
            try
            {
                if (World.world?.cities?.list == null)
                    return;

                var eligible = new List<City>();
                foreach (City city in World.world.cities.list)
                {
                    if (UnifiedMilitaryProduction.TryGetDefensiveLauncher(
                        city, out string launcherId, out ConstructionCost cost))
                    {
                        eligible.Add(city);
                    }
                }

                if (eligible.Count == 0)
                    return;

                int start = UnityEngine.Random.Range(0, eligible.Count);
                for (int offset = 0; offset < eligible.Count; offset++)
                {
                    City city = eligible[(start + offset) % eligible.Count];
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
