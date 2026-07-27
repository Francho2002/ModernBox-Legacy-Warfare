using System;
using System.Collections.Generic;
using System.Linq;
using tools;
using UnityEngine;

namespace ModernBox
{
    // Carrier air operations are deliberately sampled instead of updated every
    // frame. A carrier owns at most one fighter and one bomber at a time.
    internal sealed class CarrierAirWingController : MonoBehaviour
    {
        private const float CycleSeconds = 4f;
        private const float RosterSeconds = 24f;
        private const float SortieSeconds = 52f;
        private const float RefitSeconds = 80f;
        private const float LostCarrierRtbSeconds = 48f;
        private const int CarriersPerCycle = 3;
        private const string FighterReplacementRequiredKey = "mb_carrier_fighter_replacement_required";
        private const string BomberReplacementRequiredKey = "mb_carrier_bomber_replacement_required";

        private sealed class WingState
        {
            internal Actor fighter;
            internal Actor bomber;
            internal float readyAt;
            internal float sortieEndsAt;
            internal float carrierLostAt;
        }

        private readonly List<Actor> _carriers = new List<Actor>();
        private readonly Dictionary<Actor, WingState> _wings = new Dictionary<Actor, WingState>();
        private float _nextCycle;
        private float _nextRoster;
        private int _cursor;

        private void Awake()
        {
            _nextCycle = Time.time + CycleSeconds;
        }

        private void Update()
        {
            if (Time.time < _nextCycle)
                return;

            _nextCycle = Time.time + CycleSeconds;
            try
            {
                if (Time.time >= _nextRoster)
                    RefreshRoster();
                ProcessCarrierSlice();
            }
            catch (Exception ex)
            {
                ModernBoxLogger.Error("[MX.Carrier] Cycle failure: " + ex.Message);
            }
        }

        private void RefreshRoster()
        {
            Vehicles.ResetCarrierCache();
            _carriers.Clear();
            _cursor = 0;
            _nextRoster = Time.time + RosterSeconds;
            if (World.world?.units == null)
                return;

            foreach (Actor actor in World.world.units.Cast<Actor>())
            {
                if (IsCarrier(actor))
                {
                    _carriers.Add(actor);
                    Vehicles.RegisterCarrier(actor);
                }
            }

            // Actor data survives a save/load. Rehydrate the lightweight
            // runtime links before any new sortie can be considered.
            foreach (Actor actor in World.world.units.Cast<Actor>())
            {
                if (!Vehicles.IsCarrierAircraft(actor))
                    continue;
                if (!Vehicles.TryGetCarrierForAircraft(actor, out Actor carrier))
                {
                    BeginTerrestrialFallback(actor);
                    continue;
                }
                if (!_wings.TryGetValue(carrier, out WingState state))
                {
                    state = new WingState { sortieEndsAt = Time.time + 24f };
                    _wings[carrier] = state;
                }
                if (actor.asset?.id.StartsWith("FighterJet_", StringComparison.OrdinalIgnoreCase) == true)
                    state.fighter = actor;
                else if (actor.asset?.id.StartsWith("Bomber_", StringComparison.OrdinalIgnoreCase) == true)
                    state.bomber = actor;
            }
        }

        private void ProcessCarrierSlice()
        {
            int count = Math.Min(CarriersPerCycle, _carriers.Count);
            for (int index = 0; index < count; index++)
            {
                if (_cursor >= _carriers.Count)
                    _cursor = 0;
                ProcessCarrier(_carriers[_cursor++]);
            }

            // Dead carriers leave the roster, but their marked aircraft must
            // still receive the terrestrial RTB fallback before being removed.
            foreach (Actor carrier in _wings.Keys.Where(carrier => !IsCarrier(carrier)).ToList())
                ProcessCarrier(carrier);
        }

