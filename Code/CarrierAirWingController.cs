using System;
using System.Collections.Generic;
using System.Linq;
using tools;
using UnityEngine;

namespace ModernBox
{
    // Carrier air operations are deliberately sampled instead of updated every
    // frame. A carrier owns at most two fighters and two bombers at a time.
    internal sealed class CarrierAirWingController : MonoBehaviour
    {
        private const float CycleSeconds = 4f;
        private const float RosterSeconds = 24f;
        private const float SortieSeconds = 52f;
        private const float RefitSeconds = 80f;
        private const float LostCarrierRtbSeconds = 48f;
        private const int CarriersPerCycle = 3;
        private const int AircraftPerRole = 2;
        private const string FighterReplacementRequiredKey = "mb_carrier_fighter_replacement_required";
        private const string BomberReplacementRequiredKey = "mb_carrier_bomber_replacement_required";
        private const string FighterLossCountKey = "mb_carrier_fighter_loss_count";
        private const string BomberLossCountKey = "mb_carrier_bomber_loss_count";

        private sealed class WingState
        {
            internal readonly List<Actor> fighters = new List<Actor>();
            internal readonly List<Actor> bombers = new List<Actor>();
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

            // Actor data survives a save/load. Rebuild the lightweight runtime
            // links from every marked aircraft so old references cannot cause
            // duplicate entries after a roster refresh.
            Dictionary<Actor, Actor> previousOwners = new Dictionary<Actor, Actor>();
            HashSet<Actor> recordedDestroyedAircraft = new HashSet<Actor>();
            foreach (KeyValuePair<Actor, WingState> wing in _wings)
            {
                RecordDestroyedAircraft(wing.Key, wing.Value.fighters, true, recordedDestroyedAircraft);
                RecordDestroyedAircraft(wing.Key, wing.Value.bombers, false, recordedDestroyedAircraft);
                foreach (Actor aircraft in wing.Value.fighters)
                    if (aircraft != null)
                        previousOwners[aircraft] = wing.Key;
                foreach (Actor aircraft in wing.Value.bombers)
                    if (aircraft != null)
                        previousOwners[aircraft] = wing.Key;
                wing.Value.fighters.Clear();
                wing.Value.bombers.Clear();
            }

            HashSet<Actor> rehydratedAircraft = new HashSet<Actor>();
            foreach (Actor actor in World.world.units.Cast<Actor>())
            {
                if (!Vehicles.IsCarrierAircraft(actor))
                    continue;
                if (!actor.isAlive())
                {
                    if (recordedDestroyedAircraft.Add(actor) &&
                        Vehicles.TryGetCarrierForAircraft(actor, out Actor destroyedAircraftCarrier))
                        RegisterLossForAircraft(destroyedAircraftCarrier, actor);
                    continue;
                }
                if (!Vehicles.TryGetCarrierForAircraft(actor, out Actor carrier))
                {
                    BeginTerrestrialFallback(actor);
                    if (previousOwners.TryGetValue(actor, out Actor previousCarrier) &&
                        !IsCarrier(previousCarrier) && _wings.TryGetValue(previousCarrier, out WingState lostState))
                        AddAircraftToRole(lostState, actor);
                    continue;
                }
                if (!_wings.TryGetValue(carrier, out WingState state))
                {
                    state = new WingState { sortieEndsAt = Time.time + 24f };
                    _wings[carrier] = state;
                }
                if (!rehydratedAircraft.Add(actor))
                    continue;
                AddAircraftToRole(state, actor);
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
            int fighterLosses = GetLossCount(carrier, FighterLossCountKey, FighterReplacementRequiredKey);
            int bomberLosses = GetLossCount(carrier, BomberLossCountKey, BomberReplacementRequiredKey);
            ConstructionCost replacementCost = new ConstructionCost(
                fighterLosses * 5 + bomberLosses * 7,
                fighterLosses * 4 + bomberLosses * 6,
                fighterLosses * 3 + bomberLosses * 5,
                fighterLosses + bomberLosses * 2);
            // The carrier hull includes its initial wing. Only aircraft that
            // were actually lost consume city resources on replacement.
            if ((fighterLosses > 0 || bomberLosses > 0) && !city.hasEnoughResourcesFor(replacementCost))
                return false;

            List<Actor> deployedFighters = new List<Actor>(AircraftPerRole);
            List<Actor> deployedBombers = new List<Actor>(AircraftPerRole);
            for (int index = 0; index < AircraftPerRole; index++)
            {
                Actor fighter = SpawnAircraft(fighterId, carrier);
                if (fighter == null)
                {
                    RollbackDeployment(deployedFighters, deployedBombers);
                    return false;
                }
                deployedFighters.Add(fighter);
            }
            for (int index = 0; index < AircraftPerRole; index++)
            {
                Actor bomber = SpawnAircraft(bomberId, carrier);
                if (bomber == null)
                {
                    RollbackDeployment(deployedFighters, deployedBombers);
                    return false;
                }
                deployedBombers.Add(bomber);
            }

            if (fighterLosses > 0 || bomberLosses > 0)
                city.spendResourcesForBuildingAsset(replacementCost);
            SetLossCount(carrier, FighterLossCountKey, 0);
            SetLossCount(carrier, BomberLossCountKey, 0);
            state.fighters.AddRange(deployedFighters);
            state.bombers.AddRange(deployedBombers);
            foreach (Actor fighter in deployedFighters)
                AssignEnemyTarget(carrier, fighter);
            foreach (Actor bomber in deployedBombers)
                AssignEnemyTarget(carrier, bomber);
            return true;
        }

