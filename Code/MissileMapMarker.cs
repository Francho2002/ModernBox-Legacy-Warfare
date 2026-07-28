using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ModernBox
{
    /// <summary>
    /// Draws a full missile sprite into the aerial-map boat pass. Projectiles use
    /// a gameplay-only QuantumSpriteAsset in build 719, so patching
    /// drawProjectiles cannot ever draw into the aerial map. Reusing the loaded
    /// projectile frame here changes no projectile physics, collision or scale.
    /// </summary>
    internal static class MissileMapMarker
    {
        // The map pass can run every frame. Keep this bounded during saturation
        // launches without allocating per-projectile marker state.
        private const int MaximumOverviewMarkers = 192;

        internal static void DrawOverviewMarkers(QuantumSpriteAsset spriteAsset)
        {
            // drawBoatIcons is called for boats_small and boats_big. Draw only in
            // boats_big so a missile receives exactly one aerial marker.
            if (spriteAsset?.group_system == null ||
                !string.Equals(spriteAsset.id, "boats_big", StringComparison.Ordinal))
                return;

            if (World.world == null)
                return;

            List<Projectile> projectiles = World.world.projectiles?.list;
            if (projectiles == null)
                return;

            int drawn = 0;
            foreach (Projectile projectile in projectiles)
            {
                if (drawn >= MaximumOverviewMarkers)
                    break;
                if (!TryGetMarkerScale(projectile, out float markerScale))
                    continue;

                ProjectileAsset asset = projectile.asset;
                Sprite sprite = GetProjectileSprite(projectile, asset);
                if (sprite == null)
                    continue;

                QuantumSprite marker = spriteAsset.group_system.getNext();
                if (marker == null)
                    break;

                Vector2 transformedPosition = projectile.getTransformedPositionWithHeight();
                Vector3 position = new Vector3(transformedPosition.x, transformedPosition.y, 0f);
                marker.setSprite(sprite);
                // Constant map-space scale: the actual projectile stays at its
                // normal size. Marker scales are deliberately below 1 because
                // the source sprites are 38-83 pixels wide; multiplying them
                // made the overview icon larger than whole cities.
                marker.set(ref position, markerScale);
                marker.transform.rotation = projectile.rotation;

                Color color = new Color(1f, 1f, 1f, 1f);
                marker.setColor(ref color);
                drawn++;
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
            return MissileCatalog.TryGetOverviewMarkerScale(projectile, out markerScale);
        }
    }

    [HarmonyPatch(typeof(QuantumSpriteLibrary), "drawBoatIcons", new[] { typeof(QuantumSpriteAsset) })]
    internal static class MissileMapMarkerRenderPatch
    {
        [HarmonyPostfix]
        private static void Postfix(QuantumSpriteAsset pAsset)
        {
            MissileMapMarker.DrawOverviewMarkers(pAsset);
        }
    }
}
