using System;
using System.Collections.Generic;

namespace ModernBox
{
    /// <summary>
    /// Stable identifiers for every projectile that participates in ModernBox's
    /// missile rules.  Gameplay systems must query <see cref="MissileCatalog"/>
    /// instead of maintaining their own lists of warheads.
    /// </summary>
    internal static class MissileIds
    {
        internal const string AllianceCruise = "missileartillery";
        internal const string HordeCruise = "fireboneartillery";
        internal const string HardenCruise = "frostmissileartillery";
        internal const string GaiaCruise = "plantmissileartillery";
        internal const string JetRocket = "jetrocketprojectile";
        internal const string JetRocketHorde = "jetrocketprojectileHorde";
        internal const string JetRocketHarden = "jetrocketprojectileHarden";
        internal const string JetRocketGaia = "jetrocketprojectileGaia";
        internal const string BomberRocket = "bomberrocketprojectile";
        internal const string Nuke = "NUKER";
        internal const string BaselineSsbn = "modernbox_baseline_ssbn_warhead";
        internal const string Czar = "SSBN_CZAR_WARHEAD";
        internal const string Torpedo = "modernbox_torpedo";
        internal const string Interceptor = "modernbox_interceptor_missile";
        internal const string Arsenal = "modernbox_arsenal_warhead";
        internal const string Trident = "modernbox_trident_warhead";
        internal const string Neutron = "modernbox_neutron_warhead";
        internal const string Emp = "modernbox_emp_warhead";
        internal const string Hammer = "modernbox_hammer_warhead";
        internal const string Ruin = "modernbox_ruin_warhead";
    }

    internal enum MissileFalloutTier
    {
        None,
        Light,
        Medium,
        Heavy
    }

    /// <summary>
    /// Immutable gameplay metadata for one registered projectile.  The asset
    /// itself remains responsible for its sprite and native blast; this profile
    /// owns the cross-cutting missile behaviour.
    /// </summary>
    internal sealed class MissileProfile
    {
        internal readonly string Id;
        internal readonly bool Offensive;
        internal readonly bool Nuclear;
        internal readonly bool Interceptable;
        internal readonly float BaseInterceptChance;
        internal readonly bool ProtectedFromOrdinaryCollisions;
        internal readonly bool UsesTrail;
        internal readonly float OverviewMarkerScale;
        internal readonly float BlastSafetyRadius;
        internal readonly MissileFalloutTier FalloutTier;
        internal readonly bool ConventionalImpact;
        internal readonly bool HeavyConventionalImpact;
        // Registered warheads never use ProjectileAsset.sound_impact: it is
        // played by MissileLifecycle exactly once, at the captured impact
        // point.  This keeps legacy "fireball" samples from leaking back in.
        internal readonly string ImpactSoundId;
        internal readonly bool ImpactSoundGameViewOnly;

        internal MissileProfile(string id, bool offensive, bool nuclear, bool interceptable,
            float baseInterceptChance, bool protectedFromOrdinaryCollisions, bool usesTrail, float overviewMarkerScale,
            float blastSafetyRadius, MissileFalloutTier falloutTier, bool conventionalImpact,
            bool heavyConventionalImpact, string impactSoundId, bool impactSoundGameViewOnly)
        {
            Id = id;
            Offensive = offensive;
            Nuclear = nuclear;
            Interceptable = interceptable;
            BaseInterceptChance = baseInterceptChance;
            ProtectedFromOrdinaryCollisions = protectedFromOrdinaryCollisions;
            UsesTrail = usesTrail;
            OverviewMarkerScale = overviewMarkerScale;
            BlastSafetyRadius = blastSafetyRadius;
            FalloutTier = falloutTier;
            ConventionalImpact = conventionalImpact;
            HeavyConventionalImpact = heavyConventionalImpact;
            ImpactSoundId = impactSoundId;
            ImpactSoundGameViewOnly = impactSoundGameViewOnly;
        }
    }

    /// <summary>
    /// Single source of truth for ModernBox missile identity and behaviour.
    /// Keep additions here first; launchers, UI markers, alerts, fallout and
    /// interception intentionally delegate to this table.
    /// </summary>
    internal static class MissileCatalog
    {
        private const string TrailEffectId = "modern_cap_missile_trail";
        private const string SilentImpactEffectPrefix = "modernbox_silent_missile_";

