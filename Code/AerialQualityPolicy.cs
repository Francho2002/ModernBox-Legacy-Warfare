using HarmonyLib;
using UnityEngine;

namespace ModernBox
{
    // Keeps the native low-resolution aerial mode out of the normal overview
    // range.  The native bound varies with aspect ratio, so patch the final
    // calculated value instead of changing the camera's maximum zoom.
    [HarmonyPatch(typeof(QualityChanger), "getZoomRateBoundLow")]
    internal static class AerialQualityPolicy
    {
        private static void Postfix(ref float __result)
        {
            float mapFullViewThreshold = Mathf.Max(MapBox.width, MapBox.height) * 1.1f + 1f;
            __result = Mathf.Min(360f, Mathf.Max(__result * 2.5f, mapFullViewThreshold));
        }
    }
}
