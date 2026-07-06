using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Creates editor session-state collaborators and snapshots for tests that touch live Unity SessionState.
    /// </summary>
    internal static class UnityCliLoopEditorSessionStateTestFactory
    {
        internal static UnityCliLoopCompileSessionLifecycleService CreateCompileSessionLifecycleService()
        {
            return new UnityCliLoopCompileSessionLifecycleService(
                CreateSessionFlagsRepository(),
                CreateCompileResultSessionRepository(),
                CreatePendingCompileSessionRepository());
        }

        internal static UnityCliLoopSessionFlagsRepository CreateSessionFlagsRepository()
        {
            return new UnityCliLoopSessionFlagsRepository();
        }

        internal static UnityCliLoopCompileResultSessionRepository CreateCompileResultSessionRepository()
        {
            return new UnityCliLoopCompileResultSessionRepository();
        }

        internal static UnityCliLoopPendingCompileSessionRepository CreatePendingCompileSessionRepository()
        {
            return new UnityCliLoopPendingCompileSessionRepository();
        }

        internal static void ClearAll()
        {
            UnityCliLoopSessionFlagsRepository sessionFlagsRepository = CreateSessionFlagsRepository();
            UnityCliLoopCompileResultSessionRepository compileResultSessionRepository =
                CreateCompileResultSessionRepository();
            UnityCliLoopPendingCompileSessionRepository pendingCompileSessionRepository =
                CreatePendingCompileSessionRepository();

            sessionFlagsRepository.ClearServerSession();
            sessionFlagsRepository.ClearDomainReloadRecoveryFlags();
            sessionFlagsRepository.SetShouldAutoScanThirdPartyToolMigration(false);
            sessionFlagsRepository.SetIsServerManuallyStopped(false);
            compileResultSessionRepository.ClearCompileResult();
            pendingCompileSessionRepository.ClearPendingCompileRequest();
        }

        internal static UnityCliLoopEditorSessionStateSnapshot CaptureSnapshot()
        {
            return UnityCliLoopEditorSessionStateSnapshot.Capture(
                CreateSessionFlagsRepository(),
                CreateCompileResultSessionRepository(),
                CreatePendingCompileSessionRepository());
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
            UnityCliLoopCompileResultSessionRepository compileResultSessionRepository,
            UnityCliLoopPendingCompileSessionRepository pendingCompileSessionRepository)
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
            _compileResults = compileResultSessionRepository.GetStoredCompileResults();
            _pendingCompileRequests = pendingCompileSessionRepository.GetPendingCompileRequests();
        }

        internal static UnityCliLoopEditorSessionStateSnapshot Capture(
            UnityCliLoopSessionFlagsRepository sessionFlagsRepository,
            UnityCliLoopCompileResultSessionRepository compileResultSessionRepository,
            UnityCliLoopPendingCompileSessionRepository pendingCompileSessionRepository)
        {
            return new UnityCliLoopEditorSessionStateSnapshot(
                sessionFlagsRepository,
                compileResultSessionRepository,
                pendingCompileSessionRepository);
        }

        internal void Restore()
        {
            UnityCliLoopSessionFlagsRepository sessionFlagsRepository =
                UnityCliLoopEditorSessionStateTestFactory.CreateSessionFlagsRepository();
            UnityCliLoopCompileResultSessionRepository compileResultSessionRepository =
                UnityCliLoopEditorSessionStateTestFactory.CreateCompileResultSessionRepository();
            UnityCliLoopPendingCompileSessionRepository pendingCompileSessionRepository =
                UnityCliLoopEditorSessionStateTestFactory.CreatePendingCompileSessionRepository();
            sessionFlagsRepository.SetIsServerRunning(_isServerRunning);
            sessionFlagsRepository.SetIsServerManuallyStopped(_isServerManuallyStopped);
            sessionFlagsRepository.SetIsAfterCompile(_isAfterCompile);
            sessionFlagsRepository.SetIsDomainReloadInProgress(_isDomainReloadInProgress);
            sessionFlagsRepository.SetIsReconnecting(_isReconnecting);
            sessionFlagsRepository.SetShowReconnectingUI(_showReconnectingUI);
            sessionFlagsRepository.SetShowPostCompileReconnectingUI(_showPostCompileReconnectingUI);
            sessionFlagsRepository.SetShouldAutoScanThirdPartyToolMigration(
                _shouldAutoScanThirdPartyToolMigration);
            compileResultSessionRepository.ClearCompileResult();
            foreach (UnityCliLoopStoredCompileResult compileResult in _compileResults)
            {
                compileResultSessionRepository.StoreCompileResult(
                    compileResult.RequestId,
                    compileResult.ForceRecompile,
                    compileResult.ResultJson,
                    new System.DateTime(compileResult.CompletedAtUtcTicks, System.DateTimeKind.Utc));
            }

            pendingCompileSessionRepository.ClearPendingCompileRequest();
            foreach (UnityCliLoopPendingCompileRequest pendingCompileRequest in _pendingCompileRequests)
            {
                pendingCompileSessionRepository.StorePendingCompileRequest(
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
