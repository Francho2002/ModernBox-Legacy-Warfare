using System;
using System.Collections.Generic;
using UnityEngine;

namespace ModernBox
{
    /// <summary>
    /// Shared targeting doctrine for offensive missiles.  Target discovery is
    /// deliberately amortized: launch decisions only filter this five-second
    /// snapshot by their own range, safety and launcher-terrain rules.
    /// </summary>
    internal static class MissileDoctrine
    {
        private const float CacheLifetimeSeconds = 5f;

        // This hook intentionally defaults to permissive. Diplomacy can later
        // deny civilian fallback (for example during a ceasefire) without
        // duplicating target classification in every launcher.
        internal static Func<Actor, bool> CivilianFallbackAuthorization;

        private sealed class CachedTarget
        {
            internal Actor actor;
            internal Building building;
            internal City city;
            internal Vector2 position;
            internal int priority;
        }

        private static readonly List<CachedTarget> MilitaryTargets = new List<CachedTarget>();
        private static readonly List<CachedTarget> CivilianTargets = new List<CachedTarget>();
        private static float _nextRefreshAt = -1f;
        private static MapBox _cachedWorld;
        private static object _cachedMapStats;
        private static int _cacheEpoch;

        internal static bool TrySelectTarget(Actor caster, float blastSafetyRadius, out Vector2 target)
        {
            List<Vector2> candidates = new List<Vector2>();
            GetTargetCandidates(caster, blastSafetyRadius, candidates, 1);
            if (candidates.Count == 0)
            {
                target = default(Vector2);
                return false;
            }

            target = candidates[0];
            return true;
        }

        /// <summary>
        /// Adds every reachable military tier in priority order. If any live
        /// enemy military target exists, civilians are never considered, even
        /// when every military point is out of range or unsafe for this launcher.
        /// </summary>
        internal static void GetTargetCandidates(Actor caster, float blastSafetyRadius, List<Vector2> results,
            int maximumResults = int.MaxValue)
        {
            if (results == null)
                return;
            results.Clear();
            if (maximumResults <= 0 || caster == null || caster.kingdom == null)
                return;

            EnsureCache();
            bool hasLiveMilitaryEnemy = HasLiveEnemyTarget(caster.kingdom, MilitaryTargets);
            List<CachedTarget> source = hasLiveMilitaryEnemy ? MilitaryTargets : CivilianTargets;

            if (!hasLiveMilitaryEnemy && !IsCivilianFallbackAuthorized(caster))
                return;

            HashSet<Vector2> unique = new HashSet<Vector2>();
            if (hasLiveMilitaryEnemy)
            {
                for (int priority = 1; priority <= 3; priority++)
                {
                    AddLaunchableTargets(caster, source, priority, blastSafetyRadius, unique, results, maximumResults);
                    if (results.Count >= maximumResults)
                        return;
                }
                return;
            }

            AddLaunchableTargets(caster, source, 0, blastSafetyRadius, unique, results, maximumResults);
        }

        internal static bool IsLiveEnemyMilitaryTarget(Actor caster, BaseSimObject target)
        {
            if (caster?.kingdom == null || target == null)
                return false;

            Actor actor = target.isActor() ? target.a : null;
            if (actor != null)
                return IsLiveEnemyMilitaryActor(caster.kingdom, actor);

            return IsLiveEnemyMilitaryBuilding(caster.kingdom, target as Building);
        }

        internal static bool HasLiveEnemyMilitaryTargetNear(Actor caster, Vector2 target, float radius)
        {
            if (caster?.kingdom == null)
                return false;

            EnsureCache();
            foreach (CachedTarget candidate in MilitaryTargets)
            {
                if (TryGetLiveEnemyPosition(caster.kingdom, candidate, out Vector2 position) &&
                    Vector2.Distance(position, target) <= radius)
                    return true;
            }
            return false;
        }

        private static bool IsCivilianFallbackAuthorized(Actor caster)
        {
            return CivilianFallbackAuthorization == null || CivilianFallbackAuthorization(caster);
        }

        private static void AddLaunchableTargets(Actor caster, List<CachedTarget> source, int priority,
            float blastSafetyRadius, HashSet<Vector2> unique, List<Vector2> results, int maximumResults)
        {
            if (source.Count == 0 || results.Count >= maximumResults)
                return;

            int start = (int)((uint)(caster.getID().GetHashCode() + _cacheEpoch) % (uint)source.Count);
            for (int index = 0; index < source.Count; index++)
            {
                CachedTarget candidate = source[(start + index) % source.Count];
                if (candidate.priority != priority ||
                    !TryGetLiveEnemyPosition(caster.kingdom, candidate, out Vector2 position) ||
                    !Vehicles.IsMissileDoctrineTargetLaunchable(caster, position, blastSafetyRadius) ||
                    !unique.Add(position))
                    continue;

                results.Add(position);
                if (results.Count >= maximumResults)
                    return;
            }
        }

        private static bool HasLiveEnemyTarget(Kingdom casterKingdom, List<CachedTarget> targets)
        {
            foreach (CachedTarget candidate in targets)
            {
                if (TryGetLiveEnemyPosition(casterKingdom, candidate, out Vector2 ignored))
                    return true;
            }
            return false;
        }

