using System;
using System.Collections.Generic;

namespace ModernBox
{
    // Small gate over the original transformation and spawn-button paths. It
    // leaves the original era tables intact while excluding non-realistic units.
    internal static class ModernCapPolicy
    {
        private static readonly HashSet<string> ExcludedActors = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SpaceMarine", "Terran", "spaceork", "teslatruckgun", "atst", "artilleryatst",
            "atstsniper", "P9000", "dreadnaught", "dreadnaught_brrt", "Railgun", "MA9000",
            "HumanTitan", "AT9000", "FutureGunship", "supportatst", "HeliELite", "TIEfighter",
            "EliteBomber", "HumanTitanElite", "OmegaRailgun", "EliteP9000", "eliteMA9000",
            "eliteAT9000", "ogreunit", "armoredwolf", "golemgem", "treant", "demonscorpion",
            "demonwyvern", "xenolevitank", "xenoUFO", "santaguin", "woolyrhino", "demoncroc",
            "demongolem", "demonreaver", "xenorailgun", "xenotripod", "humanpaladin",
            "orcwarlock", "dwarfdoctor", "fairydragon", "bigfaerydragon", "Bomber_Demon",
            "xenoUFObomber", "davincitank", "balloonunit"
        };

        // Explicit archive/monster entries complement the allowlist below. They
        // keep save-compatible actor assets out of spawn menus and production.
        private static readonly HashSet<string> FantasyActors = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Gojira", "MegaGojira", "Longlegder", "Rodanix", "Invaderax", "PanKong",
            "Skullcrawler", "crabzilord", "Ramiel", "Gaghiel", "Sachiel", "Zeruel",
            "mechacrabzilla", "Anguirus", "Bagan", "Battra", "BigBiolante", "QueenMuto",
            "Crystalac", "Desghidorah", "Destoroyah", "Gamera", "GiantSquid", "GiganOld",
            "GoodGodzilla", "Hedorah", "Iris", "KiryuMech", "Kong", "Legion", "LpgKaiju",
            "MechaGhidorah", "Megalon", "OldMechagodzilla", "Shimo", "SkerBuffalo",
            "FemaleMuto", "MaleMuto", "SpaceGodzilla", "SporeMantis", "SuperMechagodzilla"
        };

        internal static bool IsAllowedActor(string actorId)
        {
            if (string.IsNullOrEmpty(actorId) || ExcludedActors.Contains(actorId))
                return false;

            if (!Main.EnableFantasySystems && FantasyActors.Contains(actorId))
                return false;

            return actorId.StartsWith("trainbox_", StringComparison.OrdinalIgnoreCase)
                || actorId.StartsWith("howitzer_", StringComparison.OrdinalIgnoreCase)
                || actorId.StartsWith("Tank_", StringComparison.OrdinalIgnoreCase)
                || actorId.StartsWith("MissileSystem_", StringComparison.OrdinalIgnoreCase)
                || actorId.StartsWith("wheeledtank_", StringComparison.OrdinalIgnoreCase)
                || actorId.StartsWith("supporttruck_", StringComparison.OrdinalIgnoreCase)
                || actorId.StartsWith("modernhumvee_", StringComparison.OrdinalIgnoreCase)
                || actorId.StartsWith("Heli_", StringComparison.OrdinalIgnoreCase)
                || actorId.StartsWith("FighterJet_", StringComparison.OrdinalIgnoreCase)
                || actorId.StartsWith("Bomber_", StringComparison.OrdinalIgnoreCase)
                || actorId.StartsWith("CargoShip_", StringComparison.OrdinalIgnoreCase)
                || actorId.StartsWith("FishingBoat_", StringComparison.OrdinalIgnoreCase)
                || actorId.StartsWith("Transporter_", StringComparison.OrdinalIgnoreCase)
                || actorId.StartsWith("aDestroyer_", StringComparison.OrdinalIgnoreCase)
                || actorId.StartsWith("bDestroyer_", StringComparison.OrdinalIgnoreCase)
                || actorId.StartsWith("CarrierVessel_", StringComparison.OrdinalIgnoreCase)
                || NavalRoles.IsAnyModernSubmarine(actorId)
                || IsConventionalExact(actorId);
        }

        private static bool IsConventionalExact(string actorId)
        {
            return string.Equals(actorId, "catapulta", StringComparison.OrdinalIgnoreCase)
                || string.Equals(actorId, "orcatapulta", StringComparison.OrdinalIgnoreCase)
                || string.Equals(actorId, "batteringram", StringComparison.OrdinalIgnoreCase)
                || string.Equals(actorId, "humancavalry", StringComparison.OrdinalIgnoreCase)
                || string.Equals(actorId, "humancannon", StringComparison.OrdinalIgnoreCase)
                || string.Equals(actorId, "orccannon", StringComparison.OrdinalIgnoreCase)
                || string.Equals(actorId, "dwarfcannon", StringComparison.OrdinalIgnoreCase)
                || string.Equals(actorId, "elfcannon", StringComparison.OrdinalIgnoreCase)
                || string.Equals(actorId, "Humvee", StringComparison.OrdinalIgnoreCase)
                || string.Equals(actorId, "AbramTank", StringComparison.OrdinalIgnoreCase)
                || string.Equals(actorId, "shermanww", StringComparison.OrdinalIgnoreCase)
                || string.Equals(actorId, "tankie", StringComparison.OrdinalIgnoreCase)
                || string.Equals(actorId, "genericwwtank", StringComparison.OrdinalIgnoreCase)
                || string.Equals(actorId, "landship", StringComparison.OrdinalIgnoreCase)
                || string.Equals(actorId, "bigtankww", StringComparison.OrdinalIgnoreCase)
                || string.Equals(actorId, "wwsupporttruck", StringComparison.OrdinalIgnoreCase)
                || string.Equals(actorId, "americanbomberww", StringComparison.OrdinalIgnoreCase)
                || string.Equals(actorId, "biplane", StringComparison.OrdinalIgnoreCase)
                || string.Equals(actorId, "fighterww", StringComparison.OrdinalIgnoreCase)
                || string.Equals(actorId, "Zeppelin", StringComparison.OrdinalIgnoreCase)
                || string.Equals(actorId, "EliteZeppelin", StringComparison.OrdinalIgnoreCase)
                || string.Equals(actorId, "F55FighterJet", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsArtillery(string actorId)
        {
            if (string.IsNullOrEmpty(actorId))
                return false;
            return actorId.StartsWith("howitzer_", StringComparison.OrdinalIgnoreCase)
                || actorId.StartsWith("MissileSystem_", StringComparison.OrdinalIgnoreCase)
                || string.Equals(actorId, "catapulta", StringComparison.OrdinalIgnoreCase)
                || string.Equals(actorId, "orcatapulta", StringComparison.OrdinalIgnoreCase)
                 || actorId.IndexOf("cannon", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static bool IsAllowedAircraft(string actorId)
        {
            if (string.IsNullOrEmpty(actorId) || !IsAllowedActor(actorId))
                return false;

            return actorId.StartsWith("Heli_", StringComparison.OrdinalIgnoreCase)
                || actorId.StartsWith("FighterJet_", StringComparison.OrdinalIgnoreCase)
                || actorId.StartsWith("Bomber_", StringComparison.OrdinalIgnoreCase)
                || string.Equals(actorId, "F55FighterJet", StringComparison.OrdinalIgnoreCase)
                || string.Equals(actorId, "americanbomberww", StringComparison.OrdinalIgnoreCase)
                || string.Equals(actorId, "biplane", StringComparison.OrdinalIgnoreCase)
                || string.Equals(actorId, "fighterww", StringComparison.OrdinalIgnoreCase)
                || string.Equals(actorId, "Zeppelin", StringComparison.OrdinalIgnoreCase)
                || string.Equals(actorId, "EliteZeppelin", StringComparison.OrdinalIgnoreCase);
        }

        // The transformation tables describe land warfare.  Trains and ships are
        // allowed elsewhere, but must never consume a city's land-vehicle cap.
        internal static bool IsLandMilitaryActor(string actorId)
        {
            if (!IsAllowedActor(actorId))
                return false;
            return !actorId.StartsWith("trainbox_", StringComparison.OrdinalIgnoreCase)
                && !actorId.StartsWith("CargoShip_", StringComparison.OrdinalIgnoreCase)
                && !actorId.StartsWith("FishingBoat_", StringComparison.OrdinalIgnoreCase)
                && !actorId.StartsWith("Transporter_", StringComparison.OrdinalIgnoreCase)
                && !actorId.StartsWith("aDestroyer_", StringComparison.OrdinalIgnoreCase)
                && !actorId.StartsWith("bDestroyer_", StringComparison.OrdinalIgnoreCase)
                && !actorId.StartsWith("CarrierVessel_", StringComparison.OrdinalIgnoreCase)
                && !NavalRoles.IsAnyModernSubmarine(actorId);
        }

        internal static bool CanSelectCandidate(City city, string actorId, string eraKey)
        {
            if (!IsAllowedActor(actorId))
                return false;
            if (!IsArtillery(actorId))
                return true;

            int cap = string.Equals(eraKey, "modern", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
            int artillery = 0;
            if (city?.units != null)
            {
                foreach (Actor unit in city.units)
                {
                    if (unit != null && unit.isAlive() && IsArtillery(unit.asset?.id))
                        artillery++;
                }
            }
            return artillery < cap;
        }
    }
}