        private void ProcessCarrier(Actor carrier)
        {
            if (!_wings.TryGetValue(carrier, out WingState state))
            {
                state = new WingState();
                _wings[carrier] = state;
            }

            if (!IsCarrier(carrier))
            {
                HandleLostCarrierWing(state);
                if (!HasLiveAircraft(state))
                    _wings.Remove(carrier);
                return;
            }

            if (!Traits.vehiclesAllowed)
            {
                ReturnToCarrier(carrier, state);
                RecoverAircraftOnDeck(carrier, state);
                return;
            }

            if (state.sortieEndsAt > 0f && Time.time >= state.sortieEndsAt)
                ReturnToCarrier(carrier, state);

            RecoverAircraftOnDeck(carrier, state);
            if (HasLiveAircraft(state) || Time.time < state.readyAt || !HasEnemyNearby(carrier))
                return;

            if (DeployWing(carrier, state))
            {
                state.sortieEndsAt = Time.time + SortieSeconds;
                state.readyAt = Time.time + RefitSeconds;
            }
        }

        private static bool DeployWing(Actor carrier, WingState state)
        {
            City city = carrier.city;
            if (city == null || carrier.current_tile == null)
                return false;

            string faction = GetFaction(carrier.asset?.id);
            string fighterId = "FighterJet_" + faction;
            string bomberId = "Bomber_" + faction;
            ConstructionCost fighterCost = new ConstructionCost(5, 4, 3, 1);
            ConstructionCost bomberCost = new ConstructionCost(7, 6, 5, 2);
            bool replaceFighter = GetReplacementRequired(carrier, FighterReplacementRequiredKey);
            bool replaceBomber = GetReplacementRequired(carrier, BomberReplacementRequiredKey);
            ConstructionCost replacementCost = replaceFighter && replaceBomber ? new ConstructionCost(12, 10, 8, 3) :
                replaceFighter ? fighterCost : replaceBomber ? bomberCost : new ConstructionCost(0, 0, 0, 0);
            // The carrier hull includes its initial wing. Only aircraft that
            // were actually lost consume city resources on replacement.
            if ((replaceFighter || replaceBomber) && !city.hasEnoughResourcesFor(replacementCost))
                return false;

            Actor fighter = SpawnAircraft(fighterId, carrier);
            if (fighter == null)
                return false;
            Actor bomber = SpawnAircraft(bomberId, carrier);
            if (bomber == null)
            {
                ActionLibrary.removeUnit(fighter);
                return false;
            }

            if (replaceFighter || replaceBomber)
                city.spendResourcesForBuildingAsset(replacementCost);
            SetReplacementRequired(carrier, FighterReplacementRequiredKey, false);
            SetReplacementRequired(carrier, BomberReplacementRequiredKey, false);
            state.fighter = fighter;
            state.bomber = bomber;
            AssignEnemyTarget(carrier, fighter);
            AssignEnemyTarget(carrier, bomber);
            return true;
        }

        private static Actor SpawnAircraft(string assetId, Actor carrier)
        {
            if (AssetManager.actor_library.get(assetId) == null)
                return null;
            Actor aircraft = World.world.units.createNewUnit(assetId, carrier.current_tile);
            if (aircraft == null)
                return null;
            aircraft.setKingdom(carrier.kingdom);
            aircraft.setCity(carrier.city);
            Vehicles.LinkCarrierAircraft(aircraft, carrier);
            if (carrier.hasHomeBuilding())
                aircraft.setHomeBuilding(carrier.getHomeBuilding());
            return aircraft;
        }

        private static void AssignEnemyTarget(Actor carrier, Actor aircraft)
        {
            Actor target = FindNearestEnemy(carrier);
            if (target?.current_tile == null || aircraft == null || !aircraft.isAlive())
                return;
            aircraft.setTileTarget(target.current_tile);
            aircraft.tryToAttack(target);
        }

        private static void ReturnToCarrier(Actor carrier, WingState state)
        {
            state.sortieEndsAt = 0f;
            Vehicles.ForceCarrierAircraftRtb(state.fighter);
            Vehicles.ForceCarrierAircraftRtb(state.bomber);
        }

        private static void RecoverAircraftOnDeck(Actor carrier, WingState state)
        {
            RecoverAircraft(carrier, state, ref state.fighter, carrier.current_tile, FighterReplacementRequiredKey);
            RecoverAircraft(carrier, state, ref state.bomber, carrier.current_tile, BomberReplacementRequiredKey);
        }