        private static bool TryGetLiveEnemyPosition(Kingdom casterKingdom, CachedTarget candidate, out Vector2 position)
        {
            position = default(Vector2);
            if (casterKingdom == null || candidate == null)
                return false;

            Kingdom owner;
            if (candidate.actor != null)
            {
                if (!candidate.actor.isAlive())
                    return false;
                owner = candidate.actor.kingdom;
                position = candidate.actor.current_position;
            }
            else if (candidate.building != null)
            {
                if (!candidate.building.isAlive())
                    return false;
                owner = candidate.building.kingdom;
                WorldTile tile = candidate.building.current_tile;
                position = tile != null ? tile.pos : candidate.building.current_position;
            }
            else if (candidate.city != null)
            {
                if (!candidate.city.isAlive())
                    return false;
                owner = candidate.city.kingdom;
                WorldTile tile = candidate.city.getTile();
                position = tile != null ? tile.pos : candidate.position;
            }
            else
            {
                return false;
            }

            return owner != null && casterKingdom.isEnemy(owner);
        }

        private static bool IsLiveEnemyMilitaryActor(Kingdom casterKingdom, Actor actor)
        {
            return actor != null && actor.isAlive() && actor.kingdom != null &&
                casterKingdom.isEnemy(actor.kingdom) && GetActorPriority(actor, null) > 0;
        }

        private static bool IsLiveEnemyMilitaryBuilding(Kingdom casterKingdom, Building building)
        {
            return building != null && building.isAlive() && building.kingdom != null &&
                casterKingdom.isEnemy(building.kingdom) && IsMilitaryBuilding(building);
        }

        private static void EnsureCache()
        {
            MapBox world = World.world;
            object mapStats = world?.map_stats;
            if (!object.ReferenceEquals(world, _cachedWorld) || !object.ReferenceEquals(mapStats, _cachedMapStats))
            {
                _cachedWorld = world;
                _cachedMapStats = mapStats;
                _nextRefreshAt = -1f;
                MilitaryTargets.Clear();
                CivilianTargets.Clear();
            }

            if (Time.time < _nextRefreshAt)
                return;

            _nextRefreshAt = Time.time + CacheLifetimeSeconds;
            _cacheEpoch++;
            MilitaryTargets.Clear();
            CivilianTargets.Clear();
            if (world == null)
                return;

            HashSet<Actor> civilianLeaders = new HashSet<Actor>();
            if (world.kingdoms != null)
            {
                foreach (Kingdom kingdom in world.kingdoms)
                {
                    if (kingdom?.king != null)
                        civilianLeaders.Add(kingdom.king);
                }
            }

            if (world.cities?.list != null)
            {
                foreach (City city in world.cities.list)
                {
                    if (city == null || !city.isAlive())
                        continue;

                    if (city.leader != null)
                        civilianLeaders.Add(city.leader);
                    WorldTile cityTile = city.getTile();
                    CivilianTargets.Add(new CachedTarget
                    {
                        city = city,
                        position = cityTile == null ? default(Vector2) : cityTile.pos
                    });

                    if (city.buildings == null)
                        continue;
                    foreach (Building building in city.buildings)
                    {
                        if (building == null || !building.isAlive())
                            continue;
                        CachedTarget target = new CachedTarget { building = building };
                        if (IsMilitaryBuilding(building))
                        {
                            target.priority = 2;
                            MilitaryTargets.Add(target);
                        }
                        else
                        {
                            CivilianTargets.Add(target);
                        }
                    }
                }
            }

            if (world.units == null)
                return;
            foreach (Actor actor in world.units)
            {
                if (actor == null || !actor.isAlive())
                    continue;

                int priority = GetActorPriority(actor, civilianLeaders);
                CachedTarget target = new CachedTarget { actor = actor, priority = priority };
                if (priority > 0)
                    MilitaryTargets.Add(target);
                else if (civilianLeaders.Contains(actor))
                    CivilianTargets.Add(target);
            }
        }

        private static int GetActorPriority(Actor actor, HashSet<Actor> civilianLeaders)
        {
            if (actor == null || actor.kingdom == null ||
                (civilianLeaders != null && civilianLeaders.Contains(actor)) ||
                actor == actor.kingdom.king || actor == actor.city?.leader)
                return 0;

            string id = actor.asset?.id;
            if (IsPriorityOneMilitaryVehicle(id))
                return 1;
            return actor.isWarrior() ? 3 : 0;
        }

        private static bool IsPriorityOneMilitaryVehicle(string id)
        {
            if (string.IsNullOrEmpty(id))
                return false;

            return id.StartsWith("MissileSystem_", StringComparison.OrdinalIgnoreCase) ||
                NavalRoles.IsAnyModernSubmarine(id) ||
                id.StartsWith("aDestroyer_", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith("bDestroyer_", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith("CarrierVessel_", StringComparison.OrdinalIgnoreCase) ||
                ModernCapPolicy.IsAllowedAircraft(id) ||
                ModernCapPolicy.IsArtillery(id) ||
                id.StartsWith("Tank_", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith("wheeledtank_", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith("supporttruck_", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith("modernhumvee_", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "Humvee", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "AbramTank", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "shermanww", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "tankie", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "genericwwtank", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "landship", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "bigtankww", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "wwsupporttruck", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsMilitaryBuilding(Building building)
        {
            BuildingAsset asset = building?.asset;
            string signature = string.Concat(asset?.id ?? string.Empty, "|", asset?.type ?? string.Empty,
                "|", asset?.group ?? string.Empty).ToLowerInvariant();
            return signature.Contains("dock") || signature.Contains("harbor") || signature.Contains("harbour") ||
                signature.Contains("port") || signature.Contains("barracks") || signature.Contains("tower") ||
                signature.Contains("windmill") || signature.Contains("mill") || signature.Contains("arsenal") ||
                signature.Contains("armory") || signature.Contains("armoury") || signature.Contains("supply") ||
                signature.Contains("depot") || signature.Contains("warehouse") || signature.Contains("factory") ||
                signature.Contains("workshop") || signature.Contains("forge") || signature.Contains("smith") ||
                signature.Contains("mine") || signature.Contains("quarry");
        }
    }
}
