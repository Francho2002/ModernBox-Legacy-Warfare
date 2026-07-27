using System;
using System.Collections.Generic;

namespace ModernBox
{
    /// <summary>
    /// Stable land-production budgets and a kingdom-wide strategic naval
    /// ceiling. This service only counts units when a city actually attempts
    /// to produce something; it never scans the world every frame and never
    /// deletes units that already exist.
    /// </summary>
    internal static class MilitaryQuotaService
    {
        internal sealed class CityQuota
        {
            internal int BaseLandUnits { get; private set; }
            internal int ArtilleryUpgradePopulation { get; private set; }

            internal CityQuota(int baseLandUnits, int artilleryUpgradePopulation)
            {
                BaseLandUnits = baseLandUnits;
                ArtilleryUpgradePopulation = artilleryUpgradePopulation;
            }
        }

        private static readonly Dictionary<long, CityQuota> CityProfiles =
            new Dictionary<long, CityQuota>();
        private static readonly Dictionary<long, int> KingdomStrategicProfiles =
            new Dictionary<long, int>();

        /// <summary>
        /// City budgets are varied but population can add modest capacity. This
        /// keeps ordinary soldiers relevant instead of filling every city with
        /// vehicles as soon as it has a barracks.
        /// </summary>
        internal static CityQuota GetCityQuota(City city)
        {
            long key = city?.id ?? 0L;
            if (CityProfiles.TryGetValue(key, out CityQuota quota))
                return quota;

            string hashKey = "city:" + key;
            quota = new CityQuota(
                3 + Pick(hashKey, 0, 3), // 3..5 before population bonuses
                Pick(hashKey, 1, 2) == 0 ? 130 : 180);
            CityProfiles[key] = quota;
            return quota;
        }

        internal static int GetLandUnitCap(City city)
        {
            CityQuota quota = GetCityQuota(city);
            int population = city?.getPopulationPeople() ?? 0;
            int bonus = population >= 100 ? 1 : 0;
            if (population >= 220)
                bonus++;
            return quota.BaseLandUnits + bonus;
        }

        internal static int GetArtilleryCap(City city)
        {
            CityQuota quota = GetCityQuota(city);
            int population = city?.getPopulationPeople() ?? 0;
            return population >= quota.ArtilleryUpgradePopulation ? 2 : 1;
        }

        // Launchers are a dedicated defensive asset, not conventional artillery.
        internal static int GetMissileLauncherCap(City city)
        {
            return 2;
        }

        /// <summary>
        /// Strategic submarines are limited across the whole kingdom, not by a
        /// single port. Every hull still has to be bought through the normal,
        /// resource-gated dock cycle.
        /// </summary>
        internal static int GetKingdomStrategicCap(Kingdom kingdom)
        {
            if (kingdom == null)
                return 1;
            long key = kingdom.id;
            if (KingdomStrategicProfiles.TryGetValue(key, out int cap))
                return cap;

            int cities = kingdom.cities?.Count ?? 1;
            int developmentBonus = Math.Min(2, Math.Max(0, cities - 1));
            cap = 4 + developmentBonus + Pick("kingdom:" + key, 0, 2); // 4..7
            KingdomStrategicProfiles[key] = cap;
            return cap;
        }

        internal static int CountKingdomStrategicAssets(Kingdom kingdom)
        {
            if (kingdom?.cities == null)
                return 0;

            int total = 0;
            HashSet<Actor> seen = new HashSet<Actor>();
            foreach (City city in kingdom.cities)
            {
                if (city?.units == null)
                    continue;

                foreach (Actor unit in city.units)
                {
                    if (unit == null || !unit.isAlive() || !seen.Add(unit))
                        continue;
                    if (NavalRoles.IsStrategicSubmarine(unit.asset?.id))
                        total++;
                }
            }
            return total;
        }

        internal static string GetCityQuotaLabel(City city)
        {
            CityQuota quota = GetCityQuota(city);
            return GetLandUnitCap(city) + " terrestres, " + GetArtilleryCap(city) +
                " artillería convencional (segunda desde población " + quota.ArtilleryUpgradePopulation + "), " +
                GetMissileLauncherCap(city) + " lanzamisiles terrestres";
        }

        internal static string GetKingdomStrategicQuotaLabel(Kingdom kingdom)
        {
            return GetKingdomStrategicCap(kingdom) + " submarino(s) estratégico(s) por reino";
        }

        internal static void ResetSession()
        {
            CityProfiles.Clear();
            KingdomStrategicProfiles.Clear();
        }

        private static int Pick(string key, int salt, int exclusiveMaximum)
        {
            if (exclusiveMaximum <= 1)
                return 0;
            return StableHash(key + ":" + salt) % exclusiveMaximum;
        }

        private static int StableHash(string value)
        {
            unchecked
            {
                uint hash = 2166136261u;
                for (int i = 0; i < value.Length; i++)
                {
                    hash ^= value[i];
                    hash *= 16777619u;
                }
                return (int)(hash & 0x7fffffffu);
            }
        }
    }
}
