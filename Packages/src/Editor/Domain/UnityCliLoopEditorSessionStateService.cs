using System;
using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    public interface IUnityCliLoopEditorSessionStatePort
    {
        bool GetIsServerRunning();
        void SetIsServerRunning(bool isServerRunning);
        bool GetIsServerManuallyStopped();
        void SetIsServerManuallyStopped(bool isServerManuallyStopped);
        bool GetIsAfterCompile();
        void SetIsAfterCompile(bool isAfterCompile);
        bool GetIsDomainReloadInProgress();
        void SetIsDomainReloadInProgress(bool isDomainReloadInProgress);
        bool GetIsReconnecting();
        void SetIsReconnecting(bool isReconnecting);
        bool GetShowReconnectingUI();
        void SetShowReconnectingUI(bool showReconnectingUI);
        bool GetShowPostCompileReconnectingUI();
        void SetShowPostCompileReconnectingUI(bool showPostCompileReconnectingUI);
        bool GetShouldAutoScanThirdPartyToolMigration();
        void SetShouldAutoScanThirdPartyToolMigration(bool shouldAutoScanThirdPartyToolMigration);
    }

    /// <summary>
    /// Coordinates Unity Editor session-scoped runtime flags through the storage port owned by Infrastructure.
    /// </summary>
    public sealed class UnityCliLoopEditorSessionStateService
    {
        private readonly IUnityCliLoopEditorSessionStatePort _sessionStatePort;

        public UnityCliLoopEditorSessionStateService(IUnityCliLoopEditorSessionStatePort sessionStatePort)
        {
            Debug.Assert(sessionStatePort != null, "sessionStatePort must not be null");

            _sessionStatePort = sessionStatePort ?? throw new ArgumentNullException(nameof(sessionStatePort));
        }

        public bool GetIsServerRunning()
        {
            return _sessionStatePort.GetIsServerRunning();
        }

        public void SetIsServerRunning(bool isServerRunning)
        {
            _sessionStatePort.SetIsServerRunning(isServerRunning);
        }

        public bool GetIsServerManuallyStopped()
        {
            return _sessionStatePort.GetIsServerManuallyStopped();
        }

        public void SetIsServerManuallyStopped(bool isServerManuallyStopped)
        {
            _sessionStatePort.SetIsServerManuallyStopped(isServerManuallyStopped);
        }

        public bool GetIsAfterCompile()
        {
            return _sessionStatePort.GetIsAfterCompile();
        }

        public void SetIsAfterCompile(bool isAfterCompile)
        {
            _sessionStatePort.SetIsAfterCompile(isAfterCompile);
        }

        public bool GetIsDomainReloadInProgress()
        {
            return _sessionStatePort.GetIsDomainReloadInProgress();
        }

        public void SetIsDomainReloadInProgress(bool isDomainReloadInProgress)
        {
            _sessionStatePort.SetIsDomainReloadInProgress(isDomainReloadInProgress);
        }

        public bool GetIsReconnecting()
        {
            return _sessionStatePort.GetIsReconnecting();
        }

        public void SetIsReconnecting(bool isReconnecting)
        {
            _sessionStatePort.SetIsReconnecting(isReconnecting);
        }

        public bool GetShowReconnectingUI()
        {
            return _sessionStatePort.GetShowReconnectingUI();
        }

        public void SetShowReconnectingUI(bool showReconnectingUI)
        {
            _sessionStatePort.SetShowReconnectingUI(showReconnectingUI);
        }

        public bool GetShowPostCompileReconnectingUI()
        {
            return _sessionStatePort.GetShowPostCompileReconnectingUI();
        }

        public void SetShowPostCompileReconnectingUI(bool showPostCompileReconnectingUI)
        {
            _sessionStatePort.SetShowPostCompileReconnectingUI(showPostCompileReconnectingUI);
        }

        public bool GetShouldAutoScanThirdPartyToolMigration()
        {
            return _sessionStatePort.GetShouldAutoScanThirdPartyToolMigration();
        }

        public void SetShouldAutoScanThirdPartyToolMigration(bool shouldAutoScanThirdPartyToolMigration)
        {
            _sessionStatePort.SetShouldAutoScanThirdPartyToolMigration(shouldAutoScanThirdPartyToolMigration);
        }

        public bool ConsumeShouldAutoScanThirdPartyToolMigration()
        {
            if (!GetShouldAutoScanThirdPartyToolMigration())
            {
                return false;
            }

            SetShouldAutoScanThirdPartyToolMigration(false);
            return true;
        }

        public void MarkDomainReloadStarted(bool serverIsRunning)
        {
            SetIsDomainReloadInProgress(true);
            if (!serverIsRunning)
            {
                return;
            }

            SetIsServerRunning(true);
            SetIsServerManuallyStopped(false);
            SetIsAfterCompile(true);
            SetIsReconnecting(true);
            SetShowReconnectingUI(true);
            SetShowPostCompileReconnectingUI(true);
        }

        public void MarkServerStarted()
        {
            SetIsServerRunning(true);
            SetIsServerManuallyStopped(false);
        }

        public void MarkServerManuallyStopped()
        {
            ClearServerSession();
            SetIsServerManuallyStopped(true);
        }

        public void ClearServerSession()
        {
            SetIsServerRunning(false);
        }

        public void ClearAfterCompileFlag()
        {
            SetIsAfterCompile(false);
        }

        public void ClearReconnectingFlags()
        {
            SetIsReconnecting(false);
            SetShowReconnectingUI(false);
        }

        public void ClearPostCompileReconnectingUI()
        {
            SetShowPostCompileReconnectingUI(false);
        }

        public void ClearDomainReloadFlag()
        {
            SetIsDomainReloadInProgress(false);
        }

        public void ClearDomainReloadRecoveryFlags()
        {
            SetIsDomainReloadInProgress(false);
            SetIsAfterCompile(false);
            SetIsReconnecting(false);
            SetShowReconnectingUI(false);
            SetShowPostCompileReconnectingUI(false);
        }

        public void ClearAll()
        {
            ClearServerSession();
            ClearDomainReloadRecoveryFlags();
            SetShouldAutoScanThirdPartyToolMigration(false);
            SetIsServerManuallyStopped(false);
        }
    }
}
