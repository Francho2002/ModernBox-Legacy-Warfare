using System;
using System.Collections.Generic;
using UnityEngine;

namespace ModernBox
{
    /// <summary>
    /// A small, session-only strategic identity for each kingdom.  It changes
    /// production preferences, never technology, culture, buildings or eras.
    /// </summary>
    internal enum MilitaryDoctrine
    {
        Balanced,
        Defensive,
        Armored,
        Air,
        Strategic,
        Naval
    }

    internal static class MilitaryDoctrineService
    {
        // This cache intentionally lives only for the running game.  A doctrine
        // is derived from the stable kingdom id, so re-reading it is deterministic
        // without writing PlayerPrefs or touching saves.
        private static readonly Dictionary<long, MilitaryDoctrine> SessionDoctrines =
            new Dictionary<long, MilitaryDoctrine>();

        internal static MilitaryDoctrine GetDoctrine(Kingdom kingdom)
        {
            if (kingdom == null)
                return MilitaryDoctrine.Balanced;

            if (SessionDoctrines.TryGetValue(kingdom.id, out MilitaryDoctrine doctrine))
                return doctrine;

            doctrine = (MilitaryDoctrine)(StableHash(kingdom.id) % 6);
            SessionDoctrines[kingdom.id] = doctrine;
            return doctrine;
        }

        internal static string GetDisplayName(Kingdom kingdom)
        {
            return GetDoctrineName(GetDoctrine(kingdom));
        }

        internal static string GetDoctrineName(MilitaryDoctrine doctrine)
        {
            switch (doctrine)
            {
                case MilitaryDoctrine.Defensive:
                    return "Defensiva";
                case MilitaryDoctrine.Armored:
                    return "Blindada";
                case MilitaryDoctrine.Air:
                    return "Aérea";
                case MilitaryDoctrine.Strategic:
                    return "Estratégica";
                case MilitaryDoctrine.Naval:
                    return "Naval";
                default:
                    return "Equilibrada";
            }
        }

        /// <summary>
        /// Returns a modest land-production multiplier. Naval doctrine is
        /// deliberately neutral until naval production consumes this API.
        /// </summary>
        internal static float GetRoleWeight(Kingdom kingdom, string role)
        {
            if (string.IsNullOrEmpty(role))
                return 1f;

            MilitaryDoctrine doctrine = GetDoctrine(kingdom);
            switch (doctrine)
            {
                case MilitaryDoctrine.Defensive:
                    if (role == "support") return 1.20f;
                    if (role == "heavy") return 1.10f;
                    if (role == "offensive") return 0.90f;
                    if (role == "air") return 0.90f;
                    break;
                case MilitaryDoctrine.Armored:
                    if (role == "heavy") return 1.25f;
                    if (role == "offensive") return 1.10f;
                    if (role == "support") return 0.90f;
                    if (role == "air") return 0.85f;
                    break;
                case MilitaryDoctrine.Air:
                    if (role == "air") return 1.30f;
                    if (role == "offensive") return 1.05f;
                    if (role == "heavy") return 0.90f;
                    if (role == "support") return 0.90f;
                    break;
                case MilitaryDoctrine.Strategic:
                    if (role == "heavy") return 1.15f;
                    if (role == "air") return 1.10f;
                    if (role == "support") return 1.05f;
                    if (role == "offensive") return 0.85f;
                    break;
                // Naval is intentionally neutral for land production.  Its
                // ship/submarine preferences belong to the naval production pass.
                case MilitaryDoctrine.Naval:
                case MilitaryDoctrine.Balanced:
                default:
                    break;
            }

            return 1f;
        }

        internal static float GetDefensiveLauncherPreference(Kingdom kingdom)
        {
            switch (GetDoctrine(kingdom))
            {
                case MilitaryDoctrine.Defensive:
                    return 1.25f;
                case MilitaryDoctrine.Strategic:
                    return 1.35f;
                case MilitaryDoctrine.Armored:
                    return 0.85f;
                case MilitaryDoctrine.Air:
                    return 0.90f;
                default:
                    return 1f;
            }
        }

        /// <summary>
        /// Only defensive and strategic kingdoms reserve a base chassis for a
        /// launcher. The independent slow launcher controller still gives every
        /// qualified city its single defensive system.
        /// </summary>
        internal static bool ShouldReserveDefensiveLauncher(City city)
        {
            return GetDefensiveLauncherPreference(city?.kingdom) > 1f;
        }

        internal static string PickLandRole(Kingdom kingdom, int militaryLevel)
        {
            // The ordinary chassis pool keeps broader air assets at level four.
            // Fighter/bomber production at level three has its own bounded
            // launcher-cycle route.
            float offensive = GetRoleWeight(kingdom, "offensive") * 5.5f;
            float heavy = GetRoleWeight(kingdom, "heavy") * (militaryLevel >= 2 ? 2.0f : 1.1f);
            float support = GetRoleWeight(kingdom, "support") * 1.15f;
            float air = militaryLevel >= 4 ? GetRoleWeight(kingdom, "air") * 1.25f : 0f;

            float total = offensive + heavy + support + air;
            float roll = UnityEngine.Random.Range(0f, total);
            if ((roll -= offensive) < 0f) return "offensive";
            if ((roll -= heavy) < 0f) return "heavy";
            if ((roll -= support) < 0f) return "support";
            return "air";
        }

        internal static void ResetSession()
        {
            SessionDoctrines.Clear();
        }

        private static int StableHash(long value)
        {
            unchecked
            {
                uint hash = 2166136261u;
                ulong bits = (ulong)value;
                for (int i = 0; i < 8; i++)
                {
                    hash ^= (byte)(bits & 0xff);
                    hash *= 16777619u;
                    bits >>= 8;
                }
                return (int)(hash & 0x7fffffffu);
            }
        }
    }
}
