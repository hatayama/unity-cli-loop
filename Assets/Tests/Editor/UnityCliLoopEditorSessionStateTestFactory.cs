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
            return new UnityCliLoopEditorSessionStateService(new UnityCliLoopEditorSessionStateRepository());
        }

        internal static UnityCliLoopEditorSessionStateSnapshot CaptureSnapshot(
            UnityCliLoopEditorSessionStateService service)
        {
            return UnityCliLoopEditorSessionStateSnapshot.Capture(service);
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
        private readonly UnityCliLoopPendingCompileRequest _pendingCompileRequest;

        private UnityCliLoopEditorSessionStateSnapshot(UnityCliLoopEditorSessionStateService service)
        {
            _isServerRunning = service.GetIsServerRunning();
            _isServerManuallyStopped = service.GetIsServerManuallyStopped();
            _isAfterCompile = service.GetIsAfterCompile();
            _isDomainReloadInProgress = service.GetIsDomainReloadInProgress();
            _isReconnecting = service.GetIsReconnecting();
            _showReconnectingUI = service.GetShowReconnectingUI();
            _showPostCompileReconnectingUI = service.GetShowPostCompileReconnectingUI();
            _shouldAutoScanThirdPartyToolMigration = service.GetShouldAutoScanThirdPartyToolMigration();
            _pendingCompileRequest = service.GetPendingCompileRequest();
        }

        internal static UnityCliLoopEditorSessionStateSnapshot Capture(
            UnityCliLoopEditorSessionStateService service)
        {
            return new UnityCliLoopEditorSessionStateSnapshot(service);
        }

        internal void Restore(UnityCliLoopEditorSessionStateService service)
        {
            service.SetIsServerRunning(_isServerRunning);
            service.SetIsServerManuallyStopped(_isServerManuallyStopped);
            service.SetIsAfterCompile(_isAfterCompile);
            service.SetIsDomainReloadInProgress(_isDomainReloadInProgress);
            service.SetIsReconnecting(_isReconnecting);
            service.SetShowReconnectingUI(_showReconnectingUI);
            service.SetShowPostCompileReconnectingUI(_showPostCompileReconnectingUI);
            service.SetShouldAutoScanThirdPartyToolMigration(_shouldAutoScanThirdPartyToolMigration);
            if (_pendingCompileRequest.HasRequest)
            {
                service.MarkPendingCompileRequest(
                    _pendingCompileRequest.RequestId,
                    _pendingCompileRequest.ForceRecompile);
                return;
            }

            service.ClearPendingCompileRequest();
        }
    }
}
