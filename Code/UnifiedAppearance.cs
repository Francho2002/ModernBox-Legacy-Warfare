using HarmonyLib;

namespace ModernBox
{
    /// <summary>
    /// Visual era is an explicit presentation choice.  It is intentionally
    /// independent from the military pool and never follows bonfire levels.
    /// </summary>
    internal static class UnifiedAppearance
    {
        internal static string CurrentVisualEra()
        {
            string requested = StatManager.Instance?.eraoverride;
            return requested == "renaissance" || requested == "modern"
                ? requested
                : "medieval";
        }

        internal static void ApplyCurrentVisualEra()
        {
            // Appearance is intentionally non-functional in the unified
            // profile. Do not invoke the legacy world conversion here.
        }
    }

    [HarmonyPatch(typeof(MapBox), "addLastStep")]
    internal static class UnifiedAppearanceWorldReadyPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Normal)]
        private static void Postfix()
        {
            // Loading a world must not mutate its buildings or technology.
            UnifiedAppearance.ApplyCurrentVisualEra();
        }
    }
}
