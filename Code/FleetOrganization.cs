using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using tools;
using UnityEngine;

namespace ModernBox
{
    /// <summary>
    /// Keeps warships assigned to the same dock moving toward one sensible
    /// naval objective.  It deliberately works with BehWarBoatFindTarget:
    /// ships still choose a reachable ocean tile through WorldBox and retain
    /// their normal movement, collision and combat behaviour.
    /// </summary>
    internal static class FleetOrganization
    {
        private const float FleetOrderMinimumSeconds = 20f;
        private const float FleetOrderMaximumSeconds = 30f;

        private sealed class FleetOrder
        {
            internal WorldTile target;
            internal Kingdom targetKingdom;
            internal float expiresAt;
        }

        // A home building is stable for the lifetime of its dock and is a
        // stronger grouping key than city/kingdom: two ports can operate as
        // two independent task groups during the same war.
        private static readonly Dictionary<Building, FleetOrder> OrdersByDock =
            new Dictionary<Building, FleetOrder>();
        private static float _nextCleanup;

        internal static void ApplySharedTarget(Actor boat)
        {
            if (!IsEligibleWarBoat(boat) || !boat.hasHomeBuilding())
                return;

            Building dock = boat.getHomeBuilding();
            if (dock == null || dock.isRekt() || dock.current_tile == null)
                return;

            if (Time.time >= _nextCleanup)
            {
                _nextCleanup = Time.time + 60f;
                CleanupExpiredOrders();
            }

            if (OrdersByDock.TryGetValue(dock, out FleetOrder order) && IsUsableOrder(boat, order))
            {
                // Map destruction can turn a formerly valid sea tile into an
                // invalid destination. Revalidate it from each ship before
                // handing it to the native pathfinder; otherwise one stale
                // fleet order can cause RegionPathFinder failures every tick.
                WorldTile reachableTarget = GetReachableSeaTarget(boat, order.target);
                if (IsModernSubmarine(boat))
                    reachableTarget = GetBufferedSubmarineTarget(boat, reachableTarget) ?? reachableTarget;
                if (reachableTarget != null)
                {
                    // This is just the destination used by the normal boat
                    // AI. No position is assigned, so ships keep natural
                    // navigation, collision and combat behaviour.
                    boat.beh_tile_target = reachableTarget;
                    return;
                }

                OrdersByDock.Remove(dock);
            }

            // BehWarBoatFindTarget has just selected a legal, reachable sea
            // tile for this ship.  Make that natural choice the dock's order.
            WorldTile naturalTarget = boat.beh_tile_target;
            if (!IsNavigableSeaTile(naturalTarget))
                return;

            // Keep the dock order itself unchanged for surface escorts and
            // carriers. A submarine receives a deeper reachable substitute
            // below when the native order is applied to that individual hull.
            if (IsModernSubmarine(boat))
            {
                WorldTile bufferedTarget = GetBufferedSubmarineTarget(boat, naturalTarget);
                if (bufferedTarget != null)
                    boat.beh_tile_target = bufferedTarget;
            }

            OrdersByDock[dock] = new FleetOrder
            {
                target = naturalTarget,
                targetKingdom = FindEnemyKingdomAt(naturalTarget, boat.kingdom) ?? FindFirstEnemyKingdom(boat.kingdom),
                expiresAt = Time.time + UnityEngine.Random.Range(FleetOrderMinimumSeconds, FleetOrderMaximumSeconds)
            };
        }

        private static bool IsEligibleWarBoat(Actor actor)
        {
            return actor != null && actor.isAlive() && actor.current_tile != null && actor.kingdom != null &&
                   actor.asset != null && (actor.asset.is_boat || actor.hasTrait("boat"));
        }

        private static bool IsUsableOrder(Actor boat, FleetOrder order)
        {
            if (order == null || order.target == null || Time.time >= order.expiresAt)
                return false;

            // A peace treaty or the destruction of the chosen enemy causes a
            // new native target selection at the next decision tick.
            return order.targetKingdom == null ||
                   (order.targetKingdom.cities != null && order.targetKingdom.cities.Count > 0 &&
                    boat.kingdom.isEnemy(order.targetKingdom));
        }

        private static Kingdom FindEnemyKingdomAt(WorldTile tile, Kingdom ownKingdom)
        {
            if (tile?.zone?.city?.kingdom != null && ownKingdom != null && ownKingdom.isEnemy(tile.zone.city.kingdom))
                return tile.zone.city.kingdom;
            return null;
        }

