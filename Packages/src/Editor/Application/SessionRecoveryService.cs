using System.Threading.Tasks;
using System.Threading;

using io.github.hatayama.UnityCliLoop.Domain;
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
        private readonly IDomainReloadDetectionService _domainReloadDetectionService;
        private readonly UnityCliLoopEditorSettingsService _editorSettingsService;

        public SessionRecoveryService(
            IUnityCliLoopServerRecoveryCoordinator recoveryCoordinator,
            IDomainReloadDetectionService domainReloadDetectionService,
            UnityCliLoopEditorSettingsService editorSettingsService)
        {
            System.Diagnostics.Debug.Assert(recoveryCoordinator != null, "recoveryCoordinator must not be null");
            System.Diagnostics.Debug.Assert(domainReloadDetectionService != null, "domainReloadDetectionService must not be null");
            System.Diagnostics.Debug.Assert(editorSettingsService != null, "editorSettingsService must not be null");

            _recoveryCoordinator = recoveryCoordinator
                ?? throw new System.ArgumentNullException(nameof(recoveryCoordinator));
            _domainReloadDetectionService = domainReloadDetectionService
                ?? throw new System.ArgumentNullException(nameof(domainReloadDetectionService));
            _editorSettingsService = editorSettingsService
                ?? throw new System.ArgumentNullException(nameof(editorSettingsService));
        }

        public async Task<ValidationResult> RestoreServerStateIfNeededAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            bool wasRunning = _editorSettingsService.GetIsServerRunning();
            bool isAfterCompile = _editorSettingsService.GetIsAfterCompile();

            IUnityCliLoopServerInstance currentServer = _recoveryCoordinator.CurrentServer;
            if (currentServer?.IsRunning == true)
            {
                if (isAfterCompile)
                {
                    _editorSettingsService.ClearAfterCompileFlag();
                }
                return ValidationResult.Success();
            }

            if (isAfterCompile)
            {
                _editorSettingsService.ClearAfterCompileFlag();
            }

            if (wasRunning && (currentServer == null || !currentServer.IsRunning))
            {
                await _recoveryCoordinator.StartRecoveryIfNeededAsync(isAfterCompile, ct);
                IUnityCliLoopServerInstance recoveredServer = _recoveryCoordinator.CurrentServer;
                if (recoveredServer?.IsRunning != true)
                {
                    return ValidationResult.Failure(
                        "Unity CLI Loop server recovery finished, but no running server instance is available.");
                }
            }

            return ValidationResult.Success();
        }

        public async Task StartReconnectionUITimeoutAsync(CancellationToken ct)
        {
            int timeoutFrames = UnityCliLoopConstants.RECONNECTION_TIMEOUT_SECONDS * 60;
            await EditorDelay.DelayFrame(timeoutFrames, ct);
            ct.ThrowIfCancellationRequested();

            bool isStillShowingUI = _editorSettingsService.GetShowReconnectingUI();
            if (isStillShowingUI)
            {
                _editorSettingsService.ClearReconnectingFlags();
            }
        }
    }
}
