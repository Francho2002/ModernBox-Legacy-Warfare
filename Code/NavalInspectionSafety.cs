using System;
using UnityEngine;

namespace ModernBox
{
    /// <summary>
    /// Gives every ModernBox combat boat a stable inspection avatar. Cloned
    /// submarine roles do not reliably inherit the base boat's avatar delegate;
    /// without an override WorldBox tries to load a native boat animation that
    /// does not exist for the custom asset and throws every UI frame.
    /// </summary>
    internal static class NavalInspectionSafety
    {
        internal static void NormalizeRegisteredAssets()
        {
            int disabled = 0;
            foreach (ActorAsset asset in AssetManager.actor_library.list)
            {
                if (!IsManagedInspectableBoat(asset))
                    continue;

                Sprite avatar = LoadAvatar(asset.id);
                if (avatar == null)
                {
                    asset.can_be_inspected = false;
                    asset.has_override_avatar_frames = false;
                    disabled++;
                    continue;
                }

                Sprite stableAvatar = avatar;
                asset.can_be_inspected = true;
                asset.has_avatar_prefab = false;
                asset.get_override_avatar_frames = (Actor _) => new[] { stableAvatar };
                asset.has_override_avatar_frames = true;
                if (asset.inspect_avatar_scale <= 0f)
                    asset.inspect_avatar_scale = 1f;
            }

            if (disabled > 0)
            {
                ModernBoxLogger.Warning("[MX.NavalUI] Disabled inspection for " + disabled +
                    " naval assets without a safe avatar.");
            }
        }

        private static bool IsManagedInspectableBoat(ActorAsset asset)
        {
            string id = asset?.id;
            if (string.IsNullOrEmpty(id) || !asset.is_boat)
                return false;

            return id.StartsWith("aDestroyer_", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith("bDestroyer_", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith("CarrierVessel_", StringComparison.OrdinalIgnoreCase) ||
                NavalRoles.IsAnyModernSubmarine(id);
        }

        private static Sprite LoadAvatar(string actorId)
        {
            string faction = GetFaction(actorId);
            string path;
            if (actorId.StartsWith("aDestroyer_", StringComparison.OrdinalIgnoreCase) ||
                actorId.StartsWith("bDestroyer_", StringComparison.OrdinalIgnoreCase))
            {
                path = "actors/Avatars/Destroyer" + FactionSuffix(faction) + "_avatar";
            }
            else if (actorId.StartsWith("CarrierVessel_", StringComparison.OrdinalIgnoreCase))
            {
                path = faction == "alliance"
                    ? "actors/Avatars/OperationalCarrier_avatar"
                    : "actors/Avatars/Carrier" + FactionSuffix(faction) + "_avatar";
            }
            else
            {
                path = "actors/Avatars/Sub" + FactionSuffix(faction) + "_avatar";
            }

            return SpriteTextureLoader.getSprite(path) ??
                SpriteTextureLoader.getSprite("actors/Avatars/base_avatar") ??
                Resources.Load<Sprite>("ui/icons/warhamma");
        }

        private static string GetFaction(string actorId)
        {
            if (actorId.EndsWith("_harden", StringComparison.OrdinalIgnoreCase))
                return "harden";
            if (actorId.EndsWith("_gaia", StringComparison.OrdinalIgnoreCase))
                return "gaia";
            if (actorId.EndsWith("_horde", StringComparison.OrdinalIgnoreCase))
                return "horde";
            return "alliance";
        }

        private static string FactionSuffix(string faction)
        {
            switch (faction)
            {
                case "harden": return "harden";
                case "gaia": return "gaia";
                case "horde": return "horde";
                default: return string.Empty;
            }
        }
    }
}
