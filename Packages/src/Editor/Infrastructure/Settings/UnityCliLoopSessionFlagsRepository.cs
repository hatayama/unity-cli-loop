using UnityEditor;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Stores Unity CLI Loop runtime flags for the current Editor session.
    /// </summary>
    public sealed class UnityCliLoopSessionFlagsRepository : ISessionFlagsRepository
    {
        private const string KeyPrefix = UnityCliLoopEditorSessionStateStorage.KeyPrefix;
        private const string IsServerRunningKey = KeyPrefix + "isServerRunning";
        private const string IsServerManuallyStoppedKey = KeyPrefix + "isServerManuallyStopped";
        private const string IsAfterCompileKey = KeyPrefix + "isAfterCompile";
        private const string IsDomainReloadInProgressKey = KeyPrefix + "isDomainReloadInProgress";
        private const string IsReconnectingKey = KeyPrefix + "isReconnecting";
        private const string ShowReconnectingUIKey = KeyPrefix + "showReconnectingUI";
        private const string ShowPostCompileReconnectingUIKey = KeyPrefix + "showPostCompileReconnectingUI";
        private const string ShouldAutoScanThirdPartyToolMigrationKey =
            KeyPrefix + "shouldAutoScanThirdPartyToolMigration";
        public bool GetIsServerRunning()
        {
            return GetBool(IsServerRunningKey);
        }

        public void SetIsServerRunning(bool isServerRunning)
        {
            SetBool(IsServerRunningKey, isServerRunning);
        }

        public bool GetIsServerManuallyStopped()
        {
            return GetBool(IsServerManuallyStoppedKey);
        }

        public void SetIsServerManuallyStopped(bool isServerManuallyStopped)
        {
            SetBool(IsServerManuallyStoppedKey, isServerManuallyStopped);
        }

        public bool GetIsAfterCompile()
        {
            return GetBool(IsAfterCompileKey);
        }

        public void SetIsAfterCompile(bool isAfterCompile)
        {
            SetBool(IsAfterCompileKey, isAfterCompile);
        }

        public bool GetIsDomainReloadInProgress()
        {
            return GetBool(IsDomainReloadInProgressKey);
        }

        public void SetIsDomainReloadInProgress(bool isDomainReloadInProgress)
        {
            SetBool(IsDomainReloadInProgressKey, isDomainReloadInProgress);
        }

        public bool GetIsReconnecting()
        {
            return GetBool(IsReconnectingKey);
        }

        public void SetIsReconnecting(bool isReconnecting)
        {
            SetBool(IsReconnectingKey, isReconnecting);
        }

        public bool GetShowReconnectingUI()
        {
            return GetBool(ShowReconnectingUIKey);
        }

        public void SetShowReconnectingUI(bool showReconnectingUI)
        {
            SetBool(ShowReconnectingUIKey, showReconnectingUI);
        }

        public bool GetShowPostCompileReconnectingUI()
        {
            return GetBool(ShowPostCompileReconnectingUIKey);
        }

        public void SetShowPostCompileReconnectingUI(bool showPostCompileReconnectingUI)
        {
            SetBool(ShowPostCompileReconnectingUIKey, showPostCompileReconnectingUI);
        }

        public bool GetShouldAutoScanThirdPartyToolMigration()
        {
            return GetBool(ShouldAutoScanThirdPartyToolMigrationKey);
        }

        public void SetShouldAutoScanThirdPartyToolMigration(bool shouldAutoScanThirdPartyToolMigration)
        {
            SetBool(ShouldAutoScanThirdPartyToolMigrationKey, shouldAutoScanThirdPartyToolMigration);
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

        private static bool GetBool(string key)
        {
            return SessionState.GetBool(key, false);
        }

        private static void SetBool(string key, bool value)
        {
            SessionState.SetBool(key, value);
        }
    }
}
