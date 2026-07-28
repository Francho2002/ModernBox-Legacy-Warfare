using System.Collections.Generic;
using HarmonyLib;
using NCMS.Utils;

namespace ModernBox
{
    /// <summary>
    /// Owns the optional native-alliance rule.  It deliberately leaves the
    /// non-military parts of Modern Diplomacy (trade, sanctions and the Liga
    /// Moderna) alone.
    /// </summary>
    internal static class AlliancePolicy
    {
        internal const string OptionKey = "AlliancesOption";
        private const string ButtonId = "alliances_toggle";

        // MapBox can be reused while a save is rehydrated, so the map_stats
        // identity is part of the one-shot cleanup key as well.
        private static MapBox enforcedWorld;
        private static object enforcedMapStats;

        internal static bool Enabled
        {
            get
            {
                return Main.savedSettings == null ||
                    Main.savedSettings.boolOptions == null ||
                    !Main.savedSettings.boolOptions.TryGetValue(OptionKey, out bool enabled) ||
                    enabled;
            }
        }

        internal static void Toggle()
        {
            bool enabled = PowerButtons.GetToggleValue(ButtonId);
            Main.modifyBoolOption(OptionKey, enabled);

            // A later disable must clean the current world even if it was
            // already inspected while the option was off earlier in the run.
            enforcedWorld = null;
            enforcedMapStats = null;
            if (!enabled)
                EnforceDisabledState();
        }

        internal static void EnforceDisabledState()
        {
            if (Enabled)
            {
                enforcedWorld = null;
                enforcedMapStats = null;
                return;
            }

            MapBox world = World.world;
            if (world == null || world.kingdoms == null || world.alliances == null)
                return;

            object mapStats = world.map_stats;
            if (object.ReferenceEquals(enforcedWorld, world) && object.ReferenceEquals(enforcedMapStats, mapStats))
                return;

            enforcedWorld = world;
            enforcedMapStats = mapStats;
            int dissolved = DissolveCivilAlliances(world);
            ModernDiplomacyController.SuspendDefensiveCommitments();

            if (dissolved > 0)
                ModernBoxLogger.Log("[MX.Alliances] Alianzas nativas civiles disueltas: " + dissolved + ".");
        }

        private static int DissolveCivilAlliances(MapBox world)
        {
            HashSet<Alliance> alliances = new HashSet<Alliance>();
            foreach (Kingdom kingdom in world.kingdoms)
            {
                if (kingdom == null || !kingdom.isCiv())
                    continue;

                try
                {
                    Alliance alliance = kingdom.getAlliance();
                    if (alliance != null)
                        alliances.Add(alliance);
                }
                catch
                {
                    // A partially loaded kingdom should not prevent the rest
                    // of the valid civilian alliances from being cleaned up.
                }
            }

            int dissolved = 0;
            foreach (Alliance alliance in alliances)
            {
                try
                {
                    world.alliances.dissolveAlliance(alliance);
                    dissolved++;
                }
                catch (System.Exception ex)
                {
                    ModernBoxLogger.Warning("[MX.Alliances] No se pudo disolver una alianza: " + ex.Message);
                }
            }
            return dissolved;
        }
    }

    [HarmonyPatch(typeof(AllianceManager), "forceAlliance")]
    internal static class AlliancePolicyForceAlliancePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(ref bool __result)
        {
            if (AlliancePolicy.Enabled)
                return true;

            __result = false;
            return false;
        }
    }
}