        private static Kingdom FindFirstEnemyKingdom(Kingdom ownKingdom)
        {
            if (ownKingdom == null || !ownKingdom.hasEnemies())
                return null;

            using (var enemies = ownKingdom.getEnemiesKingdoms())
            {
                foreach (Kingdom enemy in enemies)
                {
                    if (enemy != null)
                        return enemy;
                }
            }
            return null;
        }

        private static WorldTile GetReachableSeaTarget(Actor boat, WorldTile target)
        {
            if (boat?.current_tile == null || boat.current_tile.region == null || target == null ||
                target.region == null || !IsNavigableSeaTile(target))
                return null;

            // OceanHelper is the same WorldBox helper used by the base war
            // boat behaviour. It returns null instead of requesting a global
            // boat path to a broken/non-liquid target.
            WorldTile reachable = OceanHelper.findTileForBoat(boat.current_tile, target);
            return reachable != null && reachable.region != null && IsNavigableSeaTile(reachable)
                ? reachable
                : null;
        }

        private static bool IsNavigableSeaTile(WorldTile tile)
        {
            return tile?.Type != null && (tile.Type.ocean || tile.Type.liquid);
        }

        private static bool IsModernSubmarine(Actor boat)
        {
            return NavalRoles.IsAnyModernSubmarine(boat?.asset?.id);
        }

        private static WorldTile GetBufferedSubmarineTarget(Actor submarine, WorldTile requested)
        {
            if (submarine?.current_tile == null || requested == null || !IsNavigableSeaTile(requested))
                return null;
            if (HasWaterBuffer(requested, 2))
                return requested;

            int centerX = Mathf.RoundToInt(requested.pos.x);
            int centerY = Mathf.RoundToInt(requested.pos.y);
            // Search the closest open-water cell first. OceanHelper remains the
            // authority on reachability, so this cannot force a route through
            // land or teleport an existing submarine.
            for (int radius = 1; radius <= 8; radius++)
            {
                for (int offsetX = -radius; offsetX <= radius; offsetX++)
                {
                    for (int offsetY = -radius; offsetY <= radius; offsetY++)
                    {
                        if (Mathf.Abs(offsetX) != radius && Mathf.Abs(offsetY) != radius)
                            continue;
                        WorldTile candidate = World.world?.GetTile(centerX + offsetX, centerY + offsetY);
                        if (!HasWaterBuffer(candidate, 2))
                            continue;
                        WorldTile reachable = GetReachableSeaTarget(submarine, candidate);
                        if (reachable != null && HasWaterBuffer(reachable, 2))
                            return reachable;
                    }
                }
            }
            return null;
        }

        private static bool HasWaterBuffer(WorldTile tile, int radius)
        {
            if (!IsNavigableSeaTile(tile) || World.world == null)
                return false;

            int centerX = Mathf.RoundToInt(tile.pos.x);
            int centerY = Mathf.RoundToInt(tile.pos.y);
            for (int offsetX = -radius; offsetX <= radius; offsetX++)
            {
                for (int offsetY = -radius; offsetY <= radius; offsetY++)
                {
                    if (!IsNavigableSeaTile(World.world.GetTile(centerX + offsetX, centerY + offsetY)))
                        return false;
                }
            }
            return true;
        }

        private static void CleanupExpiredOrders()
        {
            if (OrdersByDock.Count == 0)
                return;

            List<Building> remove = null;
            foreach (KeyValuePair<Building, FleetOrder> entry in OrdersByDock)
            {
                if (entry.Key == null || entry.Key.isRekt() || entry.Value == null || Time.time >= entry.Value.expiresAt)
                {
                    if (remove == null)
                        remove = new List<Building>();
                    remove.Add(entry.Key);
                }
            }

            if (remove == null)
                return;

            foreach (Building dock in remove)
                OrdersByDock.Remove(dock);
        }
    }

    /// <summary>
    /// Periodic conventional naval warfare. The controller samples a small
    /// roster every 2.5-4.5 seconds rather than scanning per frame. It keeps
    /// anti-submarine attacks terrain-safe and gives existing missile submarines
    /// their normal feasible cadence when a kingdom has no fixed-wing support.
    /// </summary>
    internal sealed class AntiSubmarineWarfareController : MonoBehaviour
    {
        private const float MinimumInterval = 2.5f;
        private const float MaximumInterval = 4.5f;
        private const int DefendersPerCycle = 3;
        private const int MissilePlatformsPerCycle = 2;

        private readonly List<Actor> _defenders = new List<Actor>();
        private readonly List<Actor> _missilePlatforms = new List<Actor>();
        private readonly ConditionalWeakTable<Actor, Cooldown> _cooldowns =
            new ConditionalWeakTable<Actor, Cooldown>();
        private readonly ConditionalWeakTable<Actor, Cooldown> _missileCooldowns =
            new ConditionalWeakTable<Actor, Cooldown>();
        private float _nextCycle;
        private float _nextRosterRefresh;
        private int _cursor;
        private int _missileCursor;
        private bool _rosterInitialized;

