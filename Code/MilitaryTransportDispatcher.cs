using System;
using System.Collections.Generic;
using System.Linq;
using life.taxi;
using UnityEngine;

namespace ModernBox
{
    /// <summary>
    /// Gives WorldBox's native taxi task a bounded chance to take military
    /// transports when an inter-island invasion is waiting.  Warships still
    /// use their ordinary combat task when no civilian/army taxi request is
    /// pending; this controller only replaces an already-running war task.
    /// </summary>
    internal sealed class MilitaryTransportDispatcher : MonoBehaviour
    {
        private const float RosterRefreshSeconds = 18f;
        // TaxiManager may inspect a substantial request list.  A small,
        // deliberate cadence avoids rebuilding that work every simulation
        // tick while still reacting to an invasion within a few seconds.
        private const float DispatchIntervalSeconds = 0.75f;
        private const int ShipsPerCycle = 2;
        private const string WarBoatTaskId = "warBoatAttackDecision";
        private const string NativeTransportTaskId = "boat_transport_check";

        private readonly List<Actor> _ships = new List<Actor>();
        private float _nextRosterRefresh;
        private float _nextDispatch;
        private int _cursor;

        private void Awake()
        {
            _nextRosterRefresh = 0f;
            _nextDispatch = 0f;
        }

        private void Update()
        {
            if (Time.time < _nextDispatch)
                return;

            _nextDispatch = Time.time + DispatchIntervalSeconds;
            try
            {
                if (Time.time >= _nextRosterRefresh)
                    RefreshRoster();

                DispatchSlice();
            }
            catch (Exception ex)
            {
                // A bad or unloading save must never take down the simulation.
                // The next bounded pass can retry after WorldBox has settled.
                ModernBoxLogger.Warning("[MX.Transport] Dispatcher pass failed: " + ex.Message);
            }
        }

        private void RefreshRoster()
        {
            _ships.Clear();
            _cursor = 0;
            _nextRosterRefresh = Time.time + RosterRefreshSeconds;

            if (World.world?.units == null)
                return;

            foreach (Actor actor in World.world.units.Cast<Actor>())
            {
                if (IsMilitaryTransport(actor))
                    _ships.Add(actor);
            }
        }

        private void DispatchSlice()
        {
            if (_ships.Count == 0)
                return;

            int count = Math.Min(ShipsPerCycle, _ships.Count);
            for (int index = 0; index < count; index++)
            {
                if (_cursor >= _ships.Count)
                    _cursor = 0;

                TryDispatch(_ships[_cursor++]);
            }
        }

        private static void TryDispatch(Actor ship)
        {
            if (!IsMilitaryTransport(ship) || ship.ai == null)
                return;

            string currentTask = ship.ai.task?.id;
            if (string.Equals(currentTask, NativeTransportTaskId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(currentTask, WarBoatTaskId, StringComparison.OrdinalIgnoreCase))
                return;

            // This native lookup is deliberately read-only: it merely returns
            // a pending request that this hull could serve (or its existing
            // assignment).  BehBoatFindRequest performs the real assignment.
            TaxiRequest request = TaxiManager.getNewRequestForBoat(ship);
            if (request == null || !request.isStillLegit())
                return;

            // Do not call cancelAllBeh here: Boat.cancelWork would also cancel
            // the native taxi assignment. Replacing only the current task lets
            // BehBoatTransportCheck acquire, load, cross and unload normally.
            ship.setTask(NativeTransportTaskId, true, false, true);
        }

        private static bool IsMilitaryTransport(Actor actor)
        {
            if (actor == null || !actor.isAlive() || actor.current_tile == null || actor.kingdom == null)
                return false;

            string id = actor.asset?.id;
            if (string.IsNullOrEmpty(id) || !actor.asset.is_boat || !actor.asset.is_boat_transport)
                return false;

            return id.StartsWith("aDestroyer_", StringComparison.OrdinalIgnoreCase) ||
                   id.StartsWith("bDestroyer_", StringComparison.OrdinalIgnoreCase) ||
                   id.StartsWith("CarrierVessel_", StringComparison.OrdinalIgnoreCase);
        }
    }
}
