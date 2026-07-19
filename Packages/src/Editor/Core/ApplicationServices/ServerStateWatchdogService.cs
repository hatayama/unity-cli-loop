using System;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Decides whether the persisted server state needs recovery or synchronization.
    /// </summary>
    public static class ServerStateWatchdogService
    {
        public const int RECOVERY_RETRY_INTERVAL_SECONDS = 30;

        /// <summary>
        /// Determines the action required to reconcile persisted and observed server state.
        /// </summary>
        public static WatchdogAction DecideAction(WatchdogObservation observation)
        {
            if (!observation.settingsClaimServerRunning)
            {
                return WatchdogAction.None;
            }

            if (observation.isStartupProtectionActive || observation.isBackgroundProcess)
            {
                return WatchdogAction.None;
            }

            if (observation.serverIsRunning)
            {
                bool hasPortMismatch = observation.serverPort.HasValue &&
                    observation.serverPort.Value != observation.settingsPort;
                return hasPortMismatch ? WatchdogAction.RewriteSettings : WatchdogAction.None;
            }

            if (observation.lastRecoveryAttemptUtc.HasValue &&
                observation.currentUtc - observation.lastRecoveryAttemptUtc.Value < TimeSpan.FromSeconds(RECOVERY_RETRY_INTERVAL_SECONDS))
            {
                return WatchdogAction.None;
            }

            return WatchdogAction.RecoverServer;
        }
    }

    /// <summary>
    /// Describes the reconciliation action selected by the watchdog.
    /// </summary>
    public enum WatchdogAction
    {
        None,
        RecoverServer,
        RewriteSettings
    }

    /// <summary>
    /// Immutable snapshot used to make watchdog decisions without side effects.
    /// </summary>
    public sealed class WatchdogObservation
    {
        public readonly bool settingsClaimServerRunning;
        public readonly bool serverIsRunning;
        public readonly int settingsPort;
        public readonly int? serverPort;
        public readonly DateTime currentUtc;
        public readonly DateTime? lastRecoveryAttemptUtc;
        public readonly bool isStartupProtectionActive;
        public readonly bool isBackgroundProcess;

        public WatchdogObservation(
            bool settingsClaimServerRunning,
            bool serverIsRunning,
            int settingsPort,
            int? serverPort,
            DateTime currentUtc,
            DateTime? lastRecoveryAttemptUtc,
            bool isStartupProtectionActive = false,
            bool isBackgroundProcess = false)
        {
            this.settingsClaimServerRunning = settingsClaimServerRunning;
            this.serverIsRunning = serverIsRunning;
            this.settingsPort = settingsPort;
            this.serverPort = serverPort;
            this.currentUtc = currentUtc;
            this.lastRecoveryAttemptUtc = lastRecoveryAttemptUtc;
            this.isStartupProtectionActive = isStartupProtectionActive;
            this.isBackgroundProcess = isBackgroundProcess;
        }
    }
}
