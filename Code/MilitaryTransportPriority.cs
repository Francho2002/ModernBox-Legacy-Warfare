using System;
using System.Reflection;
using HarmonyLib;
using ai.behaviours;

namespace ModernBox
{
    /// <summary>
    /// Lets the game's own boat taxi behaviour finish an assigned military
    /// transport before ModernBox's war-boat objective code takes the helm
    /// again.  It deliberately applies only to destroyers and carriers;
    /// Trainbox rail boats and every civilian boat retain their own logic.
    /// </summary>
    internal static class MilitaryTransportPriority
    {
        // These are assembly-internal in WorldBox build 719. Cache the real
        // runtime members once instead of assuming a public wrapper exists.
        private static readonly FieldInfo TaxiRequestField =
            AccessTools.Field(typeof(Boat), "taxi_request");
        private static readonly MethodInfo HasPassengersMethod =
            AccessTools.Method(typeof(Boat), "hasPassengers");

        internal static bool HasNativeTransportWork(Actor actor)
        {
            if (!IsMilitaryTransport(actor))
                return false;

            Boat boat = actor.getSimpleComponent<Boat>();
            if (boat == null)
                return false;

            try
            {
                // A request covers pickup and transit; taxi_target remains
                // populated by WorldBox while the boat is navigating toward
                // its loading or landing position. Passengers cover the
                // short interval during which the request is being finished.
                if (TaxiRequestField?.GetValue(boat) != null || boat.taxi_target != null)
                    return true;

                return HasPassengersMethod != null &&
                    HasPassengersMethod.Invoke(boat, null) is bool hasPassengers && hasPassengers;
            }
            catch
            {
                // Never suppress a warship just because a game update changed
                // an internal taxi member. The native combat behaviour is the
                // safe fallback in that case.
                return false;
            }
        }

        private static bool IsMilitaryTransport(Actor actor)
        {
            ActorAsset asset = actor?.asset;
            string id = asset?.id;
            if (asset == null || !asset.is_boat || !asset.is_boat_transport || string.IsNullOrEmpty(id))
                return false;

            return id.StartsWith("aDestroyer_", StringComparison.OrdinalIgnoreCase) ||
                   id.StartsWith("bDestroyer_", StringComparison.OrdinalIgnoreCase) ||
                   id.StartsWith("CarrierVessel_", StringComparison.OrdinalIgnoreCase);
        }
    }

    // `warBoatAttackDecision` is a long-running behaviour while a kingdom is
    // at war. Stop it only once native taxi work exists, so the stock
    // `boat_transport_check` behaviour owns the pickup, crossing and landing.
    [HarmonyPatch(typeof(BehWarBoatFindTarget), nameof(BehWarBoatFindTarget.execute))]
    internal static class MilitaryTransportWarBoatGuard
    {
        [HarmonyPrefix]
        private static bool Prefix(Actor pActor, ref BehResult __result)
        {
            if (!MilitaryTransportPriority.HasNativeTransportWork(pActor))
                return true;

            __result = BehResult.Stop;
            return false;
        }
    }

    // FleetOrganization is a postfix on the same native target selection.
    // Guard it separately so it cannot overwrite the taxi destination after
    // the war-boat behaviour has been yielded.
    [HarmonyPatch(typeof(FleetOrganization), nameof(FleetOrganization.ApplySharedTarget))]
    internal static class MilitaryTransportFleetOrderGuard
    {
        [HarmonyPrefix]
        private static bool Prefix(Actor boat)
        {
            return !MilitaryTransportPriority.HasNativeTransportWork(boat);
        }
    }
}
