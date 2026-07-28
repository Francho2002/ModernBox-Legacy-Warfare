using System;

namespace ModernBox
{
    /// <summary>
    /// Keeps the native random-traits world law enabled by default for worlds
    /// created after ModernBox loads.  It changes the asset default only: a
    /// value stored in an existing world remains under the player's control.
    /// </summary>
    internal static class WorldLawDefaults
    {
        internal static void EnableRandomCultureReligionTraitsByDefault()
        {
            try
            {
                WorldLawAsset law = WorldLawLibrary.world_law_glitched_noosphere;
                if (law == null)
                {
                    ModernBoxLogger.Warning("[MX.WorldLaws] No se encontró la ley de rasgos aleatorios.");
                    return;
                }

                // In build 719 this is the native rule that expands the
                // random trait pool for cultures and religions (and, by the
                // game's own design, languages and clans) on new worlds.
                law.default_state = true;
                ModernBoxLogger.Log("[MX.WorldLaws] Rasgos aleatorios de culturas y religiones activados por defecto.");
            }
            catch (Exception ex)
            {
                ModernBoxLogger.Warning("[MX.WorldLaws] No se pudo configurar el valor predeterminado: " + ex.Message);
            }
        }
    }
}
