using System;
using System.Linq;
using HarmonyLib;

namespace ModernBox
{
    /// <summary>
    /// Repairs passenger links left behind when a transport boat disappears.
    /// WorldBox's actor updater dereferences inside_boat.actor every simulation
    /// cycle, so one stale link otherwise becomes an endless exception loop.
    /// </summary>
    internal static class NavalPassengerSafety
    {
        private static readonly AccessTools.FieldRef<Actor, bool> IsInsideBoatRef =
            TryCreateFieldRef<Actor, bool>("is_inside_boat");
        private static readonly AccessTools.FieldRef<Actor, Boat> InsideBoatRef =
            TryCreateFieldRef<Actor, Boat>("inside_boat");
        private static readonly AccessTools.FieldRef<ActorSimpleComponent, Actor> BoatActorRef =
            TryCreateFieldRef<ActorSimpleComponent, Actor>("actor");

        private static int _reportedRepairs;

        internal static void RepairBeforeInsideUpdate(Actor passenger)
        {
            if (passenger == null || IsInsideBoatRef == null || InsideBoatRef == null ||
                BoatActorRef == null)
                return;

            try
            {
                if (!IsInsideBoatRef(passenger))
                    return;

                Boat boat = InsideBoatRef(passenger);
                Actor host = boat == null ? null : BoatActorRef(boat);
                if (host != null && host.isAlive() && host.current_tile != null)
                    return;

                ClearLink(passenger, boat);
                ReportRepair(passenger);
            }
            catch
            {
                // This guard runs in WorldBox's actor hot path. If a future
                // build changes the component layout, preserve native behavior
                // instead of replacing one exception loop with another.
            }
        }

        internal static void ClearPassengersBeforeDispose(Boat boat)
        {
            if (boat == null || IsInsideBoatRef == null || InsideBoatRef == null)
                return;

            try
            {
                var passengers = boat.getPassengers();
                if (passengers == null)
                    return;

                foreach (Actor passenger in passengers.ToList())
                {
                    if (passenger != null)
                        ClearLink(passenger, boat);
                }
            }
            catch
            {
                // Boat.Dispose will still complete. The per-actor safety net
                // above repairs any link that could not be enumerated here.
            }
        }

        private static void ClearLink(Actor passenger, Boat expectedBoat)
        {
            if (passenger == null)
                return;

            Boat current = InsideBoatRef(passenger);
            if (expectedBoat != null && current != null && current != expectedBoat)
                return;

            InsideBoatRef(passenger) = null;
            IsInsideBoatRef(passenger) = false;
        }

        private static void ReportRepair(Actor passenger)
        {
            _reportedRepairs++;
            if (_reportedRepairs <= 4)
            {
                ModernBoxLogger.Warning("[MX.Transport] Repaired a stale passenger link for actor " +
                    passenger.id + ".");
            }
            else if (_reportedRepairs == 5)
            {
                ModernBoxLogger.Warning("[MX.Transport] Further stale passenger repairs will be silent.");
            }
        }

        private static AccessTools.FieldRef<TTarget, TField> TryCreateFieldRef<TTarget, TField>(
            string fieldName)
        {
            try
            {
                return AccessTools.FieldRefAccess<TTarget, TField>(fieldName);
            }
            catch (Exception ex)
            {
                ModernBoxLogger.Warning("[MX.Transport] Missing build-719 field " + fieldName +
                    ": " + ex.Message);
                return null;
            }
        }
    }

    [HarmonyPatch(typeof(Actor), "u1_checkInside")]
    internal static class NavalPassengerInsideUpdatePatch
    {
        [HarmonyPrefix]
        private static void Prefix(Actor __instance)
        {
            NavalPassengerSafety.RepairBeforeInsideUpdate(__instance);
        }
    }

    [HarmonyPatch(typeof(Boat), "Dispose")]
    internal static class NavalPassengerDisposePatch
    {
        [HarmonyPrefix]
        private static void Prefix(Boat __instance)
        {
            NavalPassengerSafety.ClearPassengersBeforeDispose(__instance);
        }
    }
}
