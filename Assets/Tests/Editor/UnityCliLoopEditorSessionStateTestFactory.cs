using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Creates editor session-state services and snapshots for tests that touch live Unity SessionState.
    /// </summary>
    internal static class UnityCliLoopEditorSessionStateTestFactory
    {
        internal static UnityCliLoopEditorSessionStateService CreateService()
        {
            return new UnityCliLoopEditorSessionStateService(
                CreateSessionFlagsRepository(),
                new UnityCliLoopCompileResultSessionRepository(),
                new UnityCliLoopPendingCompileSessionRepository());
        }

        internal static UnityCliLoopSessionFlagsRepository CreateSessionFlagsRepository()
        {
            return new UnityCliLoopSessionFlagsRepository();
        }

        internal static void ClearAll()
        {
            UnityCliLoopSessionFlagsRepository sessionFlagsRepository = CreateSessionFlagsRepository();
            UnityCliLoopEditorSessionStateService service = CreateService();

            sessionFlagsRepository.ClearServerSession();
            sessionFlagsRepository.ClearDomainReloadRecoveryFlags();
            sessionFlagsRepository.SetShouldAutoScanThirdPartyToolMigration(false);
            sessionFlagsRepository.SetIsServerManuallyStopped(false);
            service.ClearCompileResult();
            service.ClearPendingCompileRequest();
        }

        internal static UnityCliLoopEditorSessionStateSnapshot CaptureSnapshot(
            UnityCliLoopEditorSessionStateService service)
        {
            return UnityCliLoopEditorSessionStateSnapshot.Capture(CreateSessionFlagsRepository(), service);
        }
    }

    /// <summary>
    /// Captures production SessionState values so tests can restore the live Editor session.
    /// </summary>
    internal readonly struct UnityCliLoopEditorSessionStateSnapshot
    {
        private readonly bool _isServerRunning;
        private readonly bool _isServerManuallyStopped;
        private readonly bool _isAfterCompile;
        private readonly bool _isDomainReloadInProgress;
        private readonly bool _isReconnecting;
        private readonly bool _showReconnectingUI;
        private readonly bool _showPostCompileReconnectingUI;
        private readonly bool _shouldAutoScanThirdPartyToolMigration;
        private readonly UnityCliLoopStoredCompileResult[] _compileResults;
        private readonly UnityCliLoopPendingCompileRequest[] _pendingCompileRequests;

        private UnityCliLoopEditorSessionStateSnapshot(
            UnityCliLoopSessionFlagsRepository sessionFlagsRepository,
            UnityCliLoopEditorSessionStateService service)
        {
            _isServerRunning = sessionFlagsRepository.GetIsServerRunning();
            _isServerManuallyStopped = sessionFlagsRepository.GetIsServerManuallyStopped();
            _isAfterCompile = sessionFlagsRepository.GetIsAfterCompile();
            _isDomainReloadInProgress = sessionFlagsRepository.GetIsDomainReloadInProgress();
            _isReconnecting = sessionFlagsRepository.GetIsReconnecting();
            _showReconnectingUI = sessionFlagsRepository.GetShowReconnectingUI();
            _showPostCompileReconnectingUI = sessionFlagsRepository.GetShowPostCompileReconnectingUI();
            _shouldAutoScanThirdPartyToolMigration =
                sessionFlagsRepository.GetShouldAutoScanThirdPartyToolMigration();
            _compileResults = service.GetStoredCompileResults();
            _pendingCompileRequests = service.GetPendingCompileRequests();
        }

        internal static UnityCliLoopEditorSessionStateSnapshot Capture(
            UnityCliLoopSessionFlagsRepository sessionFlagsRepository,
            UnityCliLoopEditorSessionStateService service)
        {
            return new UnityCliLoopEditorSessionStateSnapshot(sessionFlagsRepository, service);
        }

        internal void Restore(UnityCliLoopEditorSessionStateService service)
        {
            UnityCliLoopSessionFlagsRepository sessionFlagsRepository =
                UnityCliLoopEditorSessionStateTestFactory.CreateSessionFlagsRepository();
            sessionFlagsRepository.SetIsServerRunning(_isServerRunning);
            sessionFlagsRepository.SetIsServerManuallyStopped(_isServerManuallyStopped);
            sessionFlagsRepository.SetIsAfterCompile(_isAfterCompile);
            sessionFlagsRepository.SetIsDomainReloadInProgress(_isDomainReloadInProgress);
            sessionFlagsRepository.SetIsReconnecting(_isReconnecting);
            sessionFlagsRepository.SetShowReconnectingUI(_showReconnectingUI);
            sessionFlagsRepository.SetShowPostCompileReconnectingUI(_showPostCompileReconnectingUI);
            sessionFlagsRepository.SetShouldAutoScanThirdPartyToolMigration(
                _shouldAutoScanThirdPartyToolMigration);
            service.ClearCompileResult();
            foreach (UnityCliLoopStoredCompileResult compileResult in _compileResults)
            {
                service.StoreCompileResult(
                    compileResult.RequestId,
                    compileResult.ForceRecompile,
                    compileResult.ResultJson,
                    new System.DateTime(compileResult.CompletedAtUtcTicks, System.DateTimeKind.Utc));
            }

            service.ClearPendingCompileRequest();
            foreach (UnityCliLoopPendingCompileRequest pendingCompileRequest in _pendingCompileRequests)
            {
                service.StorePendingCompileRequest(
                    pendingCompileRequest.RequestId,
                    pendingCompileRequest.ForceRecompile,
                    new System.DateTime(
                        pendingCompileRequest.ExpiresAtUtcTicks,
                        System.DateTimeKind.Utc),
                    pendingCompileRequest.ReloadObserved);
            }
        }
    }
}
