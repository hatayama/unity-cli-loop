using System.Threading.Tasks;
using System.Threading;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Application
{
    /// <summary>
    /// Defines the coordination boundary for Unity CLI Loop Server Recovery behavior.
    /// </summary>
    public interface IUnityCliLoopServerRecoveryCoordinator
    {
        IUnityCliLoopServerInstance CurrentServer { get; }

        Task StartRecoveryIfNeededAsync(bool isAfterCompile, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Restores persisted server session state after domain reload without owning transport details.
    /// </summary>
    public sealed class SessionRecoveryService
    {
        private readonly IUnityCliLoopServerRecoveryCoordinator _recoveryCoordinator;

        public SessionRecoveryService(IUnityCliLoopServerRecoveryCoordinator recoveryCoordinator)
        {
            System.Diagnostics.Debug.Assert(recoveryCoordinator != null, "recoveryCoordinator must not be null");

            _recoveryCoordinator = recoveryCoordinator
                ?? throw new System.ArgumentNullException(nameof(recoveryCoordinator));
        }

        public ValidationResult RestoreServerStateIfNeeded(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            bool wasRunning = UnityCliLoopEditorSettings.GetIsServerRunning();
            bool isAfterCompile = UnityCliLoopEditorSettings.GetIsAfterCompile();

            IUnityCliLoopServerInstance currentServer = _recoveryCoordinator.CurrentServer;
            if (currentServer?.IsRunning == true)
            {
                CompilationLockService.DeleteLockFile();
                DomainReloadDetectionService.DeleteLockFile();
                // Why: only the startup generation that created serverstarting.lock knows whether
                // the canonical lock still belongs to it or has already been replaced by a newer
                // generation. Why not delete it here: a stale domain-reload recovery path can race
                // with an active startup/prewarm sequence and tear down another generation's lock.

                if (isAfterCompile)
                {
                    UnityCliLoopEditorSettings.ClearAfterCompileFlag();
                }
                return ValidationResult.Success();
            }

            if (isAfterCompile)
            {
                UnityCliLoopEditorSettings.ClearAfterCompileFlag();
            }

            if (wasRunning && (currentServer == null || !currentServer.IsRunning))
            {
                _ = _recoveryCoordinator.StartRecoveryIfNeededAsync(isAfterCompile, ct).ContinueWith(task =>
                {
                    if (task.IsFaulted)
                    {
                        VibeLogger.LogError("server_startup_restore_failed",
                            $"Failed to restore server: {task.Exception?.GetBaseException().Message}");
                    }
                }, TaskScheduler.FromCurrentSynchronizationContext());
            }

            return ValidationResult.Success();
        }

        public async Task StartReconnectionUITimeoutAsync(CancellationToken ct)
        {
            int timeoutFrames = UnityCliLoopConstants.RECONNECTION_TIMEOUT_SECONDS * 60;
            await EditorDelay.DelayFrame(timeoutFrames, ct);
            ct.ThrowIfCancellationRequested();

            bool isStillShowingUI = UnityCliLoopEditorSettings.GetShowReconnectingUI();
            if (isStillShowingUI)
            {
                UnityCliLoopEditorSettings.ClearReconnectingFlags();
            }
        }
    }
}
