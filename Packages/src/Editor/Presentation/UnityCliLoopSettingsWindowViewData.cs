using UnityEngine;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// Data structures for UnityCliLoopSettingsWindow View rendering
    /// Related classes: UnityCliLoopSettingsWindow, UnityCliLoopSettingsWindowUI
    /// </summary>
    
    public record ServerStatusData
    {
        public readonly bool IsRunning;
        public readonly string Status;
        public readonly Color StatusColor;

        public ServerStatusData(bool isRunning, string status, Color statusColor)
        {
            IsRunning = isRunning;
            Status = status;
            StatusColor = statusColor;
        }
    }
    
    public record ServerControlsData
    {
        public readonly bool IsServerRunning;

        public ServerControlsData(bool isServerRunning)
        {
            IsServerRunning = isServerRunning;
        }
    }
    
    public record ToolToggleItem
    {
        public readonly string ToolName;
        public readonly bool IsEnabled;
        public readonly bool IsThirdParty;
        public readonly string SkillDescription;

        public ToolToggleItem(
            string toolName,
            bool isEnabled,
            bool isThirdParty,
            string skillDescription)
        {
            ToolName = toolName;
            IsEnabled = isEnabled;
            IsThirdParty = isThirdParty;
            SkillDescription = skillDescription;
        }
    }

    public record ToolSettingsSectionData
    {
        public readonly bool ShowToolSettings;
        public readonly ToolToggleItem[] BuiltInTools;
        public readonly ToolToggleItem[] ThirdPartyTools;
        public readonly bool IsRegistryAvailable;
        public readonly bool HasToolListData;

        public ToolSettingsSectionData(
            bool showToolSettings,
            ToolToggleItem[] builtInTools,
            ToolToggleItem[] thirdPartyTools,
            bool isRegistryAvailable,
            bool hasToolListData = true)
        {
            ShowToolSettings = showToolSettings;
            BuiltInTools = builtInTools;
            ThirdPartyTools = thirdPartyTools;
            IsRegistryAvailable = isRegistryAvailable;
            HasToolListData = hasToolListData;
        }
    }

    public record CliSetupData
    {
        public readonly bool IsCliInstalled;
        public readonly string CliVersion;
        public readonly string RequiredCliVersion;
        public readonly bool NeedsUpdate;
        public readonly bool NeedsDowngrade;
        public readonly bool CanUninstallCli;
        public readonly bool NeedsCliPathSetup;
        public readonly bool IsInstallingCli;
        public readonly bool IsChecking;
        public readonly bool IsClaudeSkillsInstalled;
        public readonly bool IsAgentsSkillsInstalled;
        public readonly bool IsCursorSkillsInstalled;
        public readonly bool IsGeminiSkillsInstalled;
        public readonly bool IsCodexSkillsInstalled;
        public readonly bool IsAntigravitySkillsInstalled;
        public readonly SkillInstallState SelectedTargetInstallState;
        public readonly SkillsTarget SelectedTarget;
        public readonly bool GroupSkillsUnderUnityCliLoop;
        public readonly bool IsInstallingSkills;

        public CliSetupData(
            bool isCliInstalled,
            string cliVersion,
            string requiredCliVersion,
            bool needsUpdate,
            bool needsDowngrade,
            bool canUninstallCli,
            bool needsCliPathSetup,
            bool isInstallingCli,
            bool isChecking,
            bool isClaudeSkillsInstalled,
            bool isAgentsSkillsInstalled,
            bool isCursorSkillsInstalled,
            bool isGeminiSkillsInstalled,
            bool isCodexSkillsInstalled,
            bool isAntigravitySkillsInstalled,
            SkillInstallState selectedTargetInstallState,
            SkillsTarget selectedTarget,
            bool groupSkillsUnderUnityCliLoop,
            bool isInstallingSkills)
        {
            IsCliInstalled = isCliInstalled;
            CliVersion = cliVersion;
            RequiredCliVersion = requiredCliVersion;
            NeedsUpdate = needsUpdate;
            NeedsDowngrade = needsDowngrade;
            CanUninstallCli = canUninstallCli;
            NeedsCliPathSetup = needsCliPathSetup;
            IsInstallingCli = isInstallingCli;
            IsChecking = isChecking;
            IsClaudeSkillsInstalled = isClaudeSkillsInstalled;
            IsAgentsSkillsInstalled = isAgentsSkillsInstalled;
            IsCursorSkillsInstalled = isCursorSkillsInstalled;
            IsGeminiSkillsInstalled = isGeminiSkillsInstalled;
            IsCodexSkillsInstalled = isCodexSkillsInstalled;
            IsAntigravitySkillsInstalled = isAntigravitySkillsInstalled;
            SelectedTargetInstallState = selectedTargetInstallState;
            SelectedTarget = selectedTarget;
            GroupSkillsUnderUnityCliLoop = groupSkillsUnderUnityCliLoop;
            IsInstallingSkills = isInstallingSkills;
        }
    }

}
