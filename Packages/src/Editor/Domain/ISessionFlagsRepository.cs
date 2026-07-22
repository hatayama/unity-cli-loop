namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Stores runtime session flags for the current Unity Editor session.
    /// </summary>
    public interface ISessionFlagsRepository
    {
        bool GetIsServerRunning();
        bool GetIsServerManuallyStopped();
        bool GetIsAfterCompile();
        bool GetIsDomainReloadInProgress();
        bool GetShowReconnectingUI();
        void SetIsAfterCompile(bool isAfterCompile);
        void SetIsDomainReloadInProgress(bool isDomainReloadInProgress);
        void SetIsReconnecting(bool isReconnecting);
        void SetShowReconnectingUI(bool showReconnectingUI);
        void SetShowPostCompileReconnectingUI(bool showPostCompileReconnectingUI);
        void SetShouldAutoScanThirdPartyToolMigration(bool shouldAutoScanThirdPartyToolMigration);
        bool GetShouldAutoScanThirdPartyToolMigration();
        bool ConsumeShouldAutoScanThirdPartyToolMigration();
        void MarkServerStarted();
        void MarkServerManuallyStopped();
        void ClearServerSession();
        void ClearAfterCompileFlag();
        void ClearReconnectingFlags();
        void ClearPostCompileReconnectingUI();
        void ClearDomainReloadFlag();
        void ClearDomainReloadRecoveryFlags();
    }
}
