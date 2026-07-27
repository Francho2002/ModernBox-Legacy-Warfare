using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ModernBox
{
    /// <summary>
    /// Adds a deliberately sparse, superficial fallout mark after military
    /// nuclear impacts.  The native nuclear damage remains responsible for the
    /// blast itself; this layer only changes a few eligible surface tiles and
    /// never creates craters, water conversion, fires, or building damage.
    /// </summary>
    internal static class NuclearFallout
    {
        private sealed class FalloutProfile
        {
            internal readonly int MinimumTiles;
            internal readonly int MaximumTiles;
            internal readonly int Radius;

            internal FalloutProfile(int minimumTiles, int maximumTiles, int radius)
            {
                MinimumTiles = minimumTiles;
                MaximumTiles = maximumTiles;
                Radius = radius;
            }
        }

        private sealed class PendingImpact
        {
            internal WorldTile Tile;
            internal FalloutProfile Profile;
        }

        // The baseline remains visibly affected without repainting a city.
        private static readonly FalloutProfile Light = new FalloutProfile(2, 3, 4);
        private static readonly FalloutProfile Medium = new FalloutProfile(3, 4, 5);
        // The exceptional Martillo and Apocalipsis have a slightly wider
        // residue, still capped at five superficial tiles.
        private static readonly FalloutProfile Heavy = new FalloutProfile(4, 5, 6);
        private static readonly ConditionalWeakTable<Projectile, PendingImpact> PendingImpacts =
            new ConditionalWeakTable<Projectile, PendingImpact>();

        internal static void RememberImpact(Projectile projectile)
        {
            if (projectile == null)
                return;

            // Projectile instances are pooled. A reused conventional projectile
            // must never inherit a prior nuclear impact state.
            PendingImpacts.Remove(projectile);
            if (!TryGetProfile(projectile.asset?.id, out FalloutProfile profile))
                return;

            WorldTile tile = projectile.getCurrentTilePosition();
            if (tile == null)
                return;

            PendingImpacts.Add(projectile, new PendingImpact
            {
                Tile = tile,
                Profile = profile
            });
        }

        internal static void ApplyRememberedImpact(Projectile projectile)
        {
            if (projectile == null || !PendingImpacts.TryGetValue(projectile, out PendingImpact impact))
                return;

            PendingImpacts.Remove(projectile);
            try
            {
                Apply(impact.Tile, impact.Profile);
            }
            catch (Exception ex)
            {
                // Fallout is cosmetic and must never destabilize the impact
                // path if a tile is removed or rebuilt by the base explosion.
                ModernBoxLogger.Warning("[NuclearFallout] Could not apply light fallout: " + ex.Message);
            }
        }

        private static bool TryGetProfile(string projectileId, out FalloutProfile profile)
        {
            profile = null;
            if (string.IsNullOrEmpty(projectileId))
                return false;

            if (string.Equals(projectileId, "NUKER", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(projectileId, "modernbox_baseline_ssbn_warhead", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(projectileId, "modernbox_neutron_warhead", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(projectileId, "modernbox_ruin_warhead", StringComparison.OrdinalIgnoreCase))
            {
                profile = Light;
                return true;
            }

            if (string.Equals(projectileId, "modernbox_trident_warhead", StringComparison.OrdinalIgnoreCase))
            {
                profile = Medium;
                return true;
            }

            if (string.Equals(projectileId, "SSBN_CZAR_WARHEAD", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(projectileId, "modernbox_hammer_warhead", StringComparison.OrdinalIgnoreCase))
            {
                profile = Heavy;
                return true;
            }

            // EMP, Arsenal, torpedoes, and conventional missiles intentionally
            // have no fallout profile.
            return false;
        }

        private static void Apply(WorldTile impactTile, FalloutProfile profile)
        {
            if (impactTile == null || profile == null || World.world == null)
                return;

            int centerX = Mathf.RoundToInt(impactTile.pos.x);
            int centerY = Mathf.RoundToInt(impactTile.pos.y);
            int radiusSquared = profile.Radius * profile.Radius;
            List<WorldTile> candidates = new List<WorldTile>();

            for (int y = -profile.Radius; y <= profile.Radius; y++)
            {
                for (int x = -profile.Radius; x <= profile.Radius; x++)
                {
                    int distanceSquared = (x * x) + (y * y);
                    if (distanceSquared > radiusSquared)
                        continue;

                    WorldTile tile = World.world.GetTile(centerX + x, centerY + y);
                    if (IsEligibleSurface(tile))
                        candidates.Add(tile);
                }
            }

            int marks = Mathf.Min(UnityEngine.Random.Range(profile.MinimumTiles, profile.MaximumTiles + 1), candidates.Count);
            for (int i = 0; i < marks; i++)
            {
                int index = UnityEngine.Random.Range(0, candidates.Count);
                WorldTile tile = candidates[index];
                candidates.RemoveAt(index);

                // This is the stock superficial wasteland transformation. It
                // does not excavate or replace the main tile, so the normal
                // nuclear blast stays terrain-safe.
                MapAction.checkAcidTerraform(tile);
            }
        }

        private static bool IsEligibleSurface(WorldTile tile)
        {
            if (tile == null || tile.building != null || tile.Type == null ||
                !tile.Type.ground || tile.Type.ocean || tile.Type.lava || tile.Type.block)
                return false;

            TopTileType top = tile.top_type;
            return top == null || (!top.wasteland && !top.ocean && !top.lava && !top.block);
        }
    }

    [HarmonyPatch(typeof(Projectile), "targetReached")]
    internal static class NuclearFalloutProjectilePatch
    {
        [HarmonyPrefix]
        private static void Prefix(Projectile __instance)
        {
            NuclearFallout.RememberImpact(__instance);
        }

        [HarmonyPostfix]
        private static void Postfix(Projectile __instance)
        {
            NuclearFallout.ApplyRememberedImpact(__instance);
        }
    }
}
