using HarmonyLib;

namespace ModernBox
{
    [HarmonyPatch(typeof(PlayerControl), "isPointerOverUIObject")]
    public static class Patch_isPointerOverUIObject
    {
        public static bool Prefix()
        {
            // WorldBox owns this hit test. The legacy override cached map clicks as UI,
            // which prevented native map selections such as culture panels.
            return true;
        }
    }
}
