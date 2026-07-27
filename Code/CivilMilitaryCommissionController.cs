using System;
using System.Collections.Generic;
using UnityEngine;

namespace ModernBox
{
    /// <summary>
    /// Supplies a deliberately slow civilian commissioning route now that the
    /// fantasy ideology actions no longer create baseWarUnit chassis. It does
    /// not scan every frame, invoke powers, or bypass UnifiedMilitaryProduction.
    /// </summary>
    internal sealed class CivilMilitaryCommissionController : MonoBehaviour
    {
        private const float MinimumInterval = 18f;
        private const float MaximumInterval = 28f;
        private const float CityCooldown = 90f;
        private const int CitiesPerCycle = 4;

        private readonly Dictionary<long, float> _lastCommissionAt =
            new Dictionary<long, float>();
        private float _nextCycle;
        private int _cursor;

        private void Awake()
        {
            ScheduleNextCycle();
        }

        private void Update()
        {
            if (Time.time < _nextCycle)
                return;

            ScheduleNextCycle();
            TryCommissionOneCity();
        }

        private void TryCommissionOneCity()
        {
            try
            {
                var cities = World.world?.cities?.list;
                if (cities == null || cities.Count == 0)
                    return;

                if (_cursor >= cities.Count)
                    _cursor = 0;

                int attempts = Math.Min(CitiesPerCycle, cities.Count);
                for (int i = 0; i < attempts; i++)
                {
                    if (_cursor >= cities.Count)
                        _cursor = 0;

                    City city = cities[_cursor++];
                    if (!CanCommission(city))
                        continue;

                    if (UnifiedMilitaryProduction.TryCommissionCityUnit(city))
                    {
                        _lastCommissionAt[city.id] = Time.time;
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                ModernBoxLogger.Error("[MX.CivilCommission] Production cycle failure: " + ex.Message);
            }
        }

        private bool CanCommission(City city)
        {
            if (city == null || !city.isAlive() || city.kingdom == null ||
                !city.kingdom.isCiv() || !city.hasLeader() ||
                MilitaryProgressionController.GetLevel(city) < 2)
                return false;

            return !_lastCommissionAt.TryGetValue(city.id, out float lastAt) ||
                Time.time - lastAt >= CityCooldown;
        }

        private void ScheduleNextCycle()
        {
            _nextCycle = Time.time + UnityEngine.Random.Range(MinimumInterval, MaximumInterval);
        }
    }
}
