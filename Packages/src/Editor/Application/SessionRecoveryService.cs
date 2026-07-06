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
        private readonly IDomainReloadDetectionService _domainReloadDetectionService;
        private readonly ISessionFlagsRepository _sessionFlagsRepository;

        public SessionRecoveryService(
            IDomainReloadDetectionService domainReloadDetectionService,
            ISessionFlagsRepository sessionFlagsRepository)
        {
            System.Diagnostics.Debug.Assert(domainReloadDetectionService != null, "domainReloadDetectionService must not be null");
            System.Diagnostics.Debug.Assert(sessionFlagsRepository != null, "sessionFlagsRepository must not be null");

            _domainReloadDetectionService = domainReloadDetectionService
                ?? throw new System.ArgumentNullException(nameof(domainReloadDetectionService));
            _sessionFlagsRepository = sessionFlagsRepository
                ?? throw new System.ArgumentNullException(nameof(sessionFlagsRepository));
        }

        public async Task<ValidationResult> RestoreServerStateIfNeededAsync(
            IUnityCliLoopServerRecoveryCoordinator recoveryCoordinator,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            bool isAfterCompile = _sessionFlagsRepository.GetIsAfterCompile();

            IUnityCliLoopServerInstance currentServer = recoveryCoordinator.CurrentServer;
            if (currentServer?.IsRunning == true)
            {
                if (isAfterCompile)
                {
                    _sessionFlagsRepository.ClearAfterCompileFlag();
                }
                return ValidationResult.Success();
            }

            if (isAfterCompile)
            {
                _sessionFlagsRepository.ClearAfterCompileFlag();
            }

            if (_sessionFlagsRepository.GetIsServerManuallyStopped())
            {
                return ValidationResult.Success();
            }

            await recoveryCoordinator.StartRecoveryIfNeededAsync(isAfterCompile, ct);
            IUnityCliLoopServerInstance recoveredServer = recoveryCoordinator.CurrentServer;
            if (recoveredServer?.IsRunning != true)
            {
                return ValidationResult.Failure(
                    "Unity CLI Loop server recovery finished, but no running server instance is available.");
            }

            return ValidationResult.Success();
        }

        public async Task StartReconnectionUITimeoutAsync(CancellationToken ct)
        {
            int timeoutMilliseconds = UnityCliLoopConstants.RECONNECTION_TIMEOUT_SECONDS * 1000;
            await TimerDelay.Wait(timeoutMilliseconds, ct);
            ct.ThrowIfCancellationRequested();

            bool isStillShowingUI = _sessionFlagsRepository.GetShowReconnectingUI();
            if (isStillShowingUI)
            {
                _sessionFlagsRepository.ClearReconnectingFlags();
            }
        }
    }
}
