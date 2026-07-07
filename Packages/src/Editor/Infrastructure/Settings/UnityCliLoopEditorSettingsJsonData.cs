using System;
using System.Diagnostics;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// JsonUtility storage DTO for Unity CLI Loop editor settings.
    /// </summary>
    [Serializable]
    internal sealed class UnityCliLoopEditorSettingsJsonData
    {
        public bool showDeveloperTools = false;
        public string lastSeenSetupWizardVersion = "";
        public string lastSeenSetupWizardMinimumDispatcherVersion = "";
        public bool suppressSetupWizardAutoShow = false;
        public bool legacySetupWizardStateMigrated = false;
        public bool showUnityCliLoopSecuritySetting = true;
        public bool showToolSettings = true;
        public bool installSkillsFlat = true;

        internal UnityCliLoopEditorSettingsData ToDomain()
        {
            return new UnityCliLoopEditorSettingsData
            {
                showDeveloperTools = showDeveloperTools,
                lastSeenSetupWizardVersion = lastSeenSetupWizardVersion,
                lastSeenSetupWizardMinimumDispatcherVersion = lastSeenSetupWizardMinimumDispatcherVersion,
                suppressSetupWizardAutoShow = suppressSetupWizardAutoShow,
                legacySetupWizardStateMigrated = legacySetupWizardStateMigrated,
                showUnityCliLoopSecuritySetting = showUnityCliLoopSecuritySetting,
                showToolSettings = showToolSettings,
                installSkillsFlat = installSkillsFlat
            };
        }

        internal static UnityCliLoopEditorSettingsJsonData FromDomain(UnityCliLoopEditorSettingsData settings)
        {
            Debug.Assert(settings != null, "settings must not be null");

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            return new UnityCliLoopEditorSettingsJsonData
            {
                showDeveloperTools = settings.showDeveloperTools,
                lastSeenSetupWizardVersion = settings.lastSeenSetupWizardVersion,
                lastSeenSetupWizardMinimumDispatcherVersion = settings.lastSeenSetupWizardMinimumDispatcherVersion,
                suppressSetupWizardAutoShow = settings.suppressSetupWizardAutoShow,
                legacySetupWizardStateMigrated = settings.legacySetupWizardStateMigrated,
                showUnityCliLoopSecuritySetting = settings.showUnityCliLoopSecuritySetting,
                showToolSettings = settings.showToolSettings,
                installSkillsFlat = settings.installSkillsFlat
            };
        }
    }
}
