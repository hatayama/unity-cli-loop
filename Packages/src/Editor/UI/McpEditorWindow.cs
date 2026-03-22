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
        private McpServerOperations _serverOperations;
        private IEnumerable<ConnectedClient> _cachedStoredTools;
        private float _lastStoredToolsUpdateTime;

        private SkillsTarget _skillsTarget = SkillsTarget.Claude;
        private bool _isInstallingCli;
        private bool _isInstallingSkills;
        private bool _isRefreshingVersion;

        [MenuItem("Window/Unity CLI Loop")]
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
            _view?.Dispose();
        }

        private void CreateGUI()
        {
            InitializeView();
            RefreshAllSections();
        }

        private void InitializeAll()
        {
            InitializeModel();
            InitializeConfigurationServices();
            InitializeEventHandler();
            InitializeServerOperations();
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
            _view.OnToggleServer += ToggleServer;
            _view.OnPortChanged += UpdateCustomPort;
            _view.OnRefreshCliVersion += HandleRefreshCliVersion;
            _view.OnInstallCli += HandleInstallCli;
            _view.OnInstallSkills += HandleInstallSkills;
            _view.OnSkillsTargetChanged += value => { _skillsTarget = value; RefreshCliSetupSection(); };
            _view.OnConfigurationFoldoutChanged += UpdateShowConfiguration;
            _view.OnConnectedToolsFoldoutChanged += UpdateShowConnectedTools;
            _view.OnEditorTypeChanged += UpdateSelectedEditorType;
            _view.OnRepositoryRootChanged += UpdateAddRepositoryRoot;
            _view.OnConfigureClicked += ConfigureEditor;
            _view.OnDeleteConfigClicked += DeleteEditorConfiguration;
            _view.OnOpenSettingsClicked += OpenConfigurationFile;
            _view.OnToolSettingsFoldoutChanged += UpdateShowToolSettings;
            _view.OnToolToggled += HandleToolToggled;
            _view.OnSecurityFoldoutChanged += UpdateShowSecuritySettings;
            _view.OnEnableTestsChanged += UpdateEnableTestsExecution;
            _view.OnAllowMenuChanged += UpdateAllowMenuItemExecution;
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

        private void InitializeServerOperations()
        {
            _serverOperations = new McpServerOperations(_model, _eventHandler);
        }

        private void LoadSavedSettings()
        {
            _model.LoadFromSettings();

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

            bool wasRunning = McpEditorSettings.GetIsServerRunning();
            bool serverNotRunning = !McpServerController.IsServerRunning;
            bool isRecoveryInProgress = McpServerController.IsStartupProtectionActive();
            bool shouldStartServer = wasRunning && serverNotRunning && !isRecoveryInProgress;

            if (shouldStartServer)
            {
                _serverOperations.StartServerInternal();
            }
        }

        private void OnDisable()
        {
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

        public void RefreshAllSections()
        {
            if (_view == null)
            {
                return;
            }

            SyncPortSettings();

            ServerStatusData statusData = CreateServerStatusData();
            _view.UpdateServerStatus(statusData);

            ConnectionModeData modeData = new ConnectionModeData(_model.UI.ConnectionMode);
            _view.UpdateConnectionMode(modeData);
            _view.UpdateConfigurationFoldout(_model.UI.ShowConfiguration);
            _view.UpdateSectionVisibility(_model.UI.ConnectionMode);

            ServerControlsData controlsData = CreateServerControlsData();
            _view.UpdateServerControls(controlsData);

            RefreshCliSetupSection();
            RefreshCliVersionInBackground();

            ConnectedToolsData toolsData = CreateConnectedToolsData();
            _view.UpdateConnectedTools(toolsData);

            EditorConfigData configData = CreateEditorConfigData();
            _view.UpdateEditorConfig(configData);

            ToolSettingsSectionData toolSettingsData = CreateToolSettingsData();
            _view.UpdateToolSettings(toolSettingsData);

            SecuritySettingsData securityData = CreateSecuritySettingsData();
            _view.UpdateSecuritySettings(securityData);
        }

        private async void RefreshCliVersionInBackground()
        {
            if (CliInstallationDetector.IsCheckCompleted())
            {
                return;
            }

            await CliInstallationDetector.RefreshCliVersionAsync(CancellationToken.None);
            RefreshCliSetupSection();
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

        private void SyncPortSettings()
        {
            bool serverIsRunning = McpServerController.IsServerRunning;

            if (serverIsRunning)
            {
                int actualServerPort = McpServerController.ServerPort;
                bool portMismatch = _model.UI.CustomPort != actualServerPort;

                if (portMismatch)
                {
                    _model.UpdateCustomPort(actualServerPort);
                }
            }
        }

        private ServerStatusData CreateServerStatusData()
        {
            (bool isRunning, int port, bool _) = McpServerController.GetServerStatus();
            string status = isRunning ? "Running" : "Stopped";
            Color statusColor = isRunning ? Color.green : Color.red;

            return new ServerStatusData(isRunning, port, status, statusColor);
        }

        private ServerControlsData CreateServerControlsData()
        {
            bool isRunning = McpServerController.IsServerRunning;

            bool hasPortWarning = false;
            string portWarningMessage = null;

            if (!isRunning)
            {
                int requestedPort = _model.UI.CustomPort;

                if (!McpPortValidator.ValidatePort(requestedPort))
                {
                    hasPortWarning = true;
                    portWarningMessage = $"Port {requestedPort} is invalid. Port must be 1024 or higher and not a reserved system port.";
                }
                else if (NetworkUtility.IsPortInUse(requestedPort))
                {
                    hasPortWarning = true;
                    portWarningMessage = $"Port {requestedPort} is already in use. Please choose a different port or stop the other process using this port.";
                }
            }

            return new ServerControlsData(_model.UI.CustomPort, isRunning, !isRunning, hasPortWarning, portWarningMessage);
        }

        private IEnumerable<ConnectedClient> GetCachedStoredTools()
        {
            const float cacheDuration = 0.1f;
            float currentTime = Time.realtimeSinceStartup;

            if (_cachedStoredTools == null || (currentTime - _lastStoredToolsUpdateTime) > cacheDuration)
            {
                _cachedStoredTools = GetConnectedToolsAsClients();
                _lastStoredToolsUpdateTime = currentTime;
            }

            return _cachedStoredTools;
        }

        private void InvalidateStoredToolsCache()
        {
            _cachedStoredTools = null;
        }

        private ConnectedToolsData CreateConnectedToolsData()
        {
            bool isServerRunning = McpServerController.IsServerRunning;
            IReadOnlyCollection<ConnectedClient> connectedClients = McpServerController.CurrentServer?.GetConnectedClients();

            bool showReconnectingUIFlag = McpEditorSettings.GetShowReconnectingUI();
            bool showPostCompileUIFlag = McpEditorSettings.GetShowPostCompileReconnectingUI();

            bool hasNamedClients = connectedClients != null &&
                                   connectedClients.Any(client => client.ClientName != McpConstants.UNKNOWN_CLIENT_NAME);

            IEnumerable<ConnectedClient> storedTools = GetCachedStoredTools();
            bool hasStoredTools = storedTools.Any();

            if (hasStoredTools)
            {
                connectedClients = storedTools.ToList();
                hasNamedClients = true;
            }

            bool showReconnectingUI = !hasStoredTools &&
                                      (showReconnectingUIFlag || showPostCompileUIFlag) &&
                                      !hasNamedClients;

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
                    hasPortMismatch = _model.UI.CustomPort != configuredPort;
                }
            }

            int portToCheck = isServerRunning ? currentPort : _model.UI.CustomPort;
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

        private SecuritySettingsData CreateSecuritySettingsData()
        {
            return new SecuritySettingsData(
                _model.UI.ShowSecuritySettings,
                ULoopSettings.GetEnableTestsExecution(),
                ULoopSettings.GetAllowMenuItemExecution(),
                ULoopSettings.GetAllowThirdPartyTools());
        }

        private ToolSettingsSectionData CreateToolSettingsData()
        {
            UnityToolRegistry registry = CustomToolManager.GetRegistry();
            if (registry == null)
            {
                return new ToolSettingsSectionData(
                    _model.UI.ShowToolSettings,
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
                await ToolSkillSynchronizer.InstallSkillFiles();

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
            int portToUse = isServerRunning ? McpServerController.ServerPort : _model.UI.CustomPort;

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

        private void StartServer()
        {
            if (_serverOperations.StartServer())
            {
                RefreshAllSections();
            }
        }

        private void StopServer()
        {
            _serverOperations.StopServer();
            RefreshAllSections();
        }

        private IMcpConfigService GetConfigService(McpEditorType editorType)
        {
            return _configServiceFactory.GetConfigService(editorType);
        }

        private void UpdateCustomPort(int port)
        {
            _model.UpdateCustomPort(port);
            RefreshAllSections();
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

        private void UpdateShowSecuritySettings(bool show)
        {
            _model.UpdateShowSecuritySettings(show);
        }

        private void UpdateEnableTestsExecution(bool enable)
        {
            _model.UpdateEnableTestsExecution(enable);
        }

        private void UpdateAllowMenuItemExecution(bool allow)
        {
            _model.UpdateAllowMenuItemExecution(allow);
        }

        private void UpdateAllowThirdPartyTools(bool allow)
        {
            _model.UpdateAllowThirdPartyTools(allow);
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
            bool isClaudeInstalled = CliInstallationDetector.AreSkillsInstalled("claude");
            bool isAgentsInstalled = CliInstallationDetector.AreSkillsInstalled("codex");
            bool isCursorInstalled = CliInstallationDetector.AreSkillsInstalled("cursor");
            bool isAntigravityInstalled = CliInstallationDetector.AreSkillsInstalled("antigravity");

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
                isAntigravityInstalled,
                _skillsTarget,
                _isInstallingSkills);
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

            // Windows: npm global prefix often points to admin-only directories (e.g. C:\Program Files\nodejs)
            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                string globalPrefix = NpmInstallDiagnostics.GetGlobalPrefix(npmPath);
                if (!string.IsNullOrEmpty(globalPrefix) && !NpmInstallDiagnostics.IsGlobalPrefixWritable(globalPrefix))
                {
                    string manualCommand = $"npm install -g {installTarget}";
                    EditorUtility.DisplayDialog(
                        "Permission Issue Detected",
                        $"npm's global directory ({globalPrefix}) requires elevated permissions.\n\n"
                        + NpmInstallDiagnostics.BuildPermissionSolutions(manualCommand),
                        "OK");
                    return;
                }
            }

            _isInstallingCli = true;
            RefreshCliSetupSection();

            try
            {
                System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = npmPath,
                    Arguments = $"install -g {installTarget}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                NodeEnvironmentResolver.SetupEnvironmentPath(startInfo, NodeEnvironmentResolver.FindNodePath());

                bool success = false;
                string errorOutput = "";

                await Task.Run(() =>
                {
                    System.Diagnostics.Process process = System.Diagnostics.Process.Start(startInfo);
                    if (process == null)
                    {
                        errorOutput = "Failed to start npm process";
                        return;
                    }

                    System.Text.StringBuilder errorBuilder = new System.Text.StringBuilder();
                    process.OutputDataReceived += (s, e) => { };
                    process.ErrorDataReceived += (s, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    if (!process.WaitForExit(30000))
                    {
                        try { process.Kill(); } catch (System.InvalidOperationException) { }
                        process.Dispose();
                        errorOutput = "Installation timed out after 30 seconds";
                        return;
                    }

                    process.WaitForExit();
                    errorOutput = errorBuilder.ToString();
                    success = process.ExitCode == 0;
                    process.Dispose();
                });

                CliInstallationDetector.InvalidateCache();

                if (!success)
                {
                    string manualCommand = $"npm install -g {installTarget}";

                    // Classifier emits Windows-specific remediation with the command embedded;
                    // on other platforms (or unrecognized errors) show raw stderr + manual command footer
                    string guidance = Application.platform == RuntimePlatform.WindowsEditor
                        ? NpmInstallDiagnostics.ClassifyInstallError(errorOutput, manualCommand)
                        : null;

                    string message;
                    if (guidance != null && !string.IsNullOrEmpty(errorOutput))
                    {
                        message = "Failed to install uLoop CLI.\n\n"
                            + NpmInstallDiagnostics.BuildInstallErrorMessage(guidance, errorOutput);
                    }
                    else
                    {
                        message = $"Failed to install uLoop CLI.\n\n{errorOutput}\n\nYou can try manually:\n{manualCommand}";
                    }

                    EditorUtility.DisplayDialog("Installation Failed", message, "OK");
                }
            }
            finally
            {
                _isInstallingCli = false;
                RefreshAllSections();
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

            _isInstallingSkills = true;
            RefreshCliSetupSection();

            try
            {
                string arguments = _skillsTarget switch
                {
                    SkillsTarget.Claude => "skills install --claude",
                    SkillsTarget.Agents => "skills install --codex",
                    SkillsTarget.Cursor => "skills install --cursor",
                    SkillsTarget.Antigravity => "skills install --antigravity",
                    _ => "skills install --claude"
                };

                string uloopPath = NodeEnvironmentResolver.FindExecutablePath(CliConstants.EXECUTABLE_NAME);
                // FindExecutablePath resolves .cmd shims on Windows via 'where' command
                string uloopFileName = uloopPath ?? CliConstants.EXECUTABLE_NAME;

                System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = uloopFileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                NodeEnvironmentResolver.SetupEnvironmentPath(startInfo, NodeEnvironmentResolver.FindNodePath());

                bool success = false;
                string errorOutput = "";

                await Task.Run(() =>
                {
                    System.Diagnostics.Process process = ProcessStartHelper.TryStart(startInfo);
                    if (process == null)
                    {
                        errorOutput = "Failed to start uloop process";
                        return;
                    }

                    System.Text.StringBuilder errorBuilder = new System.Text.StringBuilder();
                    process.OutputDataReceived += (s, e) => { };
                    process.ErrorDataReceived += (s, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    if (!process.WaitForExit(30000))
                    {
                        try { process.Kill(); } catch (System.InvalidOperationException) { }
                        process.Dispose();
                        errorOutput = "Installation timed out after 30 seconds";
                        return;
                    }

                    process.WaitForExit();
                    errorOutput = errorBuilder.ToString();
                    success = process.ExitCode == 0;
                    process.Dispose();
                });

                CliInstallationDetector.InvalidateCache();

                if (success)
                {
#if UNITY_6000_3_OR_NEWER
                    EditorDialog.DisplayAlertDialog("Skills Installed", "Skills have been installed successfully.", "OK", DialogIconType.Info);
#else
                    EditorUtility.DisplayDialog("Skills Installed", "Skills have been installed successfully.", "OK");
#endif
                }
                else
                {
                    EditorUtility.DisplayDialog("Installation Failed", $"Failed to install skills.\n\n{errorOutput}", "OK");
                }
            }
            finally
            {
                _isInstallingSkills = false;
                RefreshAllSections();
            }
        }

        private void ToggleServer()
        {
            if (McpServerController.IsServerRunning)
            {
                StopServer();
            }
            else
            {
                StartServer();
            }
        }
    }
}
