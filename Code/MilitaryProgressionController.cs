using System;
using System.Collections.Generic;
using UnityEngine;

namespace ModernBox
{
    /// <summary>
    /// A read-only military readiness snapshot for one city. It deliberately
    /// describes capability only; it never upgrades or replaces buildings.
    /// </summary>
    internal sealed class MilitaryProgressionStatus
    {
        internal int Level { get; private set; }
        internal string BlockingReason { get; private set; }
        internal int Population { get; private set; }
        internal int RelevantBuildings { get; private set; }
        internal int AdvancedBuildings { get; private set; }
        internal bool CanFundRenaissance { get; private set; }
        internal bool CanFundHeavy { get; private set; }

        internal MilitaryProgressionStatus(
            int level,
            string blockingReason,
            int population,
            int relevantBuildings,
            int advancedBuildings,
            bool canFundRenaissance,
            bool canFundHeavy)
        {
            Level = level;
            BlockingReason = blockingReason;
            Population = population;
            RelevantBuildings = relevantBuildings;
            AdvancedBuildings = advancedBuildings;
            CanFundRenaissance = canFundRenaissance;
            CanFundHeavy = canFundHeavy;
        }
    }

    /// <summary>
    /// Refreshes a tiny slice of city military readiness every 20-40 seconds.
    /// It is intentionally separate from WorldBox's visual eras and culture
    /// progression, and does not run a per-frame city scan.
    /// </summary>
    internal sealed class MilitaryProgressionController : MonoBehaviour
    {
        private const float MinimumInterval = 20f;
        private const float MaximumInterval = 40f;
        private const int CitiesPerTick = 3;

        private static readonly Dictionary<City, MilitaryProgressionStatus> StatusByCity =
            new Dictionary<City, MilitaryProgressionStatus>();

        private static readonly ConstructionCost RenaissanceReadinessCost =
            new ConstructionCost(5, 4, 2, 1);
        private static readonly ConstructionCost HeavyReadinessCost =
            new ConstructionCost(9, 7, 6, 3);

        private float _nextTick;
        private int _cursor;

        private void Awake()
        {
            ScheduleNextTick();
        }

        private void Update()
        {
            if (Time.time < _nextTick)
                return;

            ScheduleNextTick();
            RefreshCitySlice();
        }

        internal static int GetLevel(City city)
        {
            return GetStatus(city).Level;
        }

        internal static string GetBlockingReason(City city)
        {
            return GetStatus(city).BlockingReason;
        }

        internal static MilitaryProgressionStatus GetStatus(City city)
        {
            if (city == null)
                return CreateInvalidStatus("La ciudad no existe.");

            if (StatusByCity.TryGetValue(city, out MilitaryProgressionStatus status))
                return status;

            // The first question to a newly-created city computes a single
            // snapshot. Thereafter this controller performs the slow updates.
            status = Evaluate(city);
            StatusByCity[city] = status;
            return status;
        }

        internal static bool IsRoleUnlocked(City city, string tier, string role, string actorId)
        {
            return IsRoleUnlocked(GetLevel(city), tier, role, actorId);
        }

        internal static bool IsRoleUnlocked(int level, string tier, string role, string actorId)
        {
            if (level <= 0)
                return false;

            if (string.Equals(role, "air", StringComparison.OrdinalIgnoreCase) ||
                ModernCapPolicy.IsAllowedAircraft(actorId))
                return level >= 4;

            if (string.Equals(tier, "medieval", StringComparison.OrdinalIgnoreCase))
                return level >= 1;

            if (string.Equals(tier, "renaissance", StringComparison.OrdinalIgnoreCase))
                return level >= 2;

            if (string.Equals(tier, "modern", StringComparison.OrdinalIgnoreCase))
            {
                // Tanks, artillery and missile systems are mature modern ground
                // infrastructure. Air units remain level four by the rule above.
                return level >= 3;
            }

            return false;
        }

        internal static bool CanBuildDefensiveLauncher(City city)
        {
            return city != null && city.getPopulationPeople() >= 100 && GetLevel(city) >= 3;
        }

        internal static void ResetSession()
        {
            StatusByCity.Clear();
        }

        private void RefreshCitySlice()
        {
            try
            {
                var cities = World.world?.cities?.list;
                if (cities == null || cities.Count == 0)
                {
                    StatusByCity.Clear();
                    _cursor = 0;
                    return;
                }

                if (_cursor >= cities.Count)
                    _cursor = 0;

                int count = Math.Min(CitiesPerTick, cities.Count);
                for (int i = 0; i < count; i++)
                {
                    if (_cursor >= cities.Count)
                        _cursor = 0;

                    City city = cities[_cursor++];
                    if (city == null)
                        continue;

                    StatusByCity[city] = Evaluate(city);
                }
            }
            catch (Exception ex)
            {
                ModernBoxLogger.Error("[MX.MilitaryProgression] Refresh failure: " + ex.Message);
            }
        }

        private void ScheduleNextTick()
        {
            _nextTick = Time.time + UnityEngine.Random.Range(MinimumInterval, MaximumInterval);
        }

