using System;
using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    public interface IUnityCliLoopEditorSettingsPort
    {
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
    }

    /// <summary>
    /// Coordinates Unity CLI Loop editor settings through the storage port owned by Infrastructure.
    /// </summary>
    public sealed class UnityCliLoopEditorSettingsService
    {
        private readonly IUnityCliLoopEditorSettingsPort _settingsPort;

        public UnityCliLoopEditorSettingsService(IUnityCliLoopEditorSettingsPort settingsPort)
        {
            Debug.Assert(settingsPort != null, "settingsPort must not be null");

            _settingsPort = settingsPort ?? throw new ArgumentNullException(nameof(settingsPort));
        }

        public void RecoverSettingsFileIfNeeded()
        {
            _settingsPort.RecoverSettingsFileIfNeeded();
        }

        public UnityCliLoopEditorSettingsData GetSettings()
        {
            return _settingsPort.GetSettings();
        }

        public void SaveSettings(UnityCliLoopEditorSettingsData settings)
        {
            _settingsPort.SaveSettings(settings);
        }

        public void UpdateSettings(Func<UnityCliLoopEditorSettingsData, UnityCliLoopEditorSettingsData> transform)
        {
            _settingsPort.UpdateSettings(transform);
        }

        public string GetLastSeenSetupWizardVersion()
        {
            return _settingsPort.GetLastSeenSetupWizardVersion();
        }

        public void SetLastSeenSetupWizardVersion(string version)
        {
            _settingsPort.SetLastSeenSetupWizardVersion(version);
        }

        public bool GetSuppressSetupWizardAutoShow()
        {
            return _settingsPort.GetSuppressSetupWizardAutoShow();
        }

        public void SetSuppressSetupWizardAutoShow(bool suppressAutoShow)
        {
            _settingsPort.SetSuppressSetupWizardAutoShow(suppressAutoShow);
        }

        public void SetShowUnityCliLoopSecuritySetting(bool showUnityCliLoopSecuritySetting)
        {
            _settingsPort.SetShowUnityCliLoopSecuritySetting(showUnityCliLoopSecuritySetting);
        }

        public void SetShowToolSettings(bool showToolSettings)
        {
            _settingsPort.SetShowToolSettings(showToolSettings);
        }

        public void SetInstallSkillsFlat(bool installSkillsFlat)
        {
            _settingsPort.SetInstallSkillsFlat(installSkillsFlat);
        }
    }
}
