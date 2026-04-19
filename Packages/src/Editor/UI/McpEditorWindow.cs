using UnityEngine;
using UnityEditor;
using System.Linq;
using System.Collections.Generic;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.uLoopMCP
{
    public class McpEditorWindow : EditorWindow
    {
        private McpConfigServiceFactory _configServiceFactory;
        private McpEditorWindowUI _view;
        private McpEditorModel _model;
        private McpEditorWindowEventHandler _eventHandler;

        private SkillsTarget _skillsTarget = SkillsTarget.Claude;
        private bool _installSkillsFlat;
        private bool _isInstallingCli;
        private bool _isInstallingSkills;
        private bool _isRefreshingVersion;
        private SkillInstallState _selectedTargetInstallState = SkillInstallState.Missing;
        private CancellationTokenSource _skillInstallStateRefreshCts;

        [MenuItem("Window/Unity CLI Loop/Settings", priority = 0)]
        public static void ShowWindow()
        {
            McpEditorWindow window = GetWindow<McpEditorWindow>("Unity CLI Loop");
            window.Show();
        }

        private void OnEnable()
        {
            InitializeAll();
        }

        private void OnDestroy()
        {
            CancelSkillInstallStateRefresh();
            _view?.Dispose();
        }

        private void CreateGUI()
        {
            InitializeView();
            RefreshAllSections(refreshSkillInstallState: true);
        }

        private void InitializeAll()
        {
            InitializeModel();
            InitializeConfigurationServices();
            InitializeEventHandler();
            LoadSavedSettings();
            RestoreSessionState();
            HandlePostCompileMode();
        }

        private void InitializeModel()
        {
            _model = new McpEditorModel();
        }

        private void InitializeView()
        {
            _view = new McpEditorWindowUI(rootVisualElement);
            SetupViewCallbacks();
        }

        private void SetupViewCallbacks()
        {
            _view.OnConnectionModeChanged += UpdateConnectionMode;
            _view.OnRefreshCliVersion += HandleRefreshCliVersion;
            _view.OnInstallCli += HandleInstallCli;
            _view.OnInstallSkills += HandleInstallSkills;
            _view.OnRefreshSkillsState += HandleRefreshSkillsState;
            _view.OnSkillsTargetChanged += value =>
            {
                _skillsTarget = value;
                RefreshSelectedTargetInstallStateFast();
                RefreshSelectedTargetInstallStateInBackground();
            };
            _view.OnGroupSkillsChanged += HandleGroupSkillsChanged;
            _view.OnConfigurationFoldoutChanged += UpdateShowConfiguration;
            _view.OnConnectedToolsFoldoutChanged += UpdateShowConnectedTools;
            _view.OnEditorTypeChanged += UpdateSelectedEditorType;
            _view.OnRepositoryRootChanged += UpdateAddRepositoryRoot;
            _view.OnConfigureClicked += ConfigureEditor;
            _view.OnDeleteConfigClicked += DeleteEditorConfiguration;
            _view.OnOpenSettingsClicked += OpenConfigurationFile;
            _view.OnToolSettingsFoldoutChanged += UpdateShowToolSettings;
            _view.OnToolToggled += HandleToolToggled;
            _view.OnAllowThirdPartyChanged += UpdateAllowThirdPartyTools;
            _view.OnSecurityLevelChanged += UpdateDynamicCodeSecurityLevel;
        }

        public IEnumerable<ConnectedClient> GetConnectedToolsAsClients()
        {
            return ConnectedToolsMonitoringService.GetConnectedToolsAsClients();
        }

        private void InitializeConfigurationServices()
        {
            _configServiceFactory = new McpConfigServiceFactory();
        }

        private void InitializeEventHandler()
        {
            _eventHandler = new McpEditorWindowEventHandler(_model, this);
            _eventHandler.Initialize();
        }

        private void LoadSavedSettings()
        {
            _model.LoadFromSettings();
            _installSkillsFlat = McpEditorSettings.GetInstallSkillsFlat();

            bool gitRootDiffers = UnityMcpPathResolver.GitRootDiffersFromProjectRoot();
            _model.UpdateSupportsRepositoryRootToggle(gitRootDiffers);
            _model.UpdateShowRepositoryRootToggle(gitRootDiffers);

            if (!gitRootDiffers && _model.UI.AddRepositoryRoot)
            {
                _model.UpdateAddRepositoryRoot(false);
            }
        }

        private void RestoreSessionState()
        {
            _model.LoadFromSessionState();
        }

        private async void HandlePostCompileMode()
        {
            _model.EnablePostCompileMode();
            McpEditorSettings.SetShowReconnectingUI(false);

            Task recoveryTask = McpServerController.RecoveryTask;
            if (recoveryTask != null && !recoveryTask.IsCompleted)
            {
                await recoveryTask;
            }

            bool isAfterCompile = McpEditorSettings.GetIsAfterCompile();

            if (isAfterCompile)
            {
                McpEditorSettings.ClearAfterCompileFlag();

                int savedPort = McpEditorSettings.GetCustomPort();
                bool portNeedsUpdate = savedPort != _model.UI.CustomPort;

                if (portNeedsUpdate)
                {
                    _model.UpdateCustomPort(savedPort);
                }

                return;
            }

            // McpServerController.[InitializeOnLoad] handles automatic server recovery via RestoreServerStateIfNeeded()
        }

        private void OnDisable()
        {
            CancelSkillInstallStateRefresh();
            CleanupEventHandler();
            SaveSessionState();
            _view?.Dispose();
        }

        private void CleanupEventHandler()
        {
            _eventHandler?.Cleanup();
        }

        private void SaveSessionState()
        {
            _model.SaveToSessionState();
        }

        private void OnFocus()
        {
            RefreshAllSections();
        }

        public void RefreshAllSections(bool refreshSkillInstallState = false)
        {
            if (_view == null)
            {
                return;
            }

            ConnectionModeData modeData = new ConnectionModeData(_model.UI.ConnectionMode);
            _view.UpdateConnectionMode(modeData);
            _view.UpdateConfigurationFoldout(_model.UI.ShowConfiguration);
            _view.UpdateSectionVisibility(_model.UI.ConnectionMode);

            if (refreshSkillInstallState)
            {
                RefreshSelectedTargetInstallStateFast();
            }

            RefreshCliVersionInBackground();
            if (refreshSkillInstallState)
            {
                RefreshSelectedTargetInstallStateInBackground();
            }
            RefreshCliSetupSection();

            ConnectedToolsData toolsData = CreateConnectedToolsData();
            _view.UpdateConnectedTools(toolsData);

            EditorConfigData configData = CreateEditorConfigData();
            _view.UpdateEditorConfig(configData);

            ToolSettingsSectionData toolSettingsData = CreateToolSettingsData();
            _view.UpdateToolSettings(toolSettingsData);
        }

        private async void RefreshCliVersionInBackground()
        {
            if (CliInstallationDetector.IsCheckCompleted())
            {
                return;
            }

            await CliInstallationDetector.RefreshCliVersionAsync(CancellationToken.None);
            RefreshCliSetupSection();
            RefreshSelectedTargetInstallStateInBackground();
        }

        private async void HandleRefreshCliVersion()
        {
            if (_isRefreshingVersion)
            {
                return;
            }

            _isRefreshingVersion = true;
            RefreshCliSetupSection();

            try
            {
                Task forceRefresh = CliInstallationDetector.ForceRefreshCliVersionAsync(CancellationToken.None);
                Task minimumDelay = Task.Delay(500);
                await Task.WhenAll(forceRefresh, minimumDelay);
            }
            finally
            {
                _isRefreshingVersion = false;
                RefreshCliSetupSection();
                RefreshSelectedTargetInstallStateInBackground();
            }
        }

        public void RefreshConnectedToolsSection()
        {
            if (_view == null)
            {
                return;
            }

            ConnectedToolsData toolsData = CreateConnectedToolsData();
            _view.UpdateConnectedTools(toolsData);
        }

        private ConnectedToolsData CreateConnectedToolsData()
        {
            bool isServerRunning = McpServerController.IsServerRunning;
            ConnectedClient[] connectedClients = GetConnectedToolsAsClients().ToArray();
            bool showReconnectingUIFlag = McpEditorSettings.GetShowReconnectingUI();
            bool showPostCompileUIFlag = McpEditorSettings.GetShowPostCompileReconnectingUI();
            bool hasNamedClients = connectedClients.Any();
            bool showReconnectingUI = (showReconnectingUIFlag || showPostCompileUIFlag) && !hasNamedClients;

            if (hasNamedClients && showPostCompileUIFlag)
            {
                McpEditorSettings.ClearPostCompileReconnectingUI();
            }

            bool showSection = isServerRunning && hasNamedClients;

            return new ConnectedToolsData(connectedClients, _model.UI.ShowConnectedTools, isServerRunning, showReconnectingUI, showSection);
        }

        private EditorConfigData CreateEditorConfigData()
        {
            bool isServerRunning = McpServerController.IsServerRunning;
            int currentPort = McpServerController.ServerPort;

            bool isConfigured = false;
            bool hasPortMismatch = false;
            bool isUpdateNeeded = true;
            string configurationError = null;

            IMcpConfigService configService = GetConfigService(_model.UI.SelectedEditorType);
            isConfigured = configService.IsConfigured();

            if (isConfigured)
            {
                int configuredPort = configService.GetConfiguredPort();

                if (isServerRunning)
                {
                    hasPortMismatch = currentPort != configuredPort;
                }
                else
                {
                    hasPortMismatch = McpEditorSettings.GetCustomPort() != configuredPort;
                }
            }

            int portToCheck = isServerRunning ? currentPort : McpEditorSettings.GetCustomPort();
            isUpdateNeeded = configService.IsUpdateNeeded(portToCheck);

            return new EditorConfigData(
                _model.UI.SelectedEditorType,
                isServerRunning,
                currentPort,
                isConfigured,
                hasPortMismatch,
                configurationError,
                isUpdateNeeded,
                _model.UI.AddRepositoryRoot,
                _model.UI.SupportsRepositoryRootToggle,
                _model.UI.ShowRepositoryRootToggle);
        }

        private ToolSettingsSectionData CreateToolSettingsData()
        {
            UnityToolRegistry registry = CustomToolManager.GetRegistry();
            if (registry == null)
            {
                return new ToolSettingsSectionData(
                    _model.UI.ShowToolSettings,
                    ULoopSettings.GetAllowThirdPartyTools(),
                    ULoopSettings.GetDynamicCodeSecurityLevel(),
                    System.Array.Empty<ToolToggleItem>(),
                    System.Array.Empty<ToolToggleItem>(),
                    false);
            }

            ToolInfo[] allTools = registry.GetAllRegisteredToolInfos();

            System.Collections.Generic.List<ToolToggleItem> builtIn = new();
            System.Collections.Generic.List<ToolToggleItem> thirdParty = new();

            foreach (ToolInfo tool in allTools)
            {
                // Internal tools are always enabled and hidden from UI
                if (tool.DisplayDevelopmentOnly)
                {
                    continue;
                }

                bool isEnabled = ToolSettings.IsToolEnabled(tool.Name);
                bool isThirdPartyTool = registry.IsThirdPartyTool(tool.Name);

                ToolToggleItem item = new ToolToggleItem(tool.Name, tool.Description, isEnabled, isThirdPartyTool);
                if (isThirdPartyTool)
                {
                    thirdParty.Add(item);
                }
                else
                {
                    builtIn.Add(item);
                }
            }

            Comparison<ToolToggleItem> compareByName = (a, b) => string.Compare(a.ToolName, b.ToolName, StringComparison.Ordinal);
            builtIn.Sort(compareByName);
            thirdParty.Sort(compareByName);

            return new ToolSettingsSectionData(
                _model.UI.ShowToolSettings,
                ULoopSettings.GetAllowThirdPartyTools(),
                ULoopSettings.GetDynamicCodeSecurityLevel(),
                builtIn.ToArray(),
                thirdParty.ToArray(),
                true);
        }

        private void UpdateShowToolSettings(bool show)
        {
            _model.UpdateShowToolSettings(show);
        }

        private void HandleToolToggled(string toolName, bool enabled)
        {
            _model.UpdateToolEnabled(toolName, enabled);
            _view?.UpdateSingleToolToggle(toolName, enabled);

            // Skill synchronization can touch many files, so defer it to keep UI input responsive.
            EditorApplication.delayCall += () => ApplyToolToggleSideEffects(toolName, enabled);
        }

        private async void ApplyToolToggleSideEffects(string toolName, bool enabled)
        {
            ClientNotificationService.TriggerToolChangeNotification();

            if (!enabled)
            {
                ToolSkillSynchronizer.RemoveSkillFiles(toolName);
            }
            else
            {
                await ToolSkillSynchronizer.InstallSkillFiles(!_installSkillsFlat);

                if (!ToolSkillSynchronizer.IsSkillInstalled(toolName))
                {
                    Debug.LogWarning(
                        $"[uLoopMCP] Skill for '{toolName}' was not installed after enabling. " +
                        "The skill source may have an incorrect directory structure " +
                        "(expected: <ToolDir>/Skill/SKILL.md). Run 'uloop skills list' for details."
                    );
                }
            }
        }

        private void ConfigureEditor()
        {
            IMcpConfigService configService = GetConfigService(_model.UI.SelectedEditorType);
            bool isServerRunning = McpServerController.IsServerRunning;
            int portToUse = isServerRunning ? McpServerController.ServerPort : McpEditorSettings.GetCustomPort();

            configService.AutoConfigure(portToUse);
            RefreshAllSections();
        }

        private void DeleteEditorConfiguration()
        {
            string editorName = GetEditorDisplayName(_model.UI.SelectedEditorType);

            bool confirmed = EditorUtility.DisplayDialog(
                "Delete MCP Configuration",
                $"Are you sure you want to delete the {editorName} MCP configuration?\n\n" +
                "This will remove the uLoopMCP entry from the configuration file. " +
                "Other MCP server configurations will not be affected.",
                "Delete",
                "Cancel");

            if (!confirmed)
            {
                return;
            }

            IMcpConfigService configService = GetConfigService(_model.UI.SelectedEditorType);
            configService.DeleteConfiguration();
            RefreshAllSections();
        }

        private void OpenConfigurationFile()
        {
            string projectRoot = UnityMcpPathResolver.GetProjectRoot();
            string gitRoot = UnityMcpPathResolver.GetGitRepositoryRoot();
            string baseRoot = _model.UI.AddRepositoryRoot
                ? (gitRoot ?? projectRoot)
                : projectRoot;

            string configPath = UnityMcpPathResolver.GetConfigPathForRoot(_model.UI.SelectedEditorType, baseRoot);
            bool exists = System.IO.File.Exists(configPath);

            if (exists)
            {
                EditorUtility.OpenWithDefaultApp(configPath);
            }
            else
            {
                string editorName = GetEditorDisplayName(_model.UI.SelectedEditorType);
                EditorUtility.DisplayDialog(
                    "Configuration File Not Found",
                    $"Configuration file for {editorName} not found at:\n{configPath}\n\nPlease run 'Configure {editorName}' first to create the configuration file.",
                    "OK");
            }
        }

        private string GetEditorDisplayName(McpEditorType editorType)
        {
            return editorType switch
            {
                McpEditorType.Cursor => "Cursor",
                McpEditorType.ClaudeCode => "Claude Code",
                McpEditorType.VSCode => "VSCode",
                McpEditorType.GeminiCLI => "Gemini CLI",
                McpEditorType.Codex => "Codex",
                McpEditorType.McpInspector => "MCP Inspector",
                _ => editorType.ToString()
            };
        }

        private IMcpConfigService GetConfigService(McpEditorType editorType)
        {
            return _configServiceFactory.GetConfigService(editorType);
        }

        private void UpdateShowConnectedTools(bool show)
        {
            _model.UpdateShowConnectedTools(show);
        }

        private void UpdateSelectedEditorType(McpEditorType type)
        {
            _model.UpdateSelectedEditorType(type);
            RefreshAllSections();
        }

        private void UpdateShowConfiguration(bool show)
        {
            _model.UpdateShowConfiguration(show);
        }

        private void UpdateAllowThirdPartyTools(bool allow)
        {
            _model.UpdateAllowThirdPartyTools(allow);
            RefreshAllSections();
        }

        private void UpdateAddRepositoryRoot(bool addRepositoryRoot)
        {
            _model.UpdateAddRepositoryRoot(addRepositoryRoot);
            RefreshAllSections();
        }

        private void UpdateDynamicCodeSecurityLevel(DynamicCodeSecurityLevel level)
        {
            ULoopSettings.SetDynamicCodeSecurityLevel(level);
        }

        private void UpdateConnectionMode(ConnectionMode mode)
        {
            _model.UpdateConnectionMode(mode);
            _view.UpdateConnectionMode(new ConnectionModeData(mode));
            _view.UpdateSectionVisibility(mode);
            RefreshCliSetupSection();
        }

        private void RefreshCliSetupSection()
        {
            if (_view == null)
            {
                return;
            }

            CliSetupData cliData = CreateCliSetupData();
            _view.UpdateCliSetup(cliData);
        }

        private CliSetupData CreateCliSetupData()
        {
            string cliVersion = CliInstallationDetector.GetCachedCliVersion();
            bool isCliInstalled = cliVersion != null;
            bool isChecking = !CliInstallationDetector.IsCheckCompleted() || _isRefreshingVersion;
            string packageVersion = McpConstants.PackageInfo.version;
            bool needsUpdate = false;
            bool needsDowngrade = false;
            if (isCliInstalled)
            {
                System.Version cliVer = new System.Version(cliVersion);
                System.Version pkgVer = new System.Version(packageVersion);
                needsUpdate = cliVer < pkgVer;
                needsDowngrade = cliVer > pkgVer;
            }
            bool groupSkillsUnderUnityCliLoop = !_installSkillsFlat;
            bool isClaudeInstalled = CliInstallationDetector.AreSkillsInstalled(
                ".claude",
                groupSkillsUnderUnityCliLoop);
            bool isAgentsInstalled = CliInstallationDetector.AreSkillsInstalled(
                ".agents",
                groupSkillsUnderUnityCliLoop);
            bool isCursorInstalled = CliInstallationDetector.AreSkillsInstalled(
                ".cursor",
                groupSkillsUnderUnityCliLoop);
            bool isGeminiInstalled = CliInstallationDetector.AreSkillsInstalled(
                ".gemini",
                groupSkillsUnderUnityCliLoop);
            bool isCodexInstalled = CliInstallationDetector.AreSkillsInstalled(
                ".codex",
                groupSkillsUnderUnityCliLoop);
            bool isAntigravityInstalled = CliInstallationDetector.AreSkillsInstalled(
                ".agent",
                groupSkillsUnderUnityCliLoop);

            return new CliSetupData(
                isCliInstalled,
                cliVersion,
                packageVersion,
                needsUpdate,
                needsDowngrade,
                _isInstallingCli,
                isChecking,
                isClaudeInstalled,
                isAgentsInstalled,
                isCursorInstalled,
                isGeminiInstalled,
                isCodexInstalled,
                isAntigravityInstalled,
                _selectedTargetInstallState,
                _skillsTarget,
                groupSkillsUnderUnityCliLoop,
                _isInstallingSkills);
        }

        private void RefreshSelectedTargetInstallStateFast()
        {
            if (!CliInstallationDetector.IsCliInstalled())
            {
                _selectedTargetInstallState = SkillInstallState.Missing;
                RefreshCliSetupSection();
                return;
            }

            _selectedTargetInstallState = GetSelectedTargetInstallState(includeFreshnessCheck: false);
            RefreshCliSetupSection();
        }

        private void RefreshSelectedTargetInstallStateInBackground()
        {
            CancelSkillInstallStateRefresh();
            if (!CliInstallationDetector.IsCliInstalled() || _isRefreshingVersion || _isInstallingSkills)
            {
                return;
            }

            CancellationTokenSource cts = new();
            _skillInstallStateRefreshCts = cts;
            RefreshSelectedTargetInstallStateAsync(cts.Token);
        }

        private async void RefreshSelectedTargetInstallStateAsync(CancellationToken ct)
        {
            string projectRoot = UnityMcpPathResolver.GetProjectRoot();
            SkillInstallState installState = await Task.Run(
                () => GetSelectedTargetInstallState(projectRoot, includeFreshnessCheck: true));
            if (ct.IsCancellationRequested)
            {
                return;
            }

            _selectedTargetInstallState = installState;
            RefreshCliSetupSection();
        }

        private SkillInstallState GetSelectedTargetInstallState(bool includeFreshnessCheck)
        {
            string projectRoot = UnityMcpPathResolver.GetProjectRoot();
            return GetSelectedTargetInstallState(projectRoot, includeFreshnessCheck);
        }

        private SkillInstallState GetSelectedTargetInstallState(
            string projectRoot,
            bool includeFreshnessCheck)
        {
            SkillsTargetSelection selection = SkillsTargetSelectionResolver.Resolve(
                _skillsTarget,
                !_installSkillsFlat);
            List<ToolSkillSynchronizer.SkillTargetInfo> targets = includeFreshnessCheck
                ? ToolSkillSynchronizer.DetectTargetsForLayoutAtProjectRoot(projectRoot, !_installSkillsFlat)
                : ToolSkillSynchronizer.DetectTargetsForLayoutFastAtProjectRoot(projectRoot, !_installSkillsFlat);
            ToolSkillSynchronizer.SkillTargetInfo targetInfo = targets
                .FirstOrDefault(target => target.DirName == selection.DirectoryName);

            return string.IsNullOrEmpty(targetInfo.DirName)
                ? SkillInstallState.Missing
                : targetInfo.InstallState;
        }

        private void CancelSkillInstallStateRefresh()
        {
            if (_skillInstallStateRefreshCts == null)
            {
                return;
            }

            _skillInstallStateRefreshCts.Cancel();
            _skillInstallStateRefreshCts.Dispose();
            _skillInstallStateRefreshCts = null;
        }

        private async void HandleInstallCli()
        {
            string npmPath = NodeEnvironmentResolver.FindNpmPath();
            if (string.IsNullOrEmpty(npmPath))
            {
                EditorUtility.DisplayDialog(
                    "npm Not Found",
                    "npm was not found on this system.\nPlease install Node.js first, then try again.",
                    "OK");
                return;
            }

            string packageVersion = McpConstants.PackageInfo.version;
            string installTarget = $"{CliConstants.NPM_PACKAGE_NAME}@{packageVersion}";

            bool permissionOk = CliInstaller.CheckWindowsPermissions(
                npmPath, installTarget, out string globalPrefix, out string manualCommand);
            if (!permissionOk)
            {
                EditorUtility.DisplayDialog(
                    "Permission Issue Detected",
                    $"npm's global directory ({globalPrefix}) requires elevated permissions.\n\n"
                    + NpmInstallDiagnostics.BuildPermissionSolutions(manualCommand),
                    "OK");
                return;
            }

            _isInstallingCli = true;
            RefreshCliSetupSection();

            try
            {
                string nodePath = NodeEnvironmentResolver.FindNodePath();
                CliInstallResult result = await CliInstaller.InstallAsync(npmPath, installTarget, nodePath);

                if (!result.Success)
                {
                    string installCommand = $"npm install -g {installTarget}";

                    // Classifier emits Windows-specific remediation with the command embedded;
                    // on other platforms (or unrecognized errors) show raw stderr + manual command footer
                    string guidance = Application.platform == RuntimePlatform.WindowsEditor
                        ? NpmInstallDiagnostics.ClassifyInstallError(result.ErrorOutput, installCommand)
                        : null;

                    string message;
                    if (guidance != null && !string.IsNullOrEmpty(result.ErrorOutput))
                    {
                        message = "Failed to install uLoop CLI.\n\n"
                            + NpmInstallDiagnostics.BuildInstallErrorMessage(guidance, result.ErrorOutput);
                    }
                    else
                    {
                        message = $"Failed to install uLoop CLI.\n\n{result.ErrorOutput}\n\nYou can try manually:\n{installCommand}";
                    }

                    EditorUtility.DisplayDialog("Installation Failed", message, "OK");
                }
            }
            finally
            {
                _isInstallingCli = false;
                RefreshAllSections(refreshSkillInstallState: true);
            }
        }

        private async void HandleInstallSkills()
        {
            if (!CliInstallationDetector.IsCliInstalled())
            {
                EditorUtility.DisplayDialog(
                    "CLI Not Found",
                    "uloop-cli is not installed. Please install the CLI first.",
                    "OK");
                return;
            }

            CancelSkillInstallStateRefresh();
            _isInstallingSkills = true;
            RefreshCliSetupSection();

            try
            {
                SkillsTargetSelection selection = SkillsTargetSelectionResolver.Resolve(
                    _skillsTarget,
                    !_installSkillsFlat);
                ToolSkillSynchronizer.SkillTargetInfo target = new(
                    selection.DisplayName,
                    selection.DirectoryName,
                    selection.InstallFlag,
                    hasSkillsDirectory: true,
                    hasExistingSkills: false);
                await ToolSkillSynchronizer.InstallSkillFiles(
                    new List<ToolSkillSynchronizer.SkillTargetInfo> { target },
                    !_installSkillsFlat);
                EditorDialogHelper.ShowSkillsInstalledDialog();
            }
            finally
            {
                _isInstallingSkills = false;
                RefreshSelectedTargetInstallStateFast();
                RefreshSelectedTargetInstallStateInBackground();
                RefreshCliSetupSection();
            }
        }

        private void HandleGroupSkillsChanged(bool groupSkillsUnderUnityCliLoop)
        {
            _installSkillsFlat = !groupSkillsUnderUnityCliLoop;
            McpEditorSettings.SetInstallSkillsFlat(_installSkillsFlat);
            RefreshSelectedTargetInstallStateFast();
            RefreshSelectedTargetInstallStateInBackground();
        }

        private void HandleRefreshSkillsState()
        {
            RefreshSelectedTargetInstallStateFast();
            RefreshSelectedTargetInstallStateInBackground();
        }

    }
}
