using UnityEditor;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Stores Unity CLI Loop runtime flags in Unity SessionState for the current Editor session.
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
        private const string CompileResultRequestIdKey = KeyPrefix + "compileResultRequestId";
        private const string CompileResultForceRecompileKey = KeyPrefix + "compileResultForceRecompile";
        private const string CompileResultJsonKey = KeyPrefix + "compileResultJson";
        private const string CompileResultCompletedAtUtcTicksKey = KeyPrefix + "compileResultCompletedAtUtcTicks";
        private const string PendingCompileRequestIdKey = KeyPrefix + "pendingCompileRequestId";
        private const string PendingCompileForceRecompileKey = KeyPrefix + "pendingCompileForceRecompile";
        private const string PendingCompileExpiresAtUtcTicksKey =
            KeyPrefix + "pendingCompileExpiresAtUtcTicks";
        private const string PendingCompileReloadObservedKey = KeyPrefix + "pendingCompileReloadObserved";

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

        public string GetCompileResultRequestId()
        {
            return GetString(CompileResultRequestIdKey);
        }

        public void SetCompileResultRequestId(string compileResultRequestId)
        {
            SetString(CompileResultRequestIdKey, compileResultRequestId);
        }

        public bool GetCompileResultForceRecompile()
        {
            return GetBool(CompileResultForceRecompileKey);
        }

        public void SetCompileResultForceRecompile(bool compileResultForceRecompile)
        {
            SetBool(CompileResultForceRecompileKey, compileResultForceRecompile);
        }

        public string GetCompileResultJson()
        {
            return GetString(CompileResultJsonKey);
        }

        public void SetCompileResultJson(string compileResultJson)
        {
            SetString(CompileResultJsonKey, compileResultJson);
        }

        public string GetCompileResultCompletedAtUtcTicks()
        {
            return GetString(CompileResultCompletedAtUtcTicksKey);
        }

        public void SetCompileResultCompletedAtUtcTicks(string compileResultCompletedAtUtcTicks)
        {
            SetString(CompileResultCompletedAtUtcTicksKey, compileResultCompletedAtUtcTicks);
        }

        public string GetPendingCompileRequestId()
        {
            return GetString(PendingCompileRequestIdKey);
        }

        public void SetPendingCompileRequestId(string pendingCompileRequestId)
        {
            SetString(PendingCompileRequestIdKey, pendingCompileRequestId);
        }

        public bool GetPendingCompileForceRecompile()
        {
            return GetBool(PendingCompileForceRecompileKey);
        }

        public void SetPendingCompileForceRecompile(bool pendingCompileForceRecompile)
        {
            SetBool(PendingCompileForceRecompileKey, pendingCompileForceRecompile);
        }

        public string GetPendingCompileExpiresAtUtcTicks()
        {
            return GetString(PendingCompileExpiresAtUtcTicksKey);
        }

        public void SetPendingCompileExpiresAtUtcTicks(string pendingCompileExpiresAtUtcTicks)
        {
            SetString(PendingCompileExpiresAtUtcTicksKey, pendingCompileExpiresAtUtcTicks);
        }

        public bool GetPendingCompileReloadObserved()
        {
            return GetBool(PendingCompileReloadObservedKey);
        }

        public void SetPendingCompileReloadObserved(bool pendingCompileReloadObserved)
        {
            SetBool(PendingCompileReloadObservedKey, pendingCompileReloadObserved);
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
