using HarmonyLib;
using System;
using UnityEngine;

namespace ModernBox
{
    // Build 719 uses the same native low-resolution mode both for the aerial
    // renderer and the interactive Meta layers.  Keep the overview crisp by
    // default, but hand the three tile-selectable Meta maps back to WorldBox's
    // own low-resolution path while they are selected.
    [HarmonyPatch(typeof(QualityChanger), "getZoomRateBoundLow")]
    internal static class AerialQualityPolicy
    {
        private static MetaTypeAsset _activeMetaMode;

        private static bool RequiresNativeMetaRenderer()
        {
            if (_activeMetaMode == null)
            {
                return false;
            }

            return _activeMetaMode.map_mode == MetaType.Culture
                || _activeMetaMode.map_mode == MetaType.Religion
                || _activeMetaMode.map_mode == MetaType.Subspecies;
        }

        private static void Postfix(ref float __result)
        {
            if (RequiresNativeMetaRenderer())
            {
                return;
            }

            float mapFullViewThreshold = Mathf.Max(MapBox.width, MapBox.height) * 1.1f + 1f;
            __result = Mathf.Min(360f, Mathf.Max(__result * 2.5f, mapFullViewThreshold));
        }

        internal static void SetMetaMode(MetaTypeAsset pAsset)
        {
            if (ReferenceEquals(_activeMetaMode, pAsset))
            {
                return;
            }

            _activeMetaMode = pAsset;

            MapBox world = MapBox.instance;
            if (world == null || world.quality_changer == null || world.camera == null)
            {
                return;
            }

            // Re-run the native transition immediately.  Without this, a
            // culture map selected at a fixed zoom would wait for the user to
            // move the camera before QualityChanger notices the new bound.
            world.quality_changer.setZoomOrthographic(world.camera.orthographicSize);
        }
    }

    [HarmonyPatch(typeof(ZoneCalculator), "setMode")]
    internal static class AerialMetaModeTracker
    {
        private static void Postfix(MetaTypeAsset pAsset)
        {
            AerialQualityPolicy.SetMetaMode(pAsset);
        }
    }
}
