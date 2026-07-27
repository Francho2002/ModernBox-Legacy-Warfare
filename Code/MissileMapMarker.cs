using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ModernBox
{
    /// <summary>
    /// Adds a second QuantumSprite only while WorldBox renders the overview.
    /// Projectiles are ECS objects in build 719, so copying their Unity renderer
    /// is not possible; drawing their already-loaded projectile frame here keeps
    /// the marker identical to the real missile without changing its physics.
    /// </summary>
    internal static class MissileMapMarker
    {
        private static readonly HashSet<string> ConventionalProjectiles =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "missileartillery",
                "fireboneartillery",
                "frostmissileartillery",
                "plantmissileartillery",
                "modernbox_torpedo"
            };

        internal static void DrawOverviewMarkers(QuantumSpriteAsset spriteAsset)
        {
            // This render pass knows whether the map is currently being drawn.
            // Checking here (rather than during Projectile.update) prevents a
            // marker from appearing large in normal gameplay view.
            if (!MapBox.isRenderMiniMap() || spriteAsset?.group_system == null)
                return;

            List<Projectile> projectiles = World.world?.projectiles?.list;
            if (projectiles == null)
                return;

            foreach (Projectile projectile in projectiles)
            {
                if (!TryGetMarkerScale(projectile, out float markerScale))
                    continue;

                ProjectileAsset asset = projectile.asset;
                Sprite sprite = GetProjectileSprite(projectile, asset);
                if (sprite == null)
                    continue;

                QuantumSprite marker = spriteAsset.group_system.getNext();
                if (marker == null)
                    continue;

                Vector3 position = projectile.getTransformedPositionWithHeight();
                position.z = projectile.getCurrentHeight();
                marker.setSprite(sprite);
                marker.set(ref position,
                    Mathf.Max(0.05f, projectile.getCurrentScale() * markerScale));
                marker.transform.rotation = projectile.rotation;

                Color color = new Color(1f, 1f, 1f, projectile.getAlpha());
                marker.setColor(ref color);
            }
        }

        private static Sprite GetProjectileSprite(Projectile projectile, ProjectileAsset asset)
        {
            if (projectile == null || asset?.frames == null || asset.frames.Length == 0)
                return null;

            if (asset.animated)
                return AnimationHelper.getSpriteFromList(
                    projectile.GetHashCode(), asset.frames, asset.animation_speed);
            return asset.frames[0];
        }

        private static bool TryGetMarkerScale(Projectile projectile, out float markerScale)
        {
            markerScale = 0f;
            string projectileId = projectile?.asset?.id;
            if (string.IsNullOrEmpty(projectileId))
                return false;

            if (ConventionalProjectiles.Contains(projectileId))
            {
                markerScale = 3.0f;
                return true;
            }

            if (string.Equals(projectileId, "NUKER", StringComparison.Ordinal))
            {
                markerScale = 3.3f;
                return true;
            }

            if (string.Equals(projectileId, "SSBN_CZAR_WARHEAD", StringComparison.Ordinal))
            {
                markerScale = 3.8f;
                return true;
            }

            if (string.Equals(projectileId, "modernbox_hammer_warhead", StringComparison.Ordinal))
            {
                markerScale = 3.6f;
                return true;
            }

            if (NavalRoles.IsHeavyWarhead(projectileId))
            {
                markerScale = 3.3f;
                return true;
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(QuantumSpriteLibrary), "drawProjectiles")]
    internal static class MissileMapMarkerRenderPatch
    {
        [HarmonyPostfix]
        private static void Postfix(QuantumSpriteAsset pAsset)
        {
            MissileMapMarker.DrawOverviewMarkers(pAsset);
        }
    }
}
