using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Periodically reconciles persisted server intent with the server owned by this Unity editor.
    /// </summary>
    [InitializeOnLoad]
    public static class ServerStateWatchdog
    {
        private static DateTime? lastCheckUtc;
        private static DateTime? lastRecoveryAttemptUtc;

        static ServerStateWatchdog()
        {
            if (AssetDatabase.IsAssetImportWorkerProcess())
            {
                return;
            }

            EditorApplication.update += OnEditorUpdate;
        }

        private static void OnEditorUpdate()
        {
            DateTime nowUtc = DateTime.UtcNow;
            if (lastCheckUtc.HasValue &&
                nowUtc - lastCheckUtc.Value < TimeSpan.FromSeconds(McpConstants.SERVER_STATE_WATCHDOG_INTERVAL_SECONDS))
            {
                return;
            }

            lastCheckUtc = nowUtc;
            if (McpServerController.RecoveryTask != null && !McpServerController.RecoveryTask.IsCompleted)
            {
                return;
            }

            bool serverIsRunning = McpServerController.IsServerRunning;
            WatchdogObservation observation = new(
                McpEditorSettings.GetIsServerRunning(),
                serverIsRunning,
                McpEditorSettings.GetCustomPort(),
                serverIsRunning ? McpServerController.ServerPort : null,
                nowUtc,
                lastRecoveryAttemptUtc,
                McpServerController.IsStartupProtectionActive());
            WatchdogAction action = ServerStateWatchdogService.DecideAction(observation);

            if (action == WatchdogAction.RecoverServer)
            {
                lastRecoveryAttemptUtc = nowUtc;
                Task recoveryTask = McpServerController.StartRecoveryIfNeededAsync(
                    observation.settingsPort,
                    false,
                    CancellationToken.None);
                _ = recoveryTask.ContinueWith(
                    task => LogRecoveryFailure(task),
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.FromCurrentSynchronizationContext());
                return;
            }

            if (action == WatchdogAction.RewriteSettings)
            {
                McpServerController.SynchronizeRunningServerSettings();
            }
        }

        private static void LogRecoveryFailure(Task recoveryTask)
        {
            VibeLogger.LogError(
                "server_watchdog_recovery_failed",
                recoveryTask.Exception?.GetBaseException().Message);
        }
    }
}
