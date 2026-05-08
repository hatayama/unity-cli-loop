using System;
using System.Diagnostics;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Application
{
    /// <summary>
    /// Defines the Unity CLI Loop Editor Settings boundary required by the application layer.
    /// </summary>
    internal interface IUnityCliLoopEditorSettingsPort
    {
        void InvalidateCache();
        void RecoverSettingsFileIfNeeded();
        UnityCliLoopEditorSettingsData GetSettings();
        void SaveSettings(UnityCliLoopEditorSettingsData settings);
        void UpdateSettings(Func<UnityCliLoopEditorSettingsData, UnityCliLoopEditorSettingsData> transform);
        string GetLastSeenSetupWizardVersion();
        void SetLastSeenSetupWizardVersion(string version);
        bool GetSuppressSetupWizardAutoShow();
        void SetSuppressSetupWizardAutoShow(bool suppressAutoShow);
        void SetShowUnityCliLoopSecuritySetting(bool showUnityCliLoopSecuritySetting);
        void SetShowToolSettings(bool showToolSettings);
        void SetInstallSkillsFlat(bool installSkillsFlat);
        bool GetIsServerRunning();
        void SetIsServerRunning(bool isServerRunning);
        bool GetIsAfterCompile();
        bool GetIsDomainReloadInProgress();
        void SetIsDomainReloadInProgress(bool isDomainReloadInProgress);
        void SetIsReconnecting(bool isReconnecting);
        bool GetShowReconnectingUI();
        void SetShowReconnectingUI(bool showReconnectingUI);
        void ClearServerSession();
        void ClearAfterCompileFlag();
        void ClearReconnectingFlags();
        void ClearPostCompileReconnectingUI();
        void ClearDomainReloadFlag();
    }

    /// <summary>
    /// Holds Unity CLI Loop Editor settings used by Unity CLI Loop.
    /// </summary>
    internal static class UnityCliLoopEditorSettings
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

        internal static void RecoverSettingsFileIfNeeded()
        {
            Service.RecoverSettingsFileIfNeeded();
        }

        internal static UnityCliLoopEditorSettingsData GetSettings()
        {
            return Service.GetSettings();
        }

        internal static void SaveSettings(UnityCliLoopEditorSettingsData settings)
        {
            Service.SaveSettings(settings);
        }

        internal static void UpdateSettings(Func<UnityCliLoopEditorSettingsData, UnityCliLoopEditorSettingsData> transform)
        {
            Service.UpdateSettings(transform);
        }

        internal static string GetLastSeenSetupWizardVersion()
        {
            return Service.GetLastSeenSetupWizardVersion();
        }

        internal static void SetLastSeenSetupWizardVersion(string version)
        {
            Service.SetLastSeenSetupWizardVersion(version);
        }

        internal static bool GetSuppressSetupWizardAutoShow()
        {
            return Service.GetSuppressSetupWizardAutoShow();
        }

        internal static void SetSuppressSetupWizardAutoShow(bool suppressAutoShow)
        {
            Service.SetSuppressSetupWizardAutoShow(suppressAutoShow);
        }

        internal static void SetShowUnityCliLoopSecuritySetting(bool showUnityCliLoopSecuritySetting)
        {
            Service.SetShowUnityCliLoopSecuritySetting(showUnityCliLoopSecuritySetting);
        }

        internal static void SetShowToolSettings(bool showToolSettings)
        {
            Service.SetShowToolSettings(showToolSettings);
        }

        internal static void SetInstallSkillsFlat(bool installSkillsFlat)
        {
            Service.SetInstallSkillsFlat(installSkillsFlat);
        }

        internal static bool GetIsServerRunning()
        {
            return Service.GetIsServerRunning();
        }

        internal static void SetIsServerRunning(bool isServerRunning)
        {
            Service.SetIsServerRunning(isServerRunning);
        }

        internal static bool GetIsAfterCompile()
        {
            return Service.GetIsAfterCompile();
        }

        internal static bool GetIsDomainReloadInProgress()
        {
            return Service.GetIsDomainReloadInProgress();
        }

        internal static void SetIsDomainReloadInProgress(bool isDomainReloadInProgress)
        {
            Service.SetIsDomainReloadInProgress(isDomainReloadInProgress);
        }

        internal static void SetIsReconnecting(bool isReconnecting)
        {
            Service.SetIsReconnecting(isReconnecting);
        }

        internal static bool GetShowReconnectingUI()
        {
            return Service.GetShowReconnectingUI();
        }

        internal static void SetShowReconnectingUI(bool showReconnectingUI)
        {
            Service.SetShowReconnectingUI(showReconnectingUI);
        }

        internal static void ClearServerSession()
        {
            Service.ClearServerSession();
        }

        internal static void ClearAfterCompileFlag()
        {
            Service.ClearAfterCompileFlag();
        }

        internal static void ClearReconnectingFlags()
        {
            Service.ClearReconnectingFlags();
        }

        internal static void ClearPostCompileReconnectingUI()
        {
            Service.ClearPostCompileReconnectingUI();
        }

        internal static void ClearDomainReloadFlag()
        {
            Service.ClearDomainReloadFlag();
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
