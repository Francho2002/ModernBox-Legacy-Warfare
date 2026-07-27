using System;
using UnityEngine;

namespace ModernBox
{
    /// <summary>
    /// Guarantees a slow, era-independent route to a compact defensive package:
    /// up to two land launchers and a deliberately mixed fixed-wing arm per
    /// developed kingdom. Each global cycle still commissions only one asset.
    /// </summary>
    internal sealed class GroundMissileProductionController : MonoBehaviour
    {
        private const float MinimumInterval = 10f;
        private const float MaximumInterval = 16f;
        private const int CitiesPerCycle = 6;
        private const int FirstLauncherCoverageCitiesPerCycle = 12;
        private float _nextScan;
        private int _cursor;
        private int _firstLauncherCursor;

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
                TryBuildOneDefenseAsset();
        }

        private void TryBuildOneDefenseAsset()
        {
            try
            {
                if (World.world?.cities?.list == null)
                    return;

                var cities = World.world.cities.list;
                if (cities.Count == 0)
                    return;

                // Give first launchers priority in a rotating city slice before
                // allowing aviation or second launchers to use this cycle. The
                // cursor advances independently, so every city is reached
                // without a full-world scan or a per-frame update.
                if (TryBuildFirstLauncherCoverage(cities))
                    return;

                if (_cursor >= cities.Count)
                    _cursor = 0;

                int attempts = Math.Min(CitiesPerCycle, cities.Count);
                for (int i = 0; i < attempts; i++)
                {
                    if (_cursor >= cities.Count)
                        _cursor = 0;
                    City city = cities[_cursor++];
                    if (UnifiedMilitaryProduction.TryBuildDefensiveOrAirAsset(city))
                        return;
                }
            }
            catch (Exception ex)
            {
                ModernBoxLogger.Error("[MX.DefenseProduction] Exceptional production cycle failure: " + ex.Message);
            }
        }

        private bool TryBuildFirstLauncherCoverage(System.Collections.Generic.IList<City> cities)
        {
            if (cities == null || cities.Count == 0)
                return false;

            if (_firstLauncherCursor >= cities.Count)
                _firstLauncherCursor = 0;

            int attempts = Math.Min(FirstLauncherCoverageCitiesPerCycle, cities.Count);
            for (int i = 0; i < attempts; i++)
            {
                if (_firstLauncherCursor >= cities.Count)
                    _firstLauncherCursor = 0;

                City city = cities[_firstLauncherCursor++];
                if (city != null && ModernCapPolicy.CountMissileLaunchers(city) == 0 &&
                    UnifiedMilitaryProduction.TryBuildDefensiveLauncher(city))
                    return true;
            }

            if (_firstLauncherCursor >= cities.Count)
                _firstLauncherCursor = 0;
            return false;
        }

        private void ScheduleNextScan()
        {
            _nextScan = Time.time + UnityEngine.Random.Range(MinimumInterval, MaximumInterval);
        }
    }
}
