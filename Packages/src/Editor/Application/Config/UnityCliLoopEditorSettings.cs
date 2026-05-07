using System;
using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.Application
{
    /// <summary>
    /// Holds serialized data for Compile Request behavior.
    /// </summary>
    [Serializable]
    public class CompileRequestData
    {
        public string requestId;
        public string json;
    }

    [Serializable]
    public record UnityCliLoopEditorSettingsData
    {
        public bool showDeveloperTools = false;
        public string lastSeenSetupWizardVersion = "";
        public bool suppressSetupWizardAutoShow = false;
        public bool showUnityCliLoopSecuritySetting = true;
        public bool showToolSettings = true;
        public bool installSkillsFlat = true;
        public bool isServerRunning = true;
        public bool isAfterCompile = false;
        public bool isDomainReloadInProgress = false;
        public bool isReconnecting = false;
        public bool showReconnectingUI = false;
        public bool showPostCompileReconnectingUI = false;
        public bool compileWindowHasData = false;
        public string[] pendingCompileRequestIds = new string[0];
        public CompileRequestData[] compileRequests = new CompileRequestData[0];
    }

    // Port for persisted Editor-only Unity CLI Loop settings.
    /// <summary>
    /// Defines the Unity CLI Loop Editor Settings boundary required by the application layer.
    /// </summary>
    public interface IUnityCliLoopEditorSettingsPort
    {
        void InvalidateCache();
        void RecoverSettingsFileIfNeeded();
        UnityCliLoopEditorSettingsData GetSettings();
        void SaveSettings(UnityCliLoopEditorSettingsData settings);
        void UpdateSettings(Func<UnityCliLoopEditorSettingsData, UnityCliLoopEditorSettingsData> transform);
        bool GetShowDeveloperTools();
        void SetShowDeveloperTools(bool show);
        string GetLastSeenSetupWizardVersion();
        void SetLastSeenSetupWizardVersion(string version);
        bool GetSuppressSetupWizardAutoShow();
        void SetSuppressSetupWizardAutoShow(bool suppressAutoShow);
        bool GetShowUnityCliLoopSecuritySetting();
        void SetShowUnityCliLoopSecuritySetting(bool showUnityCliLoopSecuritySetting);
        bool GetShowToolSettings();
        void SetShowToolSettings(bool showToolSettings);
        bool GetInstallSkillsFlat();
        void SetInstallSkillsFlat(bool installSkillsFlat);
        bool GetIsServerRunning();
        void SetIsServerRunning(bool isServerRunning);
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
        bool GetCompileWindowHasData();
        void SetCompileWindowHasData(bool compileWindowHasData);
        void ClearServerSession();
        void ClearAfterCompileFlag();
        void ClearReconnectingFlags();
        void ClearPostCompileReconnectingUI();
        void ClearDomainReloadFlag();
        void ClearCompileWindowData();
        string[] GetPendingCompileRequestIds();
        void SetPendingCompileRequestIds(string[] pendingCompileRequestIds);
        CompileRequestData[] GetCompileRequests();
        void SetCompileRequests(CompileRequestData[] compileRequests);
        string GetCompileRequestJson(string requestId);
        void SetCompileRequestJson(string requestId, string json);
        void ClearAllCompileRequests();
        void AddPendingCompileRequest(string requestId);
        void RemovePendingCompileRequest(string requestId);
    }

    // Static facade retained for Unity callbacks and legacy call sites outside constructor control.
    /// <summary>
    /// Holds Unity CLI Loop Editor settings used by Unity CLI Loop.
    /// </summary>
    public static class UnityCliLoopEditorSettings
    {
        private static IUnityCliLoopEditorSettingsPort ServiceValue;

        internal static void RegisterService(IUnityCliLoopEditorSettingsPort service)
        {
            Debug.Assert(service != null, "service must not be null");

            ServiceValue = service ?? throw new ArgumentNullException(nameof(service));
        }

        internal static void InvalidateCache()
        {
            Service.InvalidateCache();
        }

        public static void RecoverSettingsFileIfNeeded()
        {
            Service.RecoverSettingsFileIfNeeded();
        }

        public static UnityCliLoopEditorSettingsData GetSettings()
        {
            return Service.GetSettings();
        }

        public static void SaveSettings(UnityCliLoopEditorSettingsData settings)
        {
            Service.SaveSettings(settings);
        }

        public static void UpdateSettings(Func<UnityCliLoopEditorSettingsData, UnityCliLoopEditorSettingsData> transform)
        {
            Service.UpdateSettings(transform);
        }

        public static bool GetShowDeveloperTools()
        {
            return Service.GetShowDeveloperTools();
        }

        public static void SetShowDeveloperTools(bool show)
        {
            Service.SetShowDeveloperTools(show);
        }

        public static string GetLastSeenSetupWizardVersion()
        {
            return Service.GetLastSeenSetupWizardVersion();
        }

        public static void SetLastSeenSetupWizardVersion(string version)
        {
            Service.SetLastSeenSetupWizardVersion(version);
        }

        public static bool GetSuppressSetupWizardAutoShow()
        {
            return Service.GetSuppressSetupWizardAutoShow();
        }

        public static void SetSuppressSetupWizardAutoShow(bool suppressAutoShow)
        {
            Service.SetSuppressSetupWizardAutoShow(suppressAutoShow);
        }

        public static bool GetShowUnityCliLoopSecuritySetting()
        {
            return Service.GetShowUnityCliLoopSecuritySetting();
        }

        public static void SetShowUnityCliLoopSecuritySetting(bool showUnityCliLoopSecuritySetting)
        {
            Service.SetShowUnityCliLoopSecuritySetting(showUnityCliLoopSecuritySetting);
        }

        public static bool GetShowToolSettings()
        {
            return Service.GetShowToolSettings();
        }

        public static void SetShowToolSettings(bool showToolSettings)
        {
            Service.SetShowToolSettings(showToolSettings);
        }

        public static bool GetInstallSkillsFlat()
        {
            return Service.GetInstallSkillsFlat();
        }

        public static void SetInstallSkillsFlat(bool installSkillsFlat)
        {
            Service.SetInstallSkillsFlat(installSkillsFlat);
        }

        public static bool GetIsServerRunning()
        {
            return Service.GetIsServerRunning();
        }

        public static void SetIsServerRunning(bool isServerRunning)
        {
            Service.SetIsServerRunning(isServerRunning);
        }

        public static bool GetIsAfterCompile()
        {
            return Service.GetIsAfterCompile();
        }

        public static void SetIsAfterCompile(bool isAfterCompile)
        {
            Service.SetIsAfterCompile(isAfterCompile);
        }

        public static bool GetIsDomainReloadInProgress()
        {
            return Service.GetIsDomainReloadInProgress();
        }

        public static void SetIsDomainReloadInProgress(bool isDomainReloadInProgress)
        {
            Service.SetIsDomainReloadInProgress(isDomainReloadInProgress);
        }

        public static bool GetIsReconnecting()
        {
            return Service.GetIsReconnecting();
        }

        public static void SetIsReconnecting(bool isReconnecting)
        {
            Service.SetIsReconnecting(isReconnecting);
        }

        public static bool GetShowReconnectingUI()
        {
            return Service.GetShowReconnectingUI();
        }

        public static void SetShowReconnectingUI(bool showReconnectingUI)
        {
            Service.SetShowReconnectingUI(showReconnectingUI);
        }

        public static bool GetShowPostCompileReconnectingUI()
        {
            return Service.GetShowPostCompileReconnectingUI();
        }

        public static void SetShowPostCompileReconnectingUI(bool showPostCompileReconnectingUI)
        {
            Service.SetShowPostCompileReconnectingUI(showPostCompileReconnectingUI);
        }

        public static bool GetCompileWindowHasData()
        {
            return Service.GetCompileWindowHasData();
        }

        public static void SetCompileWindowHasData(bool compileWindowHasData)
        {
            Service.SetCompileWindowHasData(compileWindowHasData);
        }

        public static void ClearServerSession()
        {
            Service.ClearServerSession();
        }

        public static void ClearAfterCompileFlag()
        {
            Service.ClearAfterCompileFlag();
        }

        public static void ClearReconnectingFlags()
        {
            Service.ClearReconnectingFlags();
        }

        public static void ClearPostCompileReconnectingUI()
        {
            Service.ClearPostCompileReconnectingUI();
        }

        public static void ClearDomainReloadFlag()
        {
            Service.ClearDomainReloadFlag();
        }

        public static void ClearCompileWindowData()
        {
            Service.ClearCompileWindowData();
        }

        public static string[] GetPendingCompileRequestIds()
        {
            return Service.GetPendingCompileRequestIds();
        }

        public static void SetPendingCompileRequestIds(string[] pendingCompileRequestIds)
        {
            Service.SetPendingCompileRequestIds(pendingCompileRequestIds);
        }

        public static CompileRequestData[] GetCompileRequests()
        {
            return Service.GetCompileRequests();
        }

        public static void SetCompileRequests(CompileRequestData[] compileRequests)
        {
            Service.SetCompileRequests(compileRequests);
        }

        public static string GetCompileRequestJson(string requestId)
        {
            return Service.GetCompileRequestJson(requestId);
        }

        public static void SetCompileRequestJson(string requestId, string json)
        {
            Service.SetCompileRequestJson(requestId, json);
        }

        public static void ClearAllCompileRequests()
        {
            Service.ClearAllCompileRequests();
        }

        public static void AddPendingCompileRequest(string requestId)
        {
            Service.AddPendingCompileRequest(requestId);
        }

        public static void RemovePendingCompileRequest(string requestId)
        {
            Service.RemovePendingCompileRequest(requestId);
        }

        private static IUnityCliLoopEditorSettingsPort Service
        {
            get
            {
                if (ServiceValue == null)
                {
                    throw new InvalidOperationException("Unity CLI Loop Editor settings service is not registered.");
                }

                return ServiceValue;
            }
        }
    }
}
