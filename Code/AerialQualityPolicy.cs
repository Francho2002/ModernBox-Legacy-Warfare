using HarmonyLib;

namespace ModernBox
{
    // In WorldBox build 719, the native low-resolution aerial mode is also
    // the switch for culture/religion/subspecies chunk overlays and their
    // tile click actions.  Do not delay it: doing so hides those native map
    // layers and prevents their detail windows from opening.
    [HarmonyPatch(typeof(QualityChanger), "getZoomRateBoundLow")]
    internal static class AerialQualityPolicy
    {
        private static bool Prepare() => false;
    }
}
