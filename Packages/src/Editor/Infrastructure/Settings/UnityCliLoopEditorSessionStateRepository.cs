using UnityEditor;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Stores Unity CLI Loop runtime flags and pending compile markers for the current Editor session.
    /// </summary>
    public sealed class UnityCliLoopEditorSessionStateRepository : IUnityCliLoopEditorSessionStatePort
    {
        private const string KeyPrefix = "io.github.hatayama.uloopmcp.editorSession.";
        private const string IsServerRunningKey = KeyPrefix + "isServerRunning";
        private const string IsServerManuallyStoppedKey = KeyPrefix + "isServerManuallyStopped";
        private const string IsAfterCompileKey = KeyPrefix + "isAfterCompile";
        private const string IsDomainReloadInProgressKey = KeyPrefix + "isDomainReloadInProgress";
        private const string IsReconnectingKey = KeyPrefix + "isReconnecting";
        private const string ShowReconnectingUIKey = KeyPrefix + "showReconnectingUI";
        private const string ShowPostCompileReconnectingUIKey = KeyPrefix + "showPostCompileReconnectingUI";
        private const string ShouldAutoScanThirdPartyToolMigrationKey =
            KeyPrefix + "shouldAutoScanThirdPartyToolMigration";
        private const string PendingCompileRequestIdsKey = KeyPrefix + "pendingCompileRequestIds";
        private const string LegacyPendingCompileRequestIdKey = KeyPrefix + "pendingCompileRequestId";
        private const string LegacyPendingCompileForceRecompileKey = KeyPrefix + "pendingCompileForceRecompile";
        private const string LegacyPendingCompileExpiresAtUtcTicksKey =
            KeyPrefix + "pendingCompileExpiresAtUtcTicks";
        private const string LegacyPendingCompileReloadObservedKey = KeyPrefix + "pendingCompileReloadObserved";
        private const string PendingCompileKeyPrefix = KeyPrefix + "pendingCompile.";
        private const string PendingCompileForceRecompileKeySuffix = ".forceRecompile";
        private const string PendingCompileExpiresAtUtcTicksKeySuffix = ".expiresAtUtcTicks";
        private const string PendingCompileReloadObservedKeySuffix = ".reloadObserved";

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

        public string GetPendingCompileRequestIds()
        {
            return GetString(PendingCompileRequestIdsKey);
        }

        public void SetPendingCompileRequestIds(string pendingCompileRequestIds)
        {
            SetString(PendingCompileRequestIdsKey, pendingCompileRequestIds);
        }

        public string GetLegacyPendingCompileRequestId()
        {
            return GetString(LegacyPendingCompileRequestIdKey);
        }

        public void SetLegacyPendingCompileRequestId(string pendingCompileRequestId)
        {
            SetString(LegacyPendingCompileRequestIdKey, pendingCompileRequestId);
        }

        public bool GetLegacyPendingCompileForceRecompile()
        {
            return GetBool(LegacyPendingCompileForceRecompileKey);
        }

        public void SetLegacyPendingCompileForceRecompile(bool pendingCompileForceRecompile)
        {
            SetBool(LegacyPendingCompileForceRecompileKey, pendingCompileForceRecompile);
        }

        public string GetLegacyPendingCompileExpiresAtUtcTicks()
        {
            return GetString(LegacyPendingCompileExpiresAtUtcTicksKey);
        }

        public void SetLegacyPendingCompileExpiresAtUtcTicks(string pendingCompileExpiresAtUtcTicks)
        {
            SetString(LegacyPendingCompileExpiresAtUtcTicksKey, pendingCompileExpiresAtUtcTicks);
        }

        public bool GetLegacyPendingCompileReloadObserved()
        {
            return GetBool(LegacyPendingCompileReloadObservedKey);
        }

        public void SetLegacyPendingCompileReloadObserved(bool pendingCompileReloadObserved)
        {
            SetBool(LegacyPendingCompileReloadObservedKey, pendingCompileReloadObserved);
        }

        public bool GetPendingCompileForceRecompile(string requestId)
        {
            return GetBool(CreatePendingCompileKey(requestId, PendingCompileForceRecompileKeySuffix));
        }

        public void SetPendingCompileForceRecompile(string requestId, bool pendingCompileForceRecompile)
        {
            SetBool(CreatePendingCompileKey(requestId, PendingCompileForceRecompileKeySuffix), pendingCompileForceRecompile);
        }

        public string GetPendingCompileExpiresAtUtcTicks(string requestId)
        {
            return GetString(CreatePendingCompileKey(requestId, PendingCompileExpiresAtUtcTicksKeySuffix));
        }

        public void SetPendingCompileExpiresAtUtcTicks(string requestId, string pendingCompileExpiresAtUtcTicks)
        {
            SetString(CreatePendingCompileKey(requestId, PendingCompileExpiresAtUtcTicksKeySuffix), pendingCompileExpiresAtUtcTicks);
        }

        public bool GetPendingCompileReloadObserved(string requestId)
        {
            return GetBool(CreatePendingCompileKey(requestId, PendingCompileReloadObservedKeySuffix));
        }

        public void SetPendingCompileReloadObserved(string requestId, bool pendingCompileReloadObserved)
        {
            SetBool(CreatePendingCompileKey(requestId, PendingCompileReloadObservedKeySuffix), pendingCompileReloadObserved);
        }

        private static string CreatePendingCompileKey(string requestId, string suffix)
        {
            return PendingCompileKeyPrefix + requestId + suffix;
        }

        private static bool GetBool(string key)
        {
            return SessionState.GetBool(key, false);
        }

        private static void SetBool(string key, bool value)
        {
            SessionState.SetBool(key, value);
        }

        private static string GetString(string key)
        {
            return SessionState.GetString(key, "");
        }

        private static void SetString(string key, string value)
        {
            SessionState.SetString(key, value ?? "");
        }
    }
}
