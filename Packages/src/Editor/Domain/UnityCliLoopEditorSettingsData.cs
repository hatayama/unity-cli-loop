using System;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    [Serializable]
    public record UnityCliLoopEditorSettingsData
    {
        public bool showDeveloperTools = false;
        public string lastSeenSetupWizardVersion = "";
        public bool suppressSetupWizardAutoShow = false;
        public bool legacySetupWizardStateMigrated = false;
        public bool showUnityCliLoopSecuritySetting = true;
        public bool showToolSettings = true;
        public bool installSkillsFlat = true;
        public bool isServerRunning = true;
        public bool isAfterCompile = false;
        public bool isDomainReloadInProgress = false;
        public bool isReconnecting = false;
        public bool showReconnectingUI = false;
        public bool showPostCompileReconnectingUI = false;
    }
}
