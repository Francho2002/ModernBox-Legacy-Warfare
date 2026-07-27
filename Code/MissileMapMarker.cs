using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ModernBox
{
    internal static class MissileMapMarker
    {
        private const string ConventionalMarkerId = "modern_cap_missile_marker";
        private const string NuclearMarkerId = "modern_cap_nuclear_missile_marker";

        private static readonly HashSet<string> ConventionalProjectiles =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "missileartillery",
                "fireboneartillery",
                "frostmissileartillery",
                "plantmissileartillery",
                "modernbox_torpedo"
            };

        private static readonly ConditionalWeakTable<Projectile, MarkerState> Markers =
            new ConditionalWeakTable<Projectile, MarkerState>();

        private static readonly FieldInfo SpriteAnimationField =
            AccessTools.Field(typeof(BaseAnimatedObject), "sprite_animation");

        private sealed class MarkerState
        {
            internal BaseEffect effect;
            internal SpriteRenderer renderer;
        }

        internal static void Start(Projectile projectile)
        {
            Remove(projectile);
            Update(projectile);
        }

        internal static void Update(Projectile projectile)
        {
            string markerId;
            float markerScale;
            if (!TryGetMarker(projectile, out markerId, out markerScale))
            {
                Remove(projectile);
                return;
            }

            MarkerState state = Markers.GetOrCreateValue(projectile);
            if (state.effect == null || state.effect.isKilled())
            {
                Vector2 initialPosition = projectile.getTransformedPositionWithHeight();
                state.effect = EffectsLibrary.spawnAt(markerId, initialPosition, markerScale);
                FreezeAnimation(state.effect);
                state.renderer = state.effect != null
                    ? state.effect.GetComponentInChildren<SpriteRenderer>()
                    : null;
            }

            if (state.effect == null || state.effect.isKilled())
            {
                return;
            }

            Vector2 position = projectile.getTransformedPositionWithHeight();
            state.effect.current_position = position;
            state.effect.transform.position = new Vector3(position.x, position.y, 0f);
            state.effect.transform.rotation = projectile.rotation;
            if (state.renderer != null)
            {
                // WorldBox renders the high-camera map after Projectile.update().
                // Disabling the renderer here therefore hid the marker precisely
                // when the overview needed it.  The projectile itself remains at
                // its original scale; this is the independent overview marker.
                state.renderer.enabled = true;
            }
        }

        internal static void Remove(Projectile projectile)
        {
            if (projectile == null)
            {
                return;
            }

            MarkerState state;
            if (Markers.TryGetValue(projectile, out state))
            {
                BaseEffect effect = state.effect;
                if (effect != null && !effect.isKilled())
                {
                    // The object returns to the shared effect pool.
                    if (state.renderer != null)
                    {
                        state.renderer.enabled = true;
                    }
                    effect.kill();
                }
            }

            Markers.Remove(projectile);
        }

        private static bool TryGetMarker(Projectile projectile, out string markerId, out float markerScale)
        {
            markerId = null;
            markerScale = 0f;

            string projectileId = projectile != null && projectile.asset != null
                ? projectile.asset.id
                : null;
            if (string.IsNullOrEmpty(projectileId))
            {
                return false;
            }

            if (ConventionalProjectiles.Contains(projectileId))
            {
                markerId = ConventionalMarkerId;
                markerScale = 1.6f;
                return true;
            }

            if (string.Equals(projectileId, "NUKER", StringComparison.Ordinal))
            {
                markerId = NuclearMarkerId;
                markerScale = 1.2f;
                return true;
            }

            if (string.Equals(projectileId, "SSBN_CZAR_WARHEAD", StringComparison.Ordinal))
            {
                markerId = NuclearMarkerId;
                markerScale = 1.45f;
                return true;
            }

            if (string.Equals(projectileId, "modernbox_hammer_warhead", StringComparison.Ordinal))
            {
                markerId = NuclearMarkerId;
                markerScale = 1.35f;
                return true;
            }

            if (NavalRoles.IsHeavyWarhead(projectileId))
            {
                markerId = NuclearMarkerId;
                markerScale = 1.20f;
                return true;
            }

            return false;
        }

        private static void FreezeAnimation(BaseEffect effect)
        {
            if (effect == null || SpriteAnimationField == null)
            {
                return;
            }

            SpriteAnimation animation = SpriteAnimationField.GetValue(effect) as SpriteAnimation;
            if (animation != null)
            {
                animation.stopAnimations();
            }
        }
    }

    [HarmonyPatch(typeof(Projectile), "start")]
    internal static class MissileMapMarkerStartPatch
    {
        private static void Postfix(Projectile __instance)
        {
            MissileMapMarker.Start(__instance);
        }
    }

    [HarmonyPatch(typeof(Projectile), "update")]
    internal static class MissileMapMarkerUpdatePatch
    {
        private static void Postfix(Projectile __instance)
        {
            MissileMapMarker.Update(__instance);
        }
    }

    [HarmonyPatch(typeof(Projectile), "reset")]
    internal static class MissileMapMarkerResetPatch
    {
        private static void Prefix(Projectile __instance)
        {
            MissileMapMarker.Remove(__instance);
        }
    }
}
