using System;
using System.Collections.Generic;
using UnityEngine;

namespace ModernBox
{
    /// <summary>
    /// Makes the realistic ModernBox combat fleet use WorldBox's native large
    /// boat icons.  This deliberately registers assets in the engine cache
    /// instead of drawing an extra sprite every frame, so the overview remains
    /// legible and keeps the same zoom behaviour as ordinary boats.
    /// </summary>
    internal static class NavalOverviewRegistration
    {
        private static readonly string[] Factions = { "alliance", "harden", "gaia", "horde" };
        private static bool _reportedUnavailable;
        private static bool _reportedSuccess;

        internal static bool EnsureRegistered()
        {
            try
            {
                // Build 719 fills this list during asset-library initialization.
                // It can still be null while mods are bootstrapping, in which
                // case the lightweight controller below retries on later frames.
                var boatAssets = AssetManager.actor_library?.list_only_boat_assets;
                if (boatAssets == null)
                    return false;

                int expected = 0;
                int registered = 0;
                foreach (string assetId in GetOverviewAssetIds())
                {
                    ActorAsset asset = AssetManager.actor_library.get(assetId);
                    if (asset == null)
                        continue;

                    expected++;
                    asset.draw_boat_mark = true;
                    asset.draw_boat_mark_big = true;

                    if (!ContainsAsset(boatAssets, assetId))
                        boatAssets.Add(asset);

                    if (ContainsAsset(boatAssets, assetId))
                        registered++;
                }

                bool complete = expected > 0 && registered == expected;
                if (complete && !_reportedSuccess)
                {
                    _reportedSuccess = true;
                    ModernBoxLogger.Log("[NavalOverview] Registered " + registered + " realistic naval overview icons.");
                }
                return complete;
            }
            catch (Exception ex)
            {
                if (!_reportedUnavailable)
                {
                    _reportedUnavailable = true;
                    ModernBoxLogger.Warning("[NavalOverview] Native boat icon cache is not ready: " + ex.Message);
                }
                return false;
            }
        }

        private static bool ContainsAsset(IEnumerable<ActorAsset> assets, string assetId)
        {
            foreach (ActorAsset candidate in assets)
            {
                if (candidate != null && string.Equals(candidate.id, assetId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static IEnumerable<string> GetOverviewAssetIds()
        {
            foreach (string faction in Factions)
            {
                // The original strategic submarine and the expensive salvo hull.
                yield return "Submarine_" + faction;
                yield return "SalvoSubmarine_" + faction;

                // Realistic surface combatants only. Cargo, fishing, transport,
                // legacy brawlers and any fantasy ship stay out of this cache.
                yield return "CarrierVessel_" + faction;
                yield return "aDestroyer_" + faction;
                yield return "bDestroyer_" + faction;
            }

            // Includes Hunter, Arsenal, Trident, Neutron, EMP, Hammer and Ruin
            // submarine classes for every faction.
            foreach (string assetId in NavalRoles.GetRoleIds())
                yield return assetId;
        }
    }

    /// <summary>
    /// Handles the small lifecycle race between asset registration and the
    /// native boat-icon cache. It stops itself as soon as registration succeeds.
    /// </summary>
    internal sealed class NavalOverviewRegistrationController : MonoBehaviour
    {
        private const float RetryIntervalSeconds = 0.5f;
        private const float RetryDeadlineSeconds = 30f;
        private float _retryAt;
        private float _deadline;
        private float _settleAt;

        private void Awake()
        {
            _deadline = Time.realtimeSinceStartup + RetryDeadlineSeconds;
        }

        private void Update()
        {
            if (Time.realtimeSinceStartup < _retryAt)
                return;

            _retryAt = Time.realtimeSinceStartup + RetryIntervalSeconds;
            if (NavalOverviewRegistration.EnsureRegistered())
            {
                // Keep verifying briefly after the first success: some builds
                // finish their internal boat-cache rebuild a few frames later.
                if (_settleAt <= 0f)
                    _settleAt = Time.realtimeSinceStartup + 3f;
                if (Time.realtimeSinceStartup >= _settleAt)
                    enabled = false;
                return;
            }

            if (Time.realtimeSinceStartup >= _deadline)
                enabled = false;
        }
    }
}