        private static readonly Dictionary<string, MissileProfile> Profiles =
            new Dictionary<string, MissileProfile>(StringComparer.OrdinalIgnoreCase)
            {
                { MissileIds.AllianceCruise, Conventional(MissileIds.AllianceCruise) },
                { MissileIds.HordeCruise, Conventional(MissileIds.HordeCruise) },
                { MissileIds.HardenCruise, Conventional(MissileIds.HardenCruise) },
                { MissileIds.GaiaCruise, Conventional(MissileIds.GaiaCruise) },

                { MissileIds.JetRocket, Conventional(MissileIds.JetRocket, 0.58f) },
                { MissileIds.JetRocketHorde, Conventional(MissileIds.JetRocketHorde, 0.58f) },
                { MissileIds.JetRocketHarden, Conventional(MissileIds.JetRocketHarden, 0.58f) },
                { MissileIds.JetRocketGaia, Conventional(MissileIds.JetRocketGaia, 0.58f) },
                { MissileIds.BomberRocket, Conventional(MissileIds.BomberRocket, 0.66f) },

                { MissileIds.Nuke, Nuclear(MissileIds.Nuke, 0.50f, 20f, MissileFalloutTier.Light, 0.25f) },
                { MissileIds.BaselineSsbn, Nuclear(MissileIds.BaselineSsbn, 0.48f, 20f, MissileFalloutTier.Light, 0.25f) },
                { MissileIds.Czar, Nuclear(MissileIds.Czar, 0.50f, 24f, MissileFalloutTier.Heavy, 0.25f) },
                { MissileIds.Torpedo, Conventional(MissileIds.Torpedo, 0.55f, 4f, false) },
                { MissileIds.Interceptor, new MissileProfile(MissileIds.Interceptor, false, false, false, 0f, false,
                    false, 0.38f, 0f, MissileFalloutTier.None, false, false,
                    null, false) },
                { MissileIds.Arsenal, Conventional(MissileIds.Arsenal, 0.72f, 6f, true) },
                { MissileIds.Trident, Nuclear(MissileIds.Trident, 0.55f, 16f, MissileFalloutTier.Medium) },
                { MissileIds.Neutron, Nuclear(MissileIds.Neutron, 0.55f, 8f, MissileFalloutTier.Light) },
                { MissileIds.Emp, Nuclear(MissileIds.Emp, 0.52f, 0f, MissileFalloutTier.None, 0.30f,
                    "event:/SFX/EXPLOSIONS/ExplosionMiddle") },
                { MissileIds.Hammer, Nuclear(MissileIds.Hammer, 0.60f, 34f, MissileFalloutTier.Heavy, 0.12f) },
                { MissileIds.Ruin, Nuclear(MissileIds.Ruin, 0.55f, 11f, MissileFalloutTier.Light) }
            };

        private static MissileProfile Conventional(string id, float markerScale = 0.85f,
            float blastSafetyRadius = 4f, bool heavyImpact = true, float baseInterceptChance = 0.65f)
        {
            return new MissileProfile(id, true, false, true, baseInterceptChance, true, true, markerScale,
                blastSafetyRadius, MissileFalloutTier.None, true, heavyImpact,
                heavyImpact ? "event:/SFX/EXPLOSIONS/ExplosionMeteorite" : "event:/SFX/EXPLOSIONS/ExplosionSmall",
                false);
        }

        private static MissileProfile Nuclear(string id, float markerScale, float blastSafetyRadius,
            MissileFalloutTier falloutTier, float baseInterceptChance = 0.30f,
            string impactSoundId = "event:/SFX/EXPLOSIONS/ExplosionHuge")
        {
            return new MissileProfile(id, true, true, true, baseInterceptChance, true, true, markerScale,
                blastSafetyRadius, falloutTier, false, false,
                impactSoundId, false);
        }

        internal static bool TryGet(string id, out MissileProfile profile)
        {
            if (string.IsNullOrEmpty(id))
            {
                profile = null;
                return false;
            }
            return Profiles.TryGetValue(id, out profile);
        }

        internal static bool TryGet(Projectile projectile, out MissileProfile profile)
        {
            return TryGet(projectile?.asset?.id, out profile);
        }

        internal static bool IsTracked(Projectile projectile)
        {
            MissileProfile profile;
            return TryGet(projectile, out profile);
        }

        internal static bool IsInterceptable(Projectile projectile)
        {
            MissileProfile profile;
            return TryGet(projectile, out profile) && profile.Interceptable;
        }

        internal static bool IsProtected(Projectile projectile)
        {
            MissileProfile profile;
            return TryGet(projectile, out profile) && profile.ProtectedFromOrdinaryCollisions;
        }

