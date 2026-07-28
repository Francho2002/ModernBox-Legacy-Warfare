using System.Collections.Generic;
using UnityEngine;

namespace ModernBox
{
    // Reservation lanes prevent a conventional salvo from blocking a nuclear
    // strike (and vice versa) while still spreading simultaneous attacks of
    // the same tactical family.
    internal enum SubmarineTargetLane
    {
        Conventional,
        Strategic,
        Electronic
    }

    // Short-lived coordination only: decisions reserve a point when they pick
    // it, so simultaneous submarines from one kingdom spread their warheads.
    internal static class SubmarineTargetReservations
    {
        private const float ReservationSeconds = 18f;

        private sealed class Reservation
        {
            internal long actorId;
            internal Vector2 target;
            internal float separation;
            internal float expiresAt;
        }

        private static readonly Dictionary<Kingdom, Dictionary<SubmarineTargetLane, List<Reservation>>> Reservations =
            new Dictionary<Kingdom, Dictionary<SubmarineTargetLane, List<Reservation>>>();

        internal static bool TryReserve(Actor caster, Vector2 target, float separation,
            SubmarineTargetLane lane, bool allowExistingReservation = false)
        {
            Kingdom kingdom = caster?.kingdom;
            if (kingdom == null)
                return false;

            if (!Reservations.TryGetValue(kingdom, out Dictionary<SubmarineTargetLane, List<Reservation>> ledgers))
            {
                ledgers = new Dictionary<SubmarineTargetLane, List<Reservation>>();
                Reservations[kingdom] = ledgers;
            }
            if (!ledgers.TryGetValue(lane, out List<Reservation> ledger))
            {
                ledger = new List<Reservation>();
                ledgers[lane] = ledger;
            }

            float now = Time.time;
            ledger.RemoveAll(entry => entry.expiresAt <= now);
            if (!allowExistingReservation)
            {
                foreach (Reservation entry in ledger)
                {
                    if (Vector2.Distance(entry.target, target) < Mathf.Max(entry.separation, separation))
                        return false;
                }
            }

            ledger.Add(new Reservation
            {
                actorId = caster.getID(),
                target = target,
                separation = separation,
                expiresAt = now + ReservationSeconds
            });
            return true;
        }

        internal static void Release(Actor caster, Vector2 target, SubmarineTargetLane lane)
        {
            Kingdom kingdom = caster?.kingdom;
            if (kingdom == null || !Reservations.TryGetValue(kingdom, out Dictionary<SubmarineTargetLane, List<Reservation>> ledgers) ||
                !ledgers.TryGetValue(lane, out List<Reservation> ledger))
                return;

            long actorId = caster.getID();
            for (int index = ledger.Count - 1; index >= 0; index--)
            {
                Reservation entry = ledger[index];
                if (entry.actorId == actorId && Vector2.Distance(entry.target, target) < 0.1f)
                {
                    ledger.RemoveAt(index);
                    break;
                }
            }

            if (ledger.Count == 0)
                ledgers.Remove(lane);
            if (ledgers.Count == 0)
                Reservations.Remove(kingdom);
        }
    }
}