        private static void RollbackDeployment(List<Actor> fighters, List<Actor> bombers)
        {
            foreach (Actor aircraft in fighters.Concat(bombers))
            {
                Vehicles.UnlinkCarrierAircraft(aircraft);
                ActionLibrary.removeUnit(aircraft);
            }
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
            foreach (Actor aircraft in state.fighters)
                Vehicles.ForceCarrierAircraftRtb(aircraft);
            foreach (Actor aircraft in state.bombers)
                Vehicles.ForceCarrierAircraftRtb(aircraft);
        }

        private static void RecoverAircraftOnDeck(Actor carrier, WingState state)
        {
            RecoverAircraft(carrier, state, state.fighters, carrier.current_tile, true);
            RecoverAircraft(carrier, state, state.bombers, carrier.current_tile, false);
        }

        private static void RecoverAircraft(Actor carrier, WingState state, List<Actor> aircraft, WorldTile deck, bool fighter)
        {
            for (int index = aircraft.Count - 1; index >= 0; index--)
            {
                Actor unit = aircraft[index];
                if (unit == null)
                {
                    aircraft.RemoveAt(index);
                    continue;
                }
                if (!unit.isAlive())
                {
                    RegisterLoss(carrier, fighter);
                    aircraft.RemoveAt(index);
                    continue;
                }
                if (Vehicles.TryConsumeCarrierRecoveryReady(unit, carrier))
                {
                    Vehicles.UnlinkCarrierAircraft(unit);
                    ActionLibrary.removeUnit(unit);
                    aircraft.RemoveAt(index);
                    continue;
                }
                if (state.sortieEndsAt <= 0f && unit.current_tile != null &&
                    Toolbox.DistTile(unit.current_tile, deck) <= 3)
                {
                    Vehicles.UnlinkCarrierAircraft(unit);
                    ActionLibrary.removeUnit(unit);
                    aircraft.RemoveAt(index);
                }
            }
        }

        private static void HandleLostCarrierWing(WingState state)
        {
            if (state.carrierLostAt <= 0f)
            {
                state.carrierLostAt = Time.time;
                foreach (Actor aircraft in state.fighters)
                    BeginTerrestrialFallback(aircraft);
                foreach (Actor aircraft in state.bombers)
                    BeginTerrestrialFallback(aircraft);
            }
            if (Time.time < state.carrierLostAt + LostCarrierRtbSeconds)
                return;
            RetireLostAircraft(state.fighters);
            RetireLostAircraft(state.bombers);
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

        private static void RetireLostAircraft(List<Actor> aircraft)
        {
            foreach (Actor unit in aircraft)
            {
                if (unit != null && unit.isAlive())
                    ActionLibrary.removeUnit(unit);
            }
            aircraft.Clear();
        }

        private static bool HasLiveAircraft(WingState state)
        {
            return state.fighters.Any(aircraft => aircraft != null && aircraft.isAlive()) ||
                state.bombers.Any(aircraft => aircraft != null && aircraft.isAlive());
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

        private static void AddAircraftToRole(WingState state, Actor aircraft)
        {
            if (aircraft.asset?.id.StartsWith("FighterJet_", StringComparison.OrdinalIgnoreCase) == true)
            {
                if (!state.fighters.Contains(aircraft))
                    state.fighters.Add(aircraft);
            }
            else if (aircraft.asset?.id.StartsWith("Bomber_", StringComparison.OrdinalIgnoreCase) == true &&
                !state.bombers.Contains(aircraft))
            {
                state.bombers.Add(aircraft);
            }
        }

        private static void RecordDestroyedAircraft(Actor carrier, List<Actor> aircraft, bool fighter,
            HashSet<Actor> recordedDestroyedAircraft)
        {
            foreach (Actor unit in aircraft)
            {
                if (unit != null && !unit.isAlive() && recordedDestroyedAircraft.Add(unit))
                    RegisterLoss(carrier, fighter);
            }
        }

        private static void RegisterLossForAircraft(Actor carrier, Actor aircraft)
        {
            if (aircraft.asset?.id.StartsWith("FighterJet_", StringComparison.OrdinalIgnoreCase) == true)
                RegisterLoss(carrier, true);
            else if (aircraft.asset?.id.StartsWith("Bomber_", StringComparison.OrdinalIgnoreCase) == true)
                RegisterLoss(carrier, false);
        }

        private static void RegisterLoss(Actor carrier, bool fighter)
        {
            string lossCountKey = fighter ? FighterLossCountKey : BomberLossCountKey;
            string legacyKey = fighter ? FighterReplacementRequiredKey : BomberReplacementRequiredKey;
            SetLossCount(carrier, lossCountKey, GetLossCount(carrier, lossCountKey, legacyKey) + 1);
        }

        private static int GetLossCount(Actor carrier, string lossCountKey, string legacyKey)
        {
            carrier.data.get(lossCountKey, out int losses, pDefault: 0);
            losses = Mathf.Clamp(losses, 0, AircraftPerRole);
            if (GetReplacementRequired(carrier, legacyKey))
            {
                losses = Mathf.Max(losses, 1);
                SetReplacementRequired(carrier, legacyKey, false);
            }
            SetLossCount(carrier, lossCountKey, losses);
            return losses;
        }

        private static void SetLossCount(Actor carrier, string key, int losses)
        {
            if (carrier != null)
                carrier.data.set(key, Mathf.Clamp(losses, 0, AircraftPerRole));
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
