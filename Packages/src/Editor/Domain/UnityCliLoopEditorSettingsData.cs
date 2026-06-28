using System;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    [Serializable]
    public record UnityCliLoopEditorSettingsData
    {
        public bool showDeveloperTools = false;
        public string lastSeenSetupWizardVersion = "";
        public string lastSeenSetupWizardMinimumDispatcherVersion = "";
        public bool suppressSetupWizardAutoShow = false;
        public bool legacySetupWizardStateMigrated = false;
        public bool showUnityCliLoopSecuritySetting = true;
        public bool showToolSettings = true;
        public bool installSkillsFlat = true;
    }
}
