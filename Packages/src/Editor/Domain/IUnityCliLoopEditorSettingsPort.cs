using System;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Defines the persistence boundary for Unity CLI Loop editor settings, owned by Infrastructure.
    /// </summary>
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
}
