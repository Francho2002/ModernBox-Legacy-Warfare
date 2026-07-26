using System;
using HarmonyLib;
using NCMS.Utils;
using UnityEngine;

namespace ModernBox
{
    /// <summary>
    /// Keeps WorldBox's Forbidden Knowledge law enabled between world loads.
    /// The user can still turn this behaviour off from the ModernBox Eras tab.
    /// </summary>
    [HarmonyPatch(typeof(MapBox), "addLastStep")]
    public static class ForbiddenKnowledgeIntegration
    {
        private const string OptionKey = "AutoForbiddenKnowledgeOption";
        private const string ButtonId = "auto_forbidden_knowledge_toggle";
        private const string RestoreMigrationKey = "ModernBox_5_6_4_ForbiddenKnowledgeRestored";

        public static void EnsureEnabledForCurrentRelease()
        {
            // One-time repair for installations where the old, nonfunctional
            // toggle was persisted as disabled. It remains user-configurable
            // after this migration.
            if (PlayerPrefs.GetInt(RestoreMigrationKey, 0) == 1)
            {
                return;
            }

            Main.modifyBoolOption(OptionKey, true);
            PlayerPrefs.SetInt(RestoreMigrationKey, 1);
            PlayerPrefs.Save();
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void OnWorldReady()
        {
            if (IsEnabled())
            {
                ApplyToCurrentWorld();
            }
        }

        public static bool IsEnabled()
        {
            return Main.savedSettings != null
                && Main.savedSettings.boolOptions != null
                && Main.savedSettings.boolOptions.TryGetValue(OptionKey, out bool enabled)
                && enabled;
        }

        public static void ToggleFromButton()
        {
            bool enabled = PowerButtons.GetToggleValue(ButtonId);
            Main.modifyBoolOption(OptionKey, enabled);

            if (enabled)
            {
                ApplyToCurrentWorld();
            }
        }

        public static bool IsUnlockedInCurrentWorld()
        {
            try
            {
                if (!IsWorldReady())
                {
                    return false;
                }

                WorldLawAsset forbiddenKnowledge = WorldLawLibrary.world_law_cursed_world;
                return forbiddenKnowledge != null && forbiddenKnowledge.isEnabled();
            }
            catch
            {
                return false;
            }
        }

        public static bool ApplyToCurrentWorld()
        {
            try
            {
                if (!IsWorldReady())
                {
                    return false;
                }

                WorldLawAsset forbiddenKnowledge = WorldLawLibrary.world_law_cursed_world;
                if (forbiddenKnowledge == null)
                {
                    return false;
                }

                if (!forbiddenKnowledge.isEnabled())
                {
                    forbiddenKnowledge.toggle(true);
                }

                // The ritual normally sets this counter to 314. Restoring the
                // completed state prevents WorldBox from asking for a new
                // sacrifice when another world is loaded.
                CursedSacrifice.loadAlreadyCursedState();

                PowerButton.checkActorSpawnButtons();
                ModernBoxLogger.Log("[MX] Forbidden Knowledge restored for the current world.");
                return forbiddenKnowledge.isEnabled();
            }
            catch (Exception ex)
            {
                Debug.LogError("[ModernBox] Could not restore Forbidden Knowledge: " + ex);
                return false;
            }
        }

        private static bool IsWorldReady()
        {
            MapBox world = World.world;
            return world != null
                && world.world_laws != null
                && world.tiles_list != null
                && world.tiles_list.Length > 0;
        }
    }

    /// <summary>
    /// WorldBox build 719 does not reliably invoke MapBox.addLastStep for every
    /// load path. This small keeper retries only until the law is actually
    /// active, then sleeps unless the law or option is changed.
    /// </summary>
    internal sealed class ForbiddenKnowledgeKeeper : MonoBehaviour
    {
        private float _nextCheck;
        private bool _appliedForCurrentState;

        private void Update()
        {
            if (Time.unscaledTime < _nextCheck)
            {
                return;
            }

            _nextCheck = Time.unscaledTime + 1f;

            if (!ForbiddenKnowledgeIntegration.IsEnabled())
            {
                _appliedForCurrentState = false;
                return;
            }

            if (_appliedForCurrentState &&
                ForbiddenKnowledgeIntegration.IsUnlockedInCurrentWorld())
            {
                return;
            }

            _appliedForCurrentState =
                ForbiddenKnowledgeIntegration.ApplyToCurrentWorld();
        }
    }
}
