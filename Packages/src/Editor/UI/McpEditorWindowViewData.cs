using UnityEngine;
using System.Collections.Generic;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Data structures for McpEditorWindow View rendering
    /// Related classes: McpEditorWindow, McpEditorWindowView
    /// </summary>
    
    public record ServerStatusData
    {
        public readonly bool IsRunning;
        public readonly int Port;
        public readonly string Status;
        public readonly Color StatusColor;

        public ServerStatusData(bool isRunning, int port, string status, Color statusColor)
        {
            IsRunning = isRunning;
            Port = port;
            Status = status;
            StatusColor = statusColor;
        }
    }
    
    public record ServerControlsData
    {
        public readonly int CustomPort;
        public readonly bool IsServerRunning;
        public readonly bool PortEditable;
        public readonly bool HasPortWarning;
        public readonly string PortWarningMessage;

        public ServerControlsData(int customPort, bool isServerRunning, bool portEditable, bool hasPortWarning = false, string portWarningMessage = null)
        {
            CustomPort = customPort;
            IsServerRunning = isServerRunning;
            PortEditable = portEditable;
            HasPortWarning = hasPortWarning;
            PortWarningMessage = portWarningMessage;
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
    
    public record EditorConfigData
    {
        public readonly McpEditorType SelectedEditor;
        public readonly bool IsServerRunning;
        public readonly int CurrentPort;
        public readonly bool IsConfigured;
        public readonly bool HasPortMismatch;
        public readonly string ConfigurationError;
        public readonly bool IsUpdateNeeded;
        public readonly bool AddRepositoryRoot;
        public readonly bool SupportsRepositoryRootToggle;
        public readonly bool ShowRepositoryRootToggle;

        public EditorConfigData(McpEditorType selectedEditor, bool isServerRunning, int currentPort, bool isConfigured = false, bool hasPortMismatch = false, string configurationError = null, bool isUpdateNeeded = true, bool addRepositoryRoot = false, bool supportsRepositoryRootToggle = false, bool showRepositoryRootToggle = false)
        {
            SelectedEditor = selectedEditor;
            IsServerRunning = isServerRunning;
            CurrentPort = currentPort;
            IsConfigured = isConfigured;
            HasPortMismatch = hasPortMismatch;
            ConfigurationError = configurationError;
            IsUpdateNeeded = isUpdateNeeded;
            AddRepositoryRoot = addRepositoryRoot;
            SupportsRepositoryRootToggle = supportsRepositoryRootToggle;
            ShowRepositoryRootToggle = showRepositoryRootToggle;
        }
    }

    public record ConnectionModeData
    {
        public readonly ConnectionMode Mode;

        public ConnectionModeData(ConnectionMode mode)
        {
            Mode = mode;
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
        public readonly bool AllowThirdPartyTools;
        public readonly DynamicCodeSecurityLevel DynamicCodeSecurityLevel;
        public readonly ToolToggleItem[] BuiltInTools;
        public readonly ToolToggleItem[] ThirdPartyTools;
        public readonly bool IsRegistryAvailable;

        public ToolSettingsSectionData(
            bool showToolSettings,
            bool allowThirdPartyTools,
            DynamicCodeSecurityLevel dynamicCodeSecurityLevel,
            ToolToggleItem[] builtInTools,
            ToolToggleItem[] thirdPartyTools,
            bool isRegistryAvailable)
        {
            ShowToolSettings = showToolSettings;
            AllowThirdPartyTools = allowThirdPartyTools;
            DynamicCodeSecurityLevel = dynamicCodeSecurityLevel;
            BuiltInTools = builtInTools;
            ThirdPartyTools = thirdPartyTools;
            IsRegistryAvailable = isRegistryAvailable;
        }
    }

    public record CliSetupData
    {
        public readonly bool IsCliInstalled;
        public readonly string CliVersion;
        public readonly string PackageVersion;
        public readonly bool NeedsUpdate;
        public readonly bool NeedsDowngrade;
        public readonly bool IsInstallingCli;
        public readonly bool IsChecking;
        public readonly bool IsClaudeSkillsInstalled;
        public readonly bool IsAgentsSkillsInstalled;
        public readonly bool IsCursorSkillsInstalled;
        public readonly bool IsGeminiSkillsInstalled;
        public readonly bool IsCodexSkillsInstalled;
        public readonly bool IsAntigravitySkillsInstalled;
        public readonly SkillsTarget SelectedTarget;
        public readonly bool IsInstallingSkills;

        public CliSetupData(
            bool isCliInstalled,
            string cliVersion,
            string packageVersion,
            bool needsUpdate,
            bool needsDowngrade,
            bool isInstallingCli,
            bool isChecking,
            bool isClaudeSkillsInstalled,
            bool isAgentsSkillsInstalled,
            bool isCursorSkillsInstalled,
            bool isGeminiSkillsInstalled,
            bool isCodexSkillsInstalled,
            bool isAntigravitySkillsInstalled,
            SkillsTarget selectedTarget,
            bool isInstallingSkills)
        {
            IsCliInstalled = isCliInstalled;
            CliVersion = cliVersion;
            PackageVersion = packageVersion;
            NeedsUpdate = needsUpdate;
            NeedsDowngrade = needsDowngrade;
            IsInstallingCli = isInstallingCli;
            IsChecking = isChecking;
            IsClaudeSkillsInstalled = isClaudeSkillsInstalled;
            IsAgentsSkillsInstalled = isAgentsSkillsInstalled;
            IsCursorSkillsInstalled = isCursorSkillsInstalled;
            IsGeminiSkillsInstalled = isGeminiSkillsInstalled;
            IsCodexSkillsInstalled = isCodexSkillsInstalled;
            IsAntigravitySkillsInstalled = isAntigravitySkillsInstalled;
            SelectedTarget = selectedTarget;
            IsInstallingSkills = isInstallingSkills;
        }
    }

}
