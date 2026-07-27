using System;
using System.Collections.Generic;

namespace ModernBox
{
    /// <summary>
    /// Stable, deliberately small production budgets.  A budget is derived
    /// from an existing WorldBox id (and the dock position), so it is varied
    /// between settlements without rolling new limits every production tick.
    /// This service only counts units when a dock/city actually attempts to
    /// produce something; it never scans the world every frame and never
    /// deletes units that already exist.
    /// </summary>
    internal static class MilitaryQuotaService
    {
        internal sealed class DockQuota
        {
            internal int TotalBoats { get; private set; }
            internal int MilitaryBoats { get; private set; }
            internal int StrategicBoatsAtThisPort { get; private set; }

            internal DockQuota(int totalBoats, int militaryBoats, int strategicBoatsAtThisPort)
            {
                TotalBoats = totalBoats;
                MilitaryBoats = militaryBoats;
                StrategicBoatsAtThisPort = strategicBoatsAtThisPort;
            }
        }

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

        private static readonly Dictionary<string, DockQuota> DockProfiles =
            new Dictionary<string, DockQuota>(StringComparer.Ordinal);
        private static readonly Dictionary<long, CityQuota> CityProfiles =
            new Dictionary<long, CityQuota>();
        private static readonly Dictionary<long, int> KingdomStrategicProfiles =
            new Dictionary<long, int>();

        /// <summary>
        /// Each port gets a stable but useful 6-8 berth budget.  This gives a
        /// mature maritime kingdom room to assemble a mixed missile fleet over
        /// time, without turning a single harbour into an unlimited shipyard.
        /// </summary>
        internal static DockQuota GetDockQuota(Docks dock, City city)
        {
            return GetDockQuota(dock?.building, city);
        }

        /// <summary>
        /// Read-only variant used by the intelligence panel.  The quota derives
        /// from the same dock building/tile key as production, so the UI never
        /// shows a made-up generic value for a city's different ports.
        /// </summary>
        internal static DockQuota GetDockQuota(Building dockBuilding, City city)
        {
            string key = GetDockKey(dockBuilding, city);
            if (DockProfiles.TryGetValue(key, out DockQuota quota))
                return quota;

            int total = 6 + Pick(key, 0, 3); // 6..8
            int military = 4 + Pick(key, 1, 3); // 4..6
            // A port can host a compact strategic detachment, while the
            // kingdom-wide cap below still prevents a one-port missile flood.
            quota = new DockQuota(total, Math.Min(military, total), 3);
            DockProfiles[key] = quota;
            return quota;
        }

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
        /// Strategic submarines are limited across the whole kingdom, not only
        /// the dock that tried to commission one. A developed naval power can
        /// field a mixed deterrent (rather than repeating one hull), but every
        /// hull still has to be bought through the slow dock cycle.
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

        internal static string GetDockQuotaLabel(Docks dock, City city)
        {
            DockQuota quota = GetDockQuota(dock, city);
            return FormatDockQuota(quota);
        }

        internal static string GetDockQuotaLabel(Building dockBuilding, City city)
        {
            DockQuota quota = GetDockQuota(dockBuilding, city);
            return FormatDockQuota(quota);
        }

        private static string FormatDockQuota(DockQuota quota)
        {
            return quota.TotalBoats + " embarcaciones, " + quota.MilitaryBoats +
                " militares, " + quota.StrategicBoatsAtThisPort + " estratégico por puerto";
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
            DockProfiles.Clear();
            CityProfiles.Clear();
            KingdomStrategicProfiles.Clear();
        }

        private static string GetDockKey(Building dockBuilding, City city)
        {
            WorldTile tile = dockBuilding?.current_tile;
            if (tile != null)
                return "dock:" + (city?.id ?? 0L) + ":" + tile.pos.x + ":" + tile.pos.y;
            return "dock:" + (city?.id ?? 0L) + ":unknown";
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
