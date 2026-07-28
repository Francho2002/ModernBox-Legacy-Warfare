using System;
using System.Reflection;
using HarmonyLib;
using tools;

namespace ModernBox
{
    // WorldBox can retain a transferred/dissolved kingdom's alliance-zone
    // entry in a save.  The native renderer assumes that entry is never null
    // and then redraws it every frame after throwing.  Leave valid zones on
    // the native path; only clear the stale one through its null-safe meta
    // renderer.
    [HarmonyPatch]
    internal static class AllianceZoneSafety
    {
        private static bool _reportedInvalidZone;

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(ZoneCalculator),
                "drawZoneAlliance",
                new[] { typeof(TileZone), typeof(int) });
        }

        private static bool Prefix(ZoneCalculator __instance, TileZone pZone, int pZoneOption)
        {
            if (pZone == null)
                return false;

            try
            {
                var alliance = pZone.getAllianceOnZone(pZoneOption);
                // No alliance is the normal state for a zone.  The native
                // renderer must still receive it so it can preserve the
                // kingdom/culture fill beneath the optional alliance layer.
                if (alliance == null || alliance.data != null)
                    return true;

                // Only a real but malformed alliance entry needs the
                // null-safe fallback.  This is the stale-save case the patch
                // exists to protect against.
                __instance.drawZoneMeta(pZone, MetaTypeLibrary.alliance, _ => null);
                ReportInvalidZoneOnce();
            }
            catch (Exception ex)
            {
                // A malformed saved zone must never make MapBox.Update throw
                // forever.  Valid alliance zones remain untouched above.
                ReportInvalidZoneOnce("[MX.Zone] Stale alliance zone was skipped safely: " + ex.Message);
            }

            return false;
        }

        private static void ReportInvalidZoneOnce(string message = null)
        {
            if (_reportedInvalidZone)
                return;

            _reportedInvalidZone = true;
            ModernBoxLogger.Log(message ?? "[MX.Zone] Cleared a stale alliance-zone entry from the loaded world.");
        }
    }
}
