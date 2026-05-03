using UnityEngine;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Data structures for McpEditorWindow View rendering
    /// Related classes: McpEditorWindow, McpEditorWindowView
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
    
    public record ConnectedToolsData
    {
        public readonly IReadOnlyCollection<ConnectedClient> Clients;
        public readonly bool ShowFoldout;
        public readonly bool IsServerRunning;
        public readonly bool ShowReconnectingUI;
        public readonly bool ShowSection;

        public ConnectedToolsData(IReadOnlyCollection<ConnectedClient> clients, bool showFoldout, bool isServerRunning, bool showReconnectingUI, bool showSection)
        {
            Clients = clients;
            ShowFoldout = showFoldout;
            IsServerRunning = isServerRunning;
            ShowReconnectingUI = showReconnectingUI;
            ShowSection = showSection;
        }
    }
    
    public record ToolToggleItem
    {
        public readonly string ToolName;
        public readonly string Description;
        public readonly bool IsEnabled;
        public readonly bool IsThirdParty;

        public ToolToggleItem(string toolName, string description, bool isEnabled, bool isThirdParty)
        {
            ToolName = toolName;
            Description = description;
            IsEnabled = isEnabled;
            IsThirdParty = isThirdParty;
        }
    }

    public record ToolSettingsSectionData
    {
        public readonly bool ShowToolSettings;
        public readonly DynamicCodeSecurityLevel DynamicCodeSecurityLevel;
        public readonly ToolToggleItem[] BuiltInTools;
        public readonly ToolToggleItem[] ThirdPartyTools;
        public readonly bool IsRegistryAvailable;
        public readonly bool HasToolListData;

        public ToolSettingsSectionData(
            bool showToolSettings,
            DynamicCodeSecurityLevel dynamicCodeSecurityLevel,
            ToolToggleItem[] builtInTools,
            ToolToggleItem[] thirdPartyTools,
            bool isRegistryAvailable,
            bool hasToolListData = true)
        {
            ShowToolSettings = showToolSettings;
            DynamicCodeSecurityLevel = dynamicCodeSecurityLevel;
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
        public readonly string PackageVersion;
        public readonly bool NeedsUpdate;
        public readonly bool NeedsDowngrade;
        public readonly bool CanUninstallCli;
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
            string packageVersion,
            bool needsUpdate,
            bool needsDowngrade,
            bool canUninstallCli,
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
            PackageVersion = packageVersion;
            NeedsUpdate = needsUpdate;
            NeedsDowngrade = needsDowngrade;
            CanUninstallCli = canUninstallCli;
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