        private static MilitaryProgressionStatus Evaluate(City city)
        {
            if (city == null)
                return CreateInvalidStatus("La ciudad no existe.");
            if (!Traits.vehiclesAllowed)
                return CreateInvalidStatus("Los vehículos están desactivados en la configuración del mod.");
            if (!city.isAlive() || city.kingdom == null || !city.kingdom.isCiv())
                return CreateInvalidStatus("La ciudad necesita pertenecer a un reino civilizado vivo.");
            if (!city.hasLeader() || city.leader == null || !city.leader.isAlive())
                return CreateInvalidStatus("La ciudad necesita un líder vivo.");

            int population = city.getPopulationPeople();
            int relevantBuildings = 0;
            int advancedBuildings = 0;
            CountMilitaryInfrastructure(city, out relevantBuildings, out advancedBuildings);

            bool canFundRenaissance = city.hasEnoughResourcesFor(RenaissanceReadinessCost);
            bool canFundHeavy = city.hasEnoughResourcesFor(HeavyReadinessCost);
            int level = 0;
            string reason;

            if (population < 20)
            {
                reason = "Faltan habitantes: se requieren 20 para iniciar una fuerza organizada.";
            }
            else
            {
                level = 1;
                if (population < 55 || relevantBuildings == 0 || !canFundRenaissance)
                {
                    reason = BuildLevelTwoBlockReason(population, relevantBuildings, canFundRenaissance);
                }
                else
                {
                    level = 2;
                    if (population < 100 || relevantBuildings == 0 || !canFundHeavy)
                    {
                        reason = BuildLevelThreeBlockReason(population, relevantBuildings, canFundHeavy);
                    }
                    else
                    {
                        level = 3;
                        if (population < 180 || relevantBuildings < 2 || advancedBuildings == 0 || !canFundHeavy)
                        {
                            reason = BuildLevelFourBlockReason(population, relevantBuildings, advancedBuildings, canFundHeavy);
                        }
                        else
                        {
                            level = 4;
                            reason = "Capacidad militar avanzada disponible.";
                        }
                    }
                }
            }

            return new MilitaryProgressionStatus(
                level, reason, population, relevantBuildings, advancedBuildings,
                canFundRenaissance, canFundHeavy);
        }

        private static void CountMilitaryInfrastructure(City city, out int relevant, out int advanced)
        {
            relevant = 0;
            advanced = 0;
            if (city?.buildings == null)
                return;

            foreach (Building building in city.buildings)
            {
                BuildingAsset asset = building?.asset;
                if (asset == null)
                    continue;

                string type = asset.type ?? string.Empty;
                string id = asset.id ?? string.Empty;
                bool isBarracks = string.Equals(type, "type_barracks", StringComparison.OrdinalIgnoreCase) ||
                                  id.IndexOf("barracks", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isHall = string.Equals(type, "type_hall", StringComparison.OrdinalIgnoreCase);
                bool isDock = id.StartsWith("docks_", StringComparison.OrdinalIgnoreCase) ||
                              id.IndexOf("dock", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isTraining = string.Equals(type, "type_training_dummies", StringComparison.OrdinalIgnoreCase);
                bool isRelevant = isBarracks || isHall || isDock || isTraining;
                if (!isRelevant)
                    continue;

                relevant++;
                // This is logistical capacity, not a visual-era check. A
                // barracks, dock or training site qualifies regardless of the
                // sprite or legacy asset name it happens to use.
                if (isBarracks || isDock || isTraining)
                    advanced++;
            }
        }

        private static string BuildLevelTwoBlockReason(int population, int infrastructure, bool funded)
        {
            if (population < 55)
                return "Faltan habitantes: se requieren 55 para el nivel militar 2.";
            if (infrastructure == 0)
                return "Falta cuartel o salón de ciudad para el nivel militar 2.";
            if (!funded)
                return "Faltan recursos para equipamiento de transición.";
            return "La ciudad aún no alcanza el nivel militar 2.";
        }

        private static string BuildLevelThreeBlockReason(int population, int infrastructure, bool funded)
        {
            if (population < 100)
                return "Faltan habitantes: se requieren 100 para armamento pesado moderno.";
            if (infrastructure == 0)
                return "Falta infraestructura militar para armamento pesado moderno.";
            if (!funded)
                return "Faltan recursos para tanques, artillería o lanzamisiles.";
            return "La ciudad aún no alcanza el nivel militar 3.";
        }

        private static string BuildLevelFourBlockReason(
            int population,
            int relevantInfrastructure,
            int advancedInfrastructure,
            bool funded)
        {
            if (population < 180)
                return "Faltan habitantes: se requieren 180 para aviación y logística avanzada.";
            if (relevantInfrastructure < 2)
                return "Falta una segunda pieza de infraestructura: ayuntamiento, cuartel, puerto o entrenamiento.";
            if (advancedInfrastructure == 0)
                return "Falta cuartel, puerto o zona de entrenamiento para aviación avanzada.";
            if (!funded)
                return "Faltan recursos para aviación y sistemas estratégicos.";
            return "La ciudad aún no alcanza el nivel militar 4.";
        }

        private static MilitaryProgressionStatus CreateInvalidStatus(string reason)
        {
            return new MilitaryProgressionStatus(0, reason, 0, 0, 0, false, false);
        }
    }
}
