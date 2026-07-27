using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ModernBox
{
    [HarmonyPatch(typeof(ItemCrafting), "getItemAssetToCraft")]
    internal static class FanpatchGetItemToCraftPatch
    {
        private static void Prefix(ref List<EquipmentAsset> pItemList)
        {
            if (pItemList == null || pItemList.Count == 0)
            {
                return;
            }

            try
            {
                bool gunsAllowed = CustomItemsList.GunsAllowed;
                bool mirvsAllowed = CustomItemsList.MirvsAllowed;
                Dictionary<string, string> weaponEras = CustomItemsList.WeaponEras;

                for (int i = pItemList.Count - 1; i >= 0; i--)
                {
                    EquipmentAsset item = pItemList[i];
                    if (item == null)
                    {
                        continue;
                    }

                    if (!gunsAllowed && weaponEras.ContainsKey(item.id))
                    {
                        pItemList.RemoveAt(i);
                        continue;
                    }

                    if (!mirvsAllowed && CustomItemsList.Kys.Contains(item.id))
                    {
                        pItemList.RemoveAt(i);
                        continue;
                    }

                    if (weaponEras.ContainsKey(item.id) && !CustomItemsList.IsUnifiedWeaponAllowed(item))
                    {
                        pItemList.RemoveAt(i);
                    }
                }
            }
            catch (Exception ex)
            {
                ModernBoxLogger.Error($"[FanpatchFixes] getItemAssetToCraft patch failed: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(Itemz), "LoadItems")]
    internal static class FanpatchExplosionFixPatch
    {
        private static void Postfix()
        {
            try
            {
                const string projectileId = "shotgun_bullet";
                EquipmentAsset m4a1 = AssetManager.items.get("M4A1");
                if (m4a1 != null)
                {
                    m4a1.projectile = projectileId;
                }

                EquipmentAsset greenHeavyBlaster = AssetManager.items.get("greenheavyblaster");
                if (greenHeavyBlaster != null)
                {
                    greenHeavyBlaster.projectile = projectileId;
                }
            }
            catch (Exception ex)
            {
                ModernBoxLogger.Error($"[FanpatchFixes] explosion fix patch failed: {ex.Message}");
            }
        }
    }

    [HarmonyPatch]
    internal static class FanpatchTimeFixYearPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.PropertySetter(typeof(MapStats), "year_obsolete");
        }

        private static bool Prefix(MapStats __instance, int value)
        {
            double adjustedValue = value * 60.0;
            if (__instance.world_time < adjustedValue - 1.0)
            {
                __instance.world_time = adjustedValue;
            }

            return false;
        }
    }

    [HarmonyPatch]
    internal static class FanpatchTimeFixMonthPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.PropertySetter(typeof(MapStats), "month_obsolete");
        }

        private static bool Prefix(MapStats __instance, int value)
        {
            double adjustedValue = value * 5.0;
            if (__instance.world_time > 0 && Math.Abs(__instance.world_time % 60.0) < 0.1)
            {
                __instance.world_time += adjustedValue;
            }
            else if (__instance.world_time < adjustedValue - 1.0)
            {
                __instance.world_time = adjustedValue;
            }

            return false;
        }
    }

    [HarmonyPatch]
    internal static class FanpatchTimeFixWorldTimePatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.PropertySetter(typeof(MapStats), "worldTime_obsolete");
        }

        private static bool Prefix(MapStats __instance, double value)
        {
            if (__instance.world_time < value - 1.0)
            {
                __instance.world_time = value;
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(Projectile), "update")]
    internal static class FanpatchProjectileCleanupPatch
    {
        private static readonly ConditionalWeakTable<Projectile, ProjectileLifetime> Timers = new ConditionalWeakTable<Projectile, ProjectileLifetime>();
        private static readonly HashSet<string> LongRangeImpactProjectiles = new HashSet<string>(StringComparer.Ordinal)
        {
            "missileartillery",
            "fireboneartillery",
            "frostmissileartillery",
            "plantmissileartillery",
            "NUKER",
            "modernbox_baseline_ssbn_warhead",
            "SSBN_CZAR_WARHEAD",
            "modernbox_torpedo",
            "modernbox_arsenal_warhead",
            "modernbox_trident_warhead",
            "modernbox_neutron_warhead",
            "modernbox_emp_warhead",
            "modernbox_hammer_warhead",
            "modernbox_ruin_warhead"
        };

        private sealed class ProjectileLifetime
        {
            public float age;
            public float initialDistance;
            public float lastDistance;
            public float stalledSeconds;
            public bool hasDistanceSample;
        }

        private static void Prefix(Projectile __instance, float pElapsed)
        {
            if (__instance?.asset == null)
            {
                return;
            }

            string id = __instance.asset.id;
            if (string.IsNullOrEmpty(id))
            {
                return;
            }

            // These projectiles are spawned against a position, often beyond the
            // generic 2.5-second cleanup distance. They need a longer allowance,
            // but never an unlimited one: a malformed/overshot target vector can
            // otherwise leave a visible missile looping forever.
            if (LongRangeImpactProjectiles.Contains(id))
            {
                TrackLongRangeProjectile(__instance, pElapsed);
                return;
            }

            if (!id.Contains("shell") && !id.Contains("missile") && !id.Contains("rocket") && !id.Contains("bullet"))
            {
                return;
            }

            ProjectileLifetime data = Timers.GetOrCreateValue(__instance);
            data.age += pElapsed;
            if (data.age > 2.5f)
            {
                __instance.setState(ProjectileState.ToRemove);
            }
        }

        private static void TrackLongRangeProjectile(Projectile projectile, float elapsed)
        {
            ProjectileLifetime data = Timers.GetOrCreateValue(projectile);
            float safeElapsed = Mathf.Max(0f, elapsed);
            data.age += safeElapsed;

            Vector2 current = projectile.getCurrentPosition();
            Vector2 target = projectile.getTargetVector();
            float remainingDistance = Vector2.Distance(current, target);
            bool invalidPosition = float.IsNaN(remainingDistance) || float.IsInfinity(remainingDistance);
            if (invalidPosition)
            {
                projectile.setState(ProjectileState.ToRemove);
                Timers.Remove(projectile);
                return;
            }

            if (!data.hasDistanceSample)
            {
                data.hasDistanceSample = true;
                data.initialDistance = remainingDistance;
                data.lastDistance = remainingDistance;
            }
            else
            {
                // A small tolerance prevents normal low-frame-rate motion from
                // being misclassified as a loop, while a missile parked away
                // from its destination is cleaned up after a visible grace time.
                if (remainingDistance + 0.08f < data.lastDistance)
                    data.stalledSeconds = 0f;
                else if (remainingDistance > 1.5f)
                    data.stalledSeconds += safeElapsed;

                data.lastDistance = remainingDistance;
            }

            float speed = Mathf.Max(1f, projectile.asset.speed);
            float lifetimeLimit = Mathf.Clamp(12f + (data.initialDistance / speed) * 4f, 18f, 45f);
            if (remainingDistance > 1.5f && (data.stalledSeconds > 4.5f || data.age > lifetimeLimit))
            {
                // Do not synthesize an explosion at an unknown/invalid point.
                // The normal game path remains responsible for every valid
                // impact; this only removes a projectile proven unable to land.
                projectile.setState(ProjectileState.ToRemove);
                Timers.Remove(projectile);
            }
        }

        [HarmonyPatch(typeof(Projectile), "start")]
        [HarmonyPostfix]
        private static void ResetTimerOnStart(Projectile __instance)
        {
            if (__instance == null)
            {
                return;
            }

            Timers.Remove(__instance);
        }
    }

}