        private static void RecoverAircraft(Actor carrier, WingState state, ref Actor aircraft, WorldTile deck, string replacementKey)
        {
            if (aircraft == null)
                return;
            if (!aircraft.isAlive())
            {
                SetReplacementRequired(carrier, replacementKey, true);
                aircraft = null;
                return;
            }
            if (state.sortieEndsAt <= 0f && aircraft.current_tile != null &&
                Toolbox.DistTile(aircraft.current_tile, deck) <= 3)
            {
                ActionLibrary.removeUnit(aircraft);
                aircraft = null;
            }
        }

        private static void HandleLostCarrierWing(WingState state)
        {
            if (state.carrierLostAt <= 0f)
            {
                state.carrierLostAt = Time.time;
                BeginTerrestrialFallback(state.fighter);
                BeginTerrestrialFallback(state.bomber);
            }
            if (Time.time < state.carrierLostAt + LostCarrierRtbSeconds)
                return;
            RetireLostAircraft(ref state.fighter);
            RetireLostAircraft(ref state.bomber);
        }

        private static void ReturnToLand(Actor aircraft)
        {
            WorldTile destination = aircraft?.city?.leader?.current_tile;
            SetReturnTarget(aircraft, destination);
        }

        private static void BeginTerrestrialFallback(Actor aircraft)
        {
            Vehicles.ForceCarrierAircraftRtb(aircraft);
            Vehicles.UnlinkCarrierAircraft(aircraft);
            ReturnToLand(aircraft);
        }

        private static void SetReturnTarget(Actor aircraft, WorldTile destination)
        {
            if (aircraft != null && aircraft.isAlive() && destination != null)
            {
                aircraft.clearAttackTarget();
                aircraft.setTileTarget(destination);
            }
        }

        private static void RetireLostAircraft(ref Actor aircraft)
        {
            if (aircraft != null && aircraft.isAlive())
                ActionLibrary.removeUnit(aircraft);
            aircraft = null;
        }

        private static bool HasLiveAircraft(WingState state)
        {
            return (state.fighter != null && state.fighter.isAlive()) ||
                (state.bomber != null && state.bomber.isAlive());
        }

        private static bool HasEnemyNearby(Actor carrier)
        {
            return FindNearestEnemy(carrier) != null;
        }

        private static Actor FindNearestEnemy(Actor carrier)
        {
            if (carrier?.kingdom == null || carrier.current_tile == null)
                return null;

            Actor closest = null;
            float closestDistance = 55f;
            int chunkRadius = Mathf.Clamp(Mathf.CeilToInt(55f / 12f), 1, 5);
            foreach (Actor actor in Finder.getUnitsFromChunk(carrier.current_tile, chunkRadius, 55f, false))
            {
                if (actor == null || !actor.isAlive() || actor.current_tile == null || actor.kingdom == null ||
                    !carrier.kingdom.isEnemy(actor.kingdom))
                    continue;
                float distance = Toolbox.DistTile(carrier.current_tile, actor.current_tile);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closest = actor;
                }
            }
            return closest;
        }

        private static bool GetReplacementRequired(Actor carrier, string key)
        {
            carrier.data.get(key, out bool required, pDefault: false);
            return required;
        }

        private static void SetReplacementRequired(Actor carrier, string key, bool required)
        {
            if (carrier != null)
                carrier.data.set(key, required);
        }

        private static bool IsCarrier(Actor actor)
        {
            return actor != null && actor.isAlive() && actor.current_tile != null && actor.kingdom != null &&
                actor.asset?.id != null && actor.asset.id.StartsWith("CarrierVessel_", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetFaction(string carrierId)
        {
            if (carrierId != null && carrierId.EndsWith("_horde", StringComparison.OrdinalIgnoreCase)) return "Ork";
            if (carrierId != null && carrierId.EndsWith("_harden", StringComparison.OrdinalIgnoreCase)) return "Dwarf";
            if (carrierId != null && carrierId.EndsWith("_gaia", StringComparison.OrdinalIgnoreCase)) return "Gaia";
            return "Human";
        }
    }
}