        internal static bool IsNuclear(string projectileId)
        {
            MissileProfile profile;
            return TryGet(projectileId, out profile) && profile.Nuclear;
        }

        internal static float GetBaseInterceptChance(string projectileId)
        {
            MissileProfile profile;
            return TryGet(projectileId, out profile) ? profile.BaseInterceptChance : 0.65f;
        }

        internal static bool TryGetOverviewMarkerScale(Projectile projectile, out float scale)
        {
            MissileProfile profile;
            if (TryGet(projectile, out profile) && profile.OverviewMarkerScale > 0f)
            {
                scale = profile.OverviewMarkerScale;
                return true;
            }
            scale = 0f;
            return false;
        }

        internal static float GetBlastSafetyRadius(string projectileId)
        {
            MissileProfile profile;
            return TryGet(projectileId, out profile) ? profile.BlastSafetyRadius : 4f;
        }

        internal static MissileFalloutTier GetFalloutTier(Projectile projectile)
        {
            MissileProfile profile;
            return TryGet(projectile, out profile) ? profile.FalloutTier : MissileFalloutTier.None;
        }

        internal static bool IsConventionalImpact(Projectile projectile, out bool heavy)
        {
            MissileProfile profile;
            if (TryGet(projectile, out profile) && profile.ConventionalImpact)
            {
                heavy = profile.HeavyConventionalImpact;
                return true;
            }
            heavy = false;
            return false;
        }

        /// <summary>
        /// Asset normalization happens after NavalRoles has registered its
        /// custom warheads. It prevents a resolved missile from becoming a
        /// ground object and gives every offensive rocket a visible flame trail.
        /// </summary>
        internal static void NormalizeRegisteredAssets()
        {
            if (AssetManager.projectiles == null)
                return;

            foreach (MissileProfile profile in Profiles.Values)
            {
                ProjectileAsset asset = AssetManager.projectiles.get(profile.Id);
                if (asset == null)
                    continue;

                // Establish the conventional visual first, so its silent
                // clone below also applies to an asset that was incomplete.
                if (profile.Offensive && profile.ConventionalImpact && string.IsNullOrEmpty(asset.end_effect))
                    asset.end_effect = "fx_firebomb_explosion";

                // Some source assets still carry WeaponFireballLand and
                // similar legacy samples. The lifecycle owns the sound path
                // for every registered warhead that defines a sound profile.
                if (!string.IsNullOrEmpty(profile.ImpactSoundId))
                {
                    asset.sound_impact = string.Empty;
                    asset.end_effect = GetSilentImpactEffect(asset.end_effect);
                }

                if (!profile.Offensive)
                    continue;

                asset.can_be_left_on_ground = false;
                asset.can_be_blocked = false;

                // A conventional profile is also an executable contract. New
                // projectiles cannot silently join the catalog without a real
                // blast and visible terminal fire effect.
                if (profile.ConventionalImpact)
                {
                    if (string.IsNullOrEmpty(asset.terraform_option))
                    {
                        asset.terraform_option = "modern_cap_missile_blast";
                        asset.terraform_range = Math.Max(asset.terraform_range, 4);
                    }
                }

                if (!profile.UsesTrail)
                    continue;

                asset.trail_effect_enabled = true;
                asset.trail_effect_id = TrailEffectId;
                asset.trail_effect_scale = 0.30f;
                asset.trail_effect_timer = 0.10f;
            }
        }

        private static string GetSilentImpactEffect(string sourceId)
        {
            if (string.IsNullOrEmpty(sourceId) || sourceId.StartsWith(SilentImpactEffectPrefix,
                StringComparison.Ordinal))
                return sourceId;

            string silentId = SilentImpactEffectPrefix + sourceId;
            EffectAsset silent = AssetManager.effects_library.get(silentId);
            if (silent == null)
            {
                EffectAsset source = AssetManager.effects_library.get(sourceId);
                if (source == null)
                {
                    ModernBoxLogger.Warning("[MissileCatalog] Missing impact effect: " + sourceId);
                    return sourceId;
                }

                silent = AssetManager.effects_library.clone(silentId, sourceId);
                if (silent == null)
                {
                    ModernBoxLogger.Warning("[MissileCatalog] Could not clone impact effect: " + sourceId);
                    return sourceId;
                }
            }

            // End effects retain their sprite, scale and animation but never
            // replay their own launch sound. MissileLifecycle is the sole
            // authority for the corresponding impact report.
            silent.sound_launch = string.Empty;
            return silentId;
        }
    }
}
