using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace ModernBox
{
    internal enum MilitaryKnowledge
    {
        None = 0,
        Industry = 1,
        Ballistics = 2,
        Aviation = 3,
        StrategicNavy = 4
    }

    /// <summary>
    /// Persistent, kingdom-wide military research. It deliberately uses no era,
    /// culture trait or resource stockpile: cities earn permanent knowledge from
    /// their people and civic/military infrastructure in slow simulation cycles.
    /// </summary>
    internal sealed class MilitaryKnowledgeService : MonoBehaviour
    {
        internal const string SaveKey = "modernbox.military_knowledge.v1";
        private const int SaveVersion = 1;
        private const float MinimumInterval = 18f;
        private const float MaximumInterval = 28f;
        private const int KingdomsPerCycle = 3;

        [Serializable]
        private sealed class KnowledgeState
        {
            public int version = SaveVersion;
            public int unlocked;
        }

        private sealed class Requirements
        {
            internal int population;
            internal int sites;
            internal int docks;
            internal int libraries;
            internal int highestCityLevel;
        }

        private static readonly Dictionary<long, KnowledgeState> States =
            new Dictionary<long, KnowledgeState>();
        private static MapBox CachedWorld;
        private static object CachedMapStats;
        private static float StateReadyAt;

        private float _nextCycle;
        private int _cursor;

        private void Awake()
        {
            ResetForWorld();
            ScheduleNextCycle();
        }

        private void Update()
        {
            if (World.world == null)
                return;
            if (CachedWorld != World.world || CachedMapStats != World.world.map_stats)
            {
                ResetForWorld();
                ScheduleNextCycle();
                return;
            }
            if (World.world.isPaused() || Time.time < _nextCycle)
                return;

            ScheduleNextCycle();
            RefreshKingdomSlice();
        }

        internal static bool CanBuild(City city, string assetId)
        {
            return CanBuild(city?.kingdom, assetId);
        }

        internal static bool CanBuild(Kingdom kingdom, string assetId)
        {
            MilitaryKnowledge required = RequiredFor(assetId);
            return required == MilitaryKnowledge.None || Has(kingdom, required);
        }

        internal static string GetBlockingReason(City city, string assetId)
        {
            MilitaryKnowledge required = RequiredFor(assetId);
            if (required == MilitaryKnowledge.None || Has(city?.kingdom, required))
                return null;
            string prerequisites = GetPrerequisiteBlock(city?.kingdom, required);
            return "requiere " + Label(required) + ". " +
                (string.IsNullOrEmpty(prerequisites) ? GetNextRequirement(city?.kingdom, required) : prerequisites);
        }

        internal static string GetSummary(City city)
        {
            Kingdom kingdom = city?.kingdom;
            if (kingdom == null)
                return "Conocimiento: sin reino.";

            List<string> known = new List<string>();
            foreach (MilitaryKnowledge knowledge in OrderedKnowledge())
            {
                if (Has(kingdom, knowledge))
                    known.Add(Label(knowledge));
            }

            string current = known.Count == 0 ? "ninguno" : string.Join(", ", known.ToArray());
            MilitaryKnowledge next = FirstMissing(kingdom);
            return next == MilitaryKnowledge.None
                ? "Conocimiento: " + current + "."
                : "Conocimiento: " + current + ". Siguiente: " + Label(next) + " (" +
                    GetNextRequirement(kingdom, next) + ").";
        }

        internal static MilitaryKnowledge RequiredFor(string assetId)
        {
            if (string.IsNullOrEmpty(assetId))
                return MilitaryKnowledge.None;

            if (assetId.StartsWith("CarrierVessel_", StringComparison.OrdinalIgnoreCase) ||
                assetId.StartsWith("SalvoSubmarine_", StringComparison.OrdinalIgnoreCase) ||
                assetId.StartsWith("TridentSubmarine_", StringComparison.OrdinalIgnoreCase) ||
                assetId.StartsWith("NeutronSubmarine_", StringComparison.OrdinalIgnoreCase) ||
                assetId.StartsWith("EmpSubmarine_", StringComparison.OrdinalIgnoreCase) ||
                assetId.StartsWith("HammerSubmarine_", StringComparison.OrdinalIgnoreCase) ||
                assetId.StartsWith("RuinSubmarine_", StringComparison.OrdinalIgnoreCase))
                return MilitaryKnowledge.StrategicNavy;

            if (assetId.StartsWith("MissileSystem_", StringComparison.OrdinalIgnoreCase) ||
                assetId.StartsWith("Submarine_", StringComparison.OrdinalIgnoreCase) ||
                assetId.StartsWith("ArsenalSubmarine_", StringComparison.OrdinalIgnoreCase))
                return MilitaryKnowledge.Ballistics;

            if (IsAircraft(assetId))
                return MilitaryKnowledge.Aviation;

            if (assetId.StartsWith("InterceptorSubmarine_", StringComparison.OrdinalIgnoreCase) ||
                assetId.StartsWith("HunterSubmarine_", StringComparison.OrdinalIgnoreCase) ||
                IsHeavyLandSystem(assetId))
                return MilitaryKnowledge.Industry;

            return MilitaryKnowledge.None;
        }

        private static bool Has(Kingdom kingdom, MilitaryKnowledge knowledge)
        {
            if (kingdom == null || !kingdom.isCiv())
                return false;
            return (State(kingdom).unlocked & Bit(knowledge)) != 0;
        }

        private static IEnumerable<MilitaryKnowledge> OrderedKnowledge()
        {
            yield return MilitaryKnowledge.Industry;
            yield return MilitaryKnowledge.Ballistics;
            yield return MilitaryKnowledge.Aviation;
            yield return MilitaryKnowledge.StrategicNavy;
        }

        private static MilitaryKnowledge FirstMissing(Kingdom kingdom)
        {
            foreach (MilitaryKnowledge knowledge in OrderedKnowledge())
                if (!Has(kingdom, knowledge))
                    return knowledge;
            return MilitaryKnowledge.None;
        }

        private void RefreshKingdomSlice()
        {
            try
            {
                List<Kingdom> kingdoms = Civilizations();
                if (kingdoms.Count == 0)
                {
                    _cursor = 0;
                    return;
                }
                if (_cursor >= kingdoms.Count)
                    _cursor = 0;

                int count = Math.Min(KingdomsPerCycle, kingdoms.Count);
                for (int index = 0; index < count; index++)
                {
                    if (_cursor >= kingdoms.Count)
                        _cursor = 0;
                    RefreshKingdom(kingdoms[_cursor++]);
                }
            }
            catch (Exception ex)
            {
                ModernBoxLogger.Error("[MX.Knowledge] Ciclo de investigación aislado: " + ex.Message);
            }
        }

        private static void RefreshKingdom(Kingdom kingdom)
        {
            if (kingdom == null || !kingdom.isCiv())
                return;

            KnowledgeState state = State(kingdom);
            Requirements requirements = GetRequirements(kingdom);
            int before = state.unlocked;

            if (!Has(kingdom, MilitaryKnowledge.Industry) && MeetsIndustry(requirements))
            {
                state.unlocked |= Bit(MilitaryKnowledge.Industry);
            }
            else if (!Has(kingdom, MilitaryKnowledge.Ballistics) &&
                Has(kingdom, MilitaryKnowledge.Industry) && MeetsBallistics(requirements))
            {
                state.unlocked |= Bit(MilitaryKnowledge.Ballistics);
            }
            else if (!Has(kingdom, MilitaryKnowledge.Aviation) &&
                Has(kingdom, MilitaryKnowledge.Industry) && MeetsAviation(requirements))
            {
                state.unlocked |= Bit(MilitaryKnowledge.Aviation);
            }
            else if (!Has(kingdom, MilitaryKnowledge.StrategicNavy) &&
                Has(kingdom, MilitaryKnowledge.Ballistics) && Has(kingdom, MilitaryKnowledge.Aviation) &&
                MeetsStrategicNavy(requirements))
            {
                state.unlocked |= Bit(MilitaryKnowledge.StrategicNavy);
            }

            if (state.unlocked == before)
                return;

            Save(kingdom);
            ModernBoxLogger.Log("[MX.Knowledge] " + kingdom.name + " desbloqueó " +
                string.Join(", ", KnownLabels(kingdom).ToArray()) + ".");
        }

        private static Requirements GetRequirements(Kingdom kingdom)
        {
            Requirements result = new Requirements();
            if (kingdom?.cities == null)
                return result;

            foreach (City city in kingdom.cities)
            {
                if (city == null || !city.isAlive())
                    continue;
                result.population += Math.Max(0, city.getPopulationPeople());
                result.highestCityLevel = Math.Max(result.highestCityLevel,
                    MilitaryProgressionController.GetLevel(city));

                if (city.buildings == null)
                    continue;
                foreach (Building building in city.buildings)
                {
                    if (!IsKnowledgeSite(building?.asset))
                        continue;
                    result.sites++;
                    if (IsDock(building.asset))
                        result.docks++;
                    if (IsLibrary(building.asset))
                        result.libraries++;
                }
            }
            return result;
        }

        private static bool MeetsIndustry(Requirements requirements)
        {
            return requirements.population >= 80 && requirements.sites >= 2 &&
                requirements.highestCityLevel >= 2;
        }

        private static bool MeetsBallistics(Requirements requirements)
        {
            return requirements.population >= 150 && requirements.sites >= 3 && requirements.libraries >= 1 &&
                requirements.highestCityLevel >= 3;
        }

        private static bool MeetsAviation(Requirements requirements)
        {
            return requirements.population >= 170 && requirements.sites >= 3 && requirements.libraries >= 1 &&
                requirements.highestCityLevel >= 3;
        }

        private static bool MeetsStrategicNavy(Requirements requirements)
        {
            return requirements.population >= 260 && requirements.sites >= 4 &&
                requirements.docks >= 1 && requirements.libraries >= 1 && requirements.highestCityLevel >= 4;
        }

        private static string GetNextRequirement(Kingdom kingdom, MilitaryKnowledge knowledge)
        {
            Requirements r = GetRequirements(kingdom);
            switch (knowledge)
            {
                case MilitaryKnowledge.Industry:
                    return "población del reino " + r.population + "/80, instalaciones " + r.sites + "/2, ciudad militar nivel " + r.highestCityLevel + "/2";
                case MilitaryKnowledge.Ballistics:
                    return "población del reino " + r.population + "/150, instalaciones " + r.sites + "/3, bibliotecas " + r.libraries + "/1, ciudad militar nivel " + r.highestCityLevel + "/3";
                case MilitaryKnowledge.Aviation:
                    return "población del reino " + r.population + "/170, instalaciones " + r.sites + "/3, bibliotecas " + r.libraries + "/1, ciudad militar nivel " + r.highestCityLevel + "/3";
                case MilitaryKnowledge.StrategicNavy:
                    return "población del reino " + r.population + "/260, instalaciones " + r.sites + "/4, bibliotecas " + r.libraries + "/1, puertos " + r.docks + "/1, ciudad militar nivel " + r.highestCityLevel + "/4";
                default:
                    return "sin requisitos pendientes";
            }
        }

        private static string GetPrerequisiteBlock(Kingdom kingdom, MilitaryKnowledge knowledge)
        {
            if ((knowledge == MilitaryKnowledge.Ballistics || knowledge == MilitaryKnowledge.Aviation) &&
                !Has(kingdom, MilitaryKnowledge.Industry))
                return "primero requiere Industria militar.";
            if (knowledge == MilitaryKnowledge.StrategicNavy && !Has(kingdom, MilitaryKnowledge.Ballistics))
                return "primero requiere Guiado balístico.";
            if (knowledge == MilitaryKnowledge.StrategicNavy && !Has(kingdom, MilitaryKnowledge.Aviation))
                return "primero requiere Aeronáutica.";
            return null;
        }

        private static bool IsKnowledgeSite(BuildingAsset asset)
        {
            if (asset == null)
                return false;
            string id = asset.id ?? string.Empty;
            string type = asset.type ?? string.Empty;
            return id.IndexOf("barracks", StringComparison.OrdinalIgnoreCase) >= 0 ||
                id.IndexOf("dock", StringComparison.OrdinalIgnoreCase) >= 0 ||
                id.IndexOf("library", StringComparison.OrdinalIgnoreCase) >= 0 ||
                string.Equals(type, "type_hall", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "type_training_dummies", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDock(BuildingAsset asset)
        {
            string id = asset?.id ?? string.Empty;
            return id.IndexOf("dock", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsLibrary(BuildingAsset asset)
        {
            string id = asset?.id ?? string.Empty;
            return id.IndexOf("library", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsAircraft(string id)
        {
            return ModernCapPolicy.IsAllowedAircraft(id);
        }

        private static bool IsHeavyLandSystem(string id)
        {
            return id.StartsWith("Tank_", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith("howitzer_", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith("wheeledtank_", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith("supporttruck_", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith("modernhumvee_", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "AbramTank", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "Humvee", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "shermanww", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "tankie", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "genericwwtank", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "landship", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "bigtankww", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "wwsupporttruck", StringComparison.OrdinalIgnoreCase) ||
                ModernCapPolicy.IsConventionalArtillery(id);
        }

        private static int Bit(MilitaryKnowledge knowledge)
        {
            return 1 << ((int)knowledge - 1);
        }

        private static string Label(MilitaryKnowledge knowledge)
        {
            switch (knowledge)
            {
                case MilitaryKnowledge.Industry: return "Industria militar";
                case MilitaryKnowledge.Ballistics: return "Guiado balístico";
                case MilitaryKnowledge.Aviation: return "Aeronáutica";
                case MilitaryKnowledge.StrategicNavy: return "Marina estratégica";
                default: return "sin investigación";
            }
        }

        private static List<string> KnownLabels(Kingdom kingdom)
        {
            List<string> labels = new List<string>();
            foreach (MilitaryKnowledge knowledge in OrderedKnowledge())
                if (Has(kingdom, knowledge))
                    labels.Add(Label(knowledge));
            return labels;
        }

        private static List<Kingdom> Civilizations()
        {
            List<Kingdom> result = new List<Kingdom>();
            if (World.world?.kingdoms == null)
                return result;
            foreach (Kingdom kingdom in World.world.kingdoms)
                if (kingdom != null && kingdom.isCiv())
                    result.Add(kingdom);
            result.Sort((left, right) => left.id.CompareTo(right.id));
            return result;
        }

        private static KnowledgeState State(Kingdom kingdom)
        {
            if (kingdom == null)
                return new KnowledgeState();
            // WorldBox restores KingdomData a few seconds after a save becomes
            // visible. Do not cache an empty placeholder before that hydration
            // completes, or a persisted research record could look lost.
            if (Time.time < StateReadyAt)
                return new KnowledgeState();
            if (States.TryGetValue(kingdom.id, out KnowledgeState current))
                return current;

            current = new KnowledgeState();
            try
            {
                string json = null;
                if (kingdom.data != null)
                    kingdom.data.get(SaveKey, out json, null);
                if (!string.IsNullOrEmpty(json))
                {
                    KnowledgeState loaded = JsonConvert.DeserializeObject<KnowledgeState>(json);
                    if (loaded != null && loaded.version == SaveVersion)
                        current = loaded;
                }
            }
            catch (Exception ex)
            {
                ModernBoxLogger.Warning("[MX.Knowledge] Estado guardado ignorado para " + kingdom.id + ": " + ex.Message);
            }
            States[kingdom.id] = current;
            return current;
        }

        private static void Save(Kingdom kingdom)
        {
            if (kingdom?.data == null || Time.time < StateReadyAt)
                return;
            try
            {
                kingdom.data.set(SaveKey, JsonConvert.SerializeObject(State(kingdom)));
            }
            catch (Exception ex)
            {
                ModernBoxLogger.Warning("[MX.Knowledge] No se pudo guardar el estado: " + ex.Message);
            }
        }

        private void ScheduleNextCycle()
        {
            _nextCycle = Time.time + UnityEngine.Random.Range(MinimumInterval, MaximumInterval);
        }

        private static void ResetForWorld()
        {
            CachedWorld = World.world;
            CachedMapStats = World.world?.map_stats;
            States.Clear();
            StateReadyAt = Time.time + 8f;
        }
    }
}
