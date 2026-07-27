using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace ModernBox
{
    /// <summary>
    /// Reports incoming strategic warheads only during their terminal flight.
    /// It deliberately observes after the anti-missile interceptor has declined
    /// the shot, so an interceptor removal cannot create a warning.
    /// </summary>
    internal static class NuclearAlertController
    {
        private const float AlertEtaSeconds = 3f;
        private const float MinimumFlightAgeSeconds = 0.35f;
        private const float MinimumUsefulEtaSeconds = 0.10f;
        private const float MinimumAlertIntervalSeconds = 1.5f;

        private sealed class AlertState
        {
            internal float age;
            internal bool warned;
        }

        private static readonly ConditionalWeakTable<Projectile, AlertState> States =
            new ConditionalWeakTable<Projectile, AlertState>();
        private static float nextAlertAt;

        internal static void Observe(Projectile projectile, float elapsed)
        {
            if (!Vehicles.balls || projectile?.asset == null || !IsNuclearWarhead(projectile.asset.id))
                return;

            AlertState state = States.GetOrCreateValue(projectile);
            if (state.warned)
                return;

            state.age += Mathf.Max(0f, elapsed);
            // Never announce on the spawn frame. Short-range shots may finish
            // before the terminal window; that is preferable to a launch alert.
            if (state.age < MinimumFlightAgeSeconds)
                return;

            float speed = projectile.asset.speed;
            if (speed <= 0f)
                return;

            float remainingDistance = Vector2.Distance(projectile.getCurrentPosition(), projectile.getTargetVector());
            float eta = remainingDistance / speed;
            if (eta < MinimumUsefulEtaSeconds || eta > AlertEtaSeconds)
                return;
            if (Time.realtimeSinceStartup < nextAlertAt)
                return;

            HistoryHud hud = HistoryHud.instance;
            if (hud == null)
                return;

            state.warned = true;
            nextAlertAt = Time.realtimeSinceStartup + MinimumAlertIntervalSeconds;
            WorldLogMessage message = new WorldLogMessage { asset_id = "modernbox_nuclear_alert" };
            hud.newHistory(message);
        }

        internal static void Forget(Projectile projectile)
        {
            if (projectile != null)
                States.Remove(projectile);
        }

        private static bool IsNuclearWarhead(string projectileId)
        {
            return string.Equals(projectileId, "NUKER", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(projectileId, "SSBN_CZAR_WARHEAD", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(projectileId, "modernbox_neutron_warhead", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(projectileId, "modernbox_hammer_warhead", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(projectileId, "modernbox_ruin_warhead", StringComparison.OrdinalIgnoreCase);
        }
    }
}