        private sealed class Cooldown
        {
            internal float readyAt;
        }

        private void Awake()
        {
            ScheduleNextCycle();
        }

        private void Update()
        {
            if (Time.time < _nextCycle)
                return;

            ScheduleNextCycle();
            try
            {
                if (!_rosterInitialized || Time.time >= _nextRosterRefresh)
                    RefreshRoster();

                ProcessDefenderSlice();
                ProcessMissilePlatformSlice();
            }
            catch (Exception ex)
            {
                ModernBoxLogger.Error("[MX.ASW] Cycle failure: " + ex.Message);
            }
        }

        private void ScheduleNextCycle()
        {
            _nextCycle = Time.time + UnityEngine.Random.Range(MinimumInterval, MaximumInterval);
        }

        private void RefreshRoster()
        {
            _defenders.Clear();
            _missilePlatforms.Clear();
            _cursor = 0;
            _missileCursor = 0;
            _nextRosterRefresh = Time.time + 22f;
            _rosterInitialized = true;

            if (World.world?.units == null)
                return;

            foreach (Actor actor in World.world.units.Cast<Actor>())
            {
                if (IsAswDestroyer(actor))
                    _defenders.Add(actor);
                if (IsMissileNavalPlatform(actor))
                    _missilePlatforms.Add(actor);
            }
        }

        private void ProcessDefenderSlice()
        {
            if (_defenders.Count == 0)
                return;

            int count = Math.Min(DefendersPerCycle, _defenders.Count);
            for (int i = 0; i < count; i++)
            {
                if (_cursor >= _defenders.Count)
                    _cursor = 0;

                Actor defender = _defenders[_cursor++];
                TryEngage(defender);
            }
        }

        private void TryEngage(Actor defender)
        {
            if (!IsAswDestroyer(defender) || !IsReady(defender))
                return;

            bool heavyDestroyer = defender.asset.id.StartsWith("bDestroyer_", StringComparison.OrdinalIgnoreCase);
            float range = heavyDestroyer ? 38f : 28f;
            Actor target = FindNearestEnemySubmarine(defender, range);
            if (target == null)
                return;

            // The lighter destroyer has less reliable sonar; the heavier one
            // is a purpose-built escort.  Both retain a long enough cooldown
            // that anti-sub warfare is a response, not a projectile fountain.
            float detectionChance = heavyDestroyer ? 0.88f : 0.58f;
            PutOnCooldown(defender, heavyDestroyer ? 8.5f : 11.5f);
            if (UnityEngine.Random.value > detectionChance)
                return;

            if (!LaunchSafeTorpedo(defender, target))
            {
                // Fallback remains conventional direct hull damage.  It is
                // intentionally terrain-free if an older asset bundle lacks
                // the mod torpedo projectile.
                target.getHit(heavyDestroyer ? 120f : 72f, true, AttackType.Weapon, defender, true, false, true);
            }
        }

        private void ProcessMissilePlatformSlice()
        {
            if (_missilePlatforms.Count == 0)
                return;

            int count = Math.Min(MissilePlatformsPerCycle, _missilePlatforms.Count);
            for (int i = 0; i < count; i++)
            {
                if (_missileCursor >= _missilePlatforms.Count)
                    _missileCursor = 0;

                TryLaunchNoAirFallback(_missilePlatforms[_missileCursor++]);
            }
        }

        private void TryLaunchNoAirFallback(Actor platform)
        {
            if (!IsMissileNavalPlatform(platform) || IsMissileOnCooldown(platform) ||
                KingdomHasOperationalFixedWing(platform.kingdom))
                return;

            if (NavalRoles.TryLaunchNoAirFallback(platform))
            {
                float cadence = NavalRoles.GetNoAirFallbackCadence(platform.asset.id);
                PutMissileOnCooldown(platform, cadence);
            }
            else
            {
                // Sin blancos, oro o ruta válida no se reintenta cada ciclo.
                // La espera corta mantiene reactiva la flota sin hacer sondeos
                // repetidos sobre todos los reinos enemigos.
                PutMissileOnCooldown(platform, 8f);
            }
        }

        private static bool KingdomHasOperationalFixedWing(Kingdom kingdom)
        {
            if (kingdom == null || World.world?.units == null)
                return false;

            foreach (Actor unit in World.world.units)
            {
                if (unit == null || !unit.isAlive() || unit.kingdom != kingdom || !IsFixedWingAircraft(unit.asset?.id) ||
                    (Vehicles.IsFixedWingMissionAircraft(unit) && !Vehicles.IsAircraftInAttackWindow(unit)))
                    continue;
                return true;
            }
            return false;
        }

