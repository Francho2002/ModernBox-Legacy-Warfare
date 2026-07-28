using System.Collections.Generic;
using UnityEngine;

namespace ModernBox
{
    // Short-lived coordination only: decisions reserve a point when they pick
    // it, so simultaneous submarines from one kingdom spread their warheads.
    internal static class SubmarineTargetReservations
    {
        private const float ReservationSeconds = 18f;

        private sealed class Reservation
        {
            internal Vector2 target;
            internal float separation;
            internal float expiresAt;
        }

        private static readonly Dictionary<Kingdom, List<Reservation>> Reservations =
            new Dictionary<Kingdom, List<Reservation>>();

        internal static bool TryReserve(Actor caster, Vector2 target, float separation)
        {
            Kingdom kingdom = caster?.kingdom;
            if (kingdom == null)
                return false;

            if (!Reservations.TryGetValue(kingdom, out List<Reservation> ledger))
            {
                ledger = new List<Reservation>();
                Reservations[kingdom] = ledger;
            }

            float now = Time.time;
            ledger.RemoveAll(entry => entry.expiresAt <= now);
            foreach (Reservation entry in ledger)
            {
                if (Vector2.Distance(entry.target, target) < Mathf.Max(entry.separation, separation))
                    return false;
            }

            ledger.Add(new Reservation
            {
                target = target,
                separation = separation,
                expiresAt = now + ReservationSeconds
            });
            return true;
        }
    }
}
