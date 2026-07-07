namespace io.github.hatayama.UnityCliLoop.Domain
{
    public record UnityCliLoopEditorSettingsData
    {
        public bool showDeveloperTools { get; init; } = false;
        public string lastSeenSetupWizardVersion { get; init; } = "";
        public string lastSeenSetupWizardMinimumDispatcherVersion { get; init; } = "";
        public bool suppressSetupWizardAutoShow { get; init; } = false;
        public bool legacySetupWizardStateMigrated { get; init; } = false;
        public bool showUnityCliLoopSecuritySetting { get; init; } = true;
        public bool showToolSettings { get; init; } = true;
        public bool installSkillsFlat { get; init; } = true;
    }
}