        private static bool IsFixedWingAircraft(string actorId)
        {
            return !string.IsNullOrEmpty(actorId) &&
                (actorId.StartsWith("FighterJet_", StringComparison.OrdinalIgnoreCase) ||
                 actorId.StartsWith("Bomber_", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(actorId, "F55FighterJet", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(actorId, "americanbomberww", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(actorId, "biplane", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(actorId, "fighterww", StringComparison.OrdinalIgnoreCase));
        }

        private static Actor FindNearestEnemySubmarine(Actor defender, float range)
        {
            if (defender?.current_tile == null || defender.kingdom == null)
                return null;

            int chunks = Mathf.Clamp(Mathf.CeilToInt(range / 12f), 1, 4);
            Actor nearest = null;
            float nearestDistance = float.MaxValue;
            foreach (Actor candidate in Finder.getUnitsFromChunk(defender.current_tile, chunks, range, false))
            {
                if (!IsEnemySubmarine(defender, candidate))
                    continue;

                float distance = Vector2.Distance(defender.current_position, candidate.current_position);
                if (distance < nearestDistance)
                {
                    nearest = candidate;
                    nearestDistance = distance;
                }
            }
            return nearest;
        }

        private static bool LaunchSafeTorpedo(Actor defender, Actor target)
        {
            if (defender == null || target == null || !target.isAlive() || defender.current_tile == null ||
                target.current_tile == null || World.world?.projectiles == null ||
                AssetManager.projectiles?.get("modernbox_torpedo") == null)
                return false;

            Vector3 origin = defender.current_position;
            Vector3 destination = target.current_position;
            float distance = Vector2.Distance(origin, destination);
            Vector3 vector = Toolbox.getNewPoint(origin.x, origin.y, destination.x, destination.y, distance);
            Vector3 start = Toolbox.getNewPoint(origin.x, origin.y, destination.x, destination.y, defender.stats["size"]);
            World.world.projectiles.spawn(defender, target, "modernbox_torpedo", start, vector);
            defender.punchTargetAnimation(vector, true, false, 45f);
            return true;
        }

        private bool IsReady(Actor defender)
        {
            return !_cooldowns.TryGetValue(defender, out Cooldown cooldown) || Time.time >= cooldown.readyAt;
        }

        private void PutOnCooldown(Actor defender, float seconds)
        {
            Cooldown cooldown = _cooldowns.GetOrCreateValue(defender);
            cooldown.readyAt = Time.time + seconds;
        }

        private bool IsMissileOnCooldown(Actor platform)
        {
            return _missileCooldowns.TryGetValue(platform, out Cooldown cooldown) && Time.time < cooldown.readyAt;
        }

        private void PutMissileOnCooldown(Actor platform, float seconds)
        {
            Cooldown cooldown = _missileCooldowns.GetOrCreateValue(platform);
            cooldown.readyAt = Time.time + seconds;
        }

        private static bool IsAswDestroyer(Actor actor)
        {
            if (actor == null || !actor.isAlive() || actor.asset == null || actor.current_tile == null || actor.kingdom == null)
                return false;

            string id = actor.asset.id;
            return id.StartsWith("aDestroyer_", StringComparison.OrdinalIgnoreCase) ||
                   id.StartsWith("bDestroyer_", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsMissileNavalPlatform(Actor actor)
        {
            return actor != null && actor.isAlive() && actor.asset != null && actor.kingdom != null &&
                   NavalRoles.IsAnyModernSubmarine(actor.asset.id) &&
                   !NavalRoles.IsInterceptorSubmarine(actor.asset.id);
        }

        private static bool IsEnemySubmarine(Actor defender, Actor candidate)
        {
            if (candidate == null || candidate == defender || !candidate.isAlive() || candidate.asset == null ||
                candidate.kingdom == null || !defender.kingdom.isEnemy(candidate.kingdom))
                return false;

            string id = candidate.asset.id;
            return id.IndexOf("submarine", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   id.IndexOf("ssbn", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    // Runs after the stock behaviour has selected a reachable ocean tile.
    // The patch is passive until a boat has a home dock and does not replace
    // the base method, so all existing warboats keep their original logic.
    [HarmonyPatch(typeof(BehWarBoatFindTarget), nameof(BehWarBoatFindTarget.execute))]
    internal static class FleetOrganizationTargetPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Actor pActor)
        {
            FleetOrganization.ApplySharedTarget(pActor);
        }
    }
}
