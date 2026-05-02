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
        private const bool ForceFlatSkillInstall = true;
        private const double DeferredInitialRefreshDelaySeconds = 0.05;
        private const double ToolSettingsRegistryWarmupInitialDelaySeconds = 0.05;
        private const double ToolSettingsRegistryWarmupMaxDelaySeconds = 0.8;
        private const int ToolSettingsRegistryWarmupMaxAttempts = 5;

        private McpEditorWindowUI _view;
        private McpEditorModel _model;
        private McpEditorWindowEventHandler _eventHandler;

        private SkillsTarget _skillsTarget = SkillsTarget.Claude;
        private bool _installSkillsFlat;
        private bool _isInstallingCli;
        private bool _isInstallingSkills;
        private bool _isRefreshingVersion;
        private bool _isToolSettingsCatalogDirty = true;
        private bool _isDeferredInitialRefreshScheduled;
        private bool _hasCompletedDeferredInitialRefresh;
        private double _deferredInitialRefreshDueTime;
        private bool _isToolSettingsRegistryWarmupScheduled;
        private double _toolSettingsRegistryWarmupDueTime;
        private int _toolSettingsRegistryWarmupAttemptCount;
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
            CancelDeferredInitialRefresh();
            CancelToolSettingsRegistryWarmup();
            ResetToolSettingsRegistryWarmupAttemptCount();
            CancelSkillInstallStateRefresh();
            _view?.Dispose();
            _view = null;
        }

        private void CreateGUI()
        {
            InitializeView();
            RefreshAllSections(refreshMode: McpEditorWindowRefreshMode.InitialPaint);
            ScheduleDeferredInitialRefresh();
        }

        private void InitializeAll()
        {
            InitializeModel();
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
            _view.OnToolSettingsFoldoutChanged += UpdateShowToolSettings;
            _view.OnToolToggled += HandleToolToggled;
            _view.OnSecurityLevelChanged += UpdateDynamicCodeSecurityLevel;
        }

        public IEnumerable<ConnectedClient> GetConnectedToolsAsClients()
        {
            return ConnectedToolsMonitoringService.GetConnectedToolsAsClients();
        }

        private void InitializeEventHandler()
        {
            _eventHandler = new McpEditorWindowEventHandler(_model, this);
            _eventHandler.Initialize();
        }

        private void LoadSavedSettings()
        {
            _model.LoadFromSettings();
            _installSkillsFlat = ForceFlatSkillInstall;
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
                return;
            }

            // McpServerController.[InitializeOnLoad] handles automatic server recovery via RestoreServerStateIfNeeded()
        }

        private void OnDisable()
        {
            CancelDeferredInitialRefresh();
            CancelToolSettingsRegistryWarmup();
            ResetToolSettingsRegistryWarmupAttemptCount();
            CancelSkillInstallStateRefresh();
            CleanupEventHandler();
            SaveSessionState();
            _view?.Dispose();
            _view = null;
        }

        private void CleanupEventHandler()
        {
            _eventHandler?.Cleanup();
        }

        private void SaveSessionState()
        {
            _model.SaveToSessionState();
        }

        private void ScheduleDeferredInitialRefresh()
        {
            if (_isDeferredInitialRefreshScheduled)
            {
                return;
            }

            _isDeferredInitialRefreshScheduled = true;
            _deferredInitialRefreshDueTime = EditorApplication.timeSinceStartup + DeferredInitialRefreshDelaySeconds;
            EditorApplication.update += RunDeferredInitialRefreshWhenDue;
        }

        private void RunDeferredInitialRefreshWhenDue()
        {
            if (EditorApplication.timeSinceStartup < _deferredInitialRefreshDueTime)
            {
                return;
            }

            CancelDeferredInitialRefresh();
            if (_view == null)
            {
                return;
            }

            _hasCompletedDeferredInitialRefresh = true;
            _selectedTargetInstallState = SkillInstallState.Checking;
            ApplyFlatSkillInstallPreference();
            RefreshAllSections(
                refreshSkillInstallState: false,
                refreshMode: McpEditorWindowRefreshMode.Full);
            RefreshSelectedTargetInstallStateInBackground();
        }

        private void CancelDeferredInitialRefresh()
        {
            if (!_isDeferredInitialRefreshScheduled)
            {
                return;
            }

            EditorApplication.update -= RunDeferredInitialRefreshWhenDue;
            _isDeferredInitialRefreshScheduled = false;
        }

        private void OnFocus()
        {
            if (!_hasCompletedDeferredInitialRefresh)
            {
                RefreshAllSections(refreshMode: McpEditorWindowRefreshMode.InitialPaint);
            }

            ScheduleDeferredInitialRefresh();
        }

        internal void RefreshAllSections(
            bool refreshSkillInstallState = false,
            McpEditorWindowRefreshMode refreshMode = McpEditorWindowRefreshMode.Full)
        {
            if (_view == null)
            {
                return;
            }

            bool runExpensiveChecks = McpEditorWindowRefreshPolicy.ShouldRunExpensiveChecks(refreshMode);

            _view.UpdateConfigurationFoldout(_model.UI.ShowConfiguration);

            if (McpEditorWindowRefreshPolicy.ShouldRefreshSkillInstallState(refreshMode, refreshSkillInstallState))
            {
                RefreshSelectedTargetInstallStateFast();
            }

            if (runExpensiveChecks)
            {
                RefreshCliVersionInBackground();
                if (refreshSkillInstallState)
                {
                    RefreshSelectedTargetInstallStateInBackground();
                }
            }
            RefreshCliSetupSection(runExpensiveChecks);

            ConnectedToolsData toolsData = CreateConnectedToolsData();
            _view.UpdateConnectedTools(toolsData);

            RefreshToolSettingsHeader();
            if (runExpensiveChecks)
            {
                RefreshToolSettingsCatalogIfNeeded();
            }
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

        public void InvalidateToolSettingsCatalog()
        {
            _isToolSettingsCatalogDirty = true;
        }

        private void RefreshToolSettingsHeader()
        {
            ToolSettingsSectionData toolSettingsData = CreateToolSettingsHeaderData();
            _view.UpdateToolSettings(toolSettingsData);
        }

        private void RefreshToolSettingsCatalog()
        {
            ToolSettingsSectionData toolSettingsData = CreateToolSettingsData();
            _view.UpdateToolSettings(toolSettingsData);

            if (McpEditorWindowRefreshPolicy.ShouldKeepToolSettingsCatalogDirty(toolSettingsData))
            {
                if (ScheduleToolSettingsRegistryWarmup())
                {
                    _isToolSettingsCatalogDirty = true;
                    return;
                }

                _isToolSettingsCatalogDirty = false;
                return;
            }

            CancelToolSettingsRegistryWarmup();
            ResetToolSettingsRegistryWarmupAttemptCount();
            _isToolSettingsCatalogDirty = false;
        }

        private void RefreshToolSettingsCatalogIfNeeded()
        {
            if (!_model.UI.ShowToolSettings || !_isToolSettingsCatalogDirty)
            {
                return;
            }

            if (_view == null)
            {
                return;
            }

            RefreshToolSettingsCatalog();
        }

        private ToolSettingsSectionData CreateToolSettingsHeaderData()
        {
            return new ToolSettingsSectionData(
                _model.UI.ShowToolSettings,
                ULoopSettings.GetDynamicCodeSecurityLevel(),
                System.Array.Empty<ToolToggleItem>(),
                System.Array.Empty<ToolToggleItem>(),
                true,
                false);
        }

        private ToolSettingsSectionData CreateToolSettingsData()
        {
            UnityToolRegistry registry = CustomToolManager.TryGetRegistry();
            if (registry == null)
            {
                return new ToolSettingsSectionData(
                    _model.UI.ShowToolSettings,
                    ULoopSettings.GetDynamicCodeSecurityLevel(),
                    System.Array.Empty<ToolToggleItem>(),
                    System.Array.Empty<ToolToggleItem>(),
                    false,
                    true);
            }

            ToolSettingsCatalogItem[] allTools = registry.GetToolSettingsCatalog();

            System.Collections.Generic.List<ToolToggleItem> builtIn = new();
            System.Collections.Generic.List<ToolToggleItem> thirdParty = new();

            foreach (ToolSettingsCatalogItem tool in allTools)
            {
                if (tool.DisplayDevelopmentOnly)
                {
                    continue;
                }

                bool isEnabled = ToolSettings.IsToolEnabled(tool.Name);
                bool isThirdPartyTool = tool.IsThirdParty;

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
                ULoopSettings.GetDynamicCodeSecurityLevel(),
                builtIn.ToArray(),
                thirdParty.ToArray(),
                true,
                true);
        }

        private void UpdateShowToolSettings(bool show)
        {
            _model.UpdateShowToolSettings(show);
            RefreshToolSettingsHeader();

            if (!show)
            {
                _isToolSettingsCatalogDirty = true;
                CancelToolSettingsRegistryWarmup();
                ResetToolSettingsRegistryWarmupAttemptCount();
                return;
            }

            RefreshToolSettingsCatalogIfNeeded();
        }

        private bool ScheduleToolSettingsRegistryWarmup()
        {
            if (McpEditorWindowRefreshPolicy.ShouldStartToolSettingsRegistryWarmup(
                    _isToolSettingsRegistryWarmupScheduled,
                    _toolSettingsRegistryWarmupAttemptCount,
                    ToolSettingsRegistryWarmupMaxAttempts))
            {
                double delaySeconds = McpEditorWindowRefreshPolicy.CalculateToolSettingsRegistryWarmupDelaySeconds(
                    ToolSettingsRegistryWarmupInitialDelaySeconds,
                    ToolSettingsRegistryWarmupMaxDelaySeconds,
                    _toolSettingsRegistryWarmupAttemptCount);

                _isToolSettingsRegistryWarmupScheduled = true;
                _toolSettingsRegistryWarmupDueTime = EditorApplication.timeSinceStartup + delaySeconds;
                _toolSettingsRegistryWarmupAttemptCount++;
                EditorApplication.update += RunToolSettingsRegistryWarmupWhenDue;
                return true;
            }

            return _isToolSettingsRegistryWarmupScheduled;
        }

        private void RunToolSettingsRegistryWarmupWhenDue()
        {
            if (EditorApplication.timeSinceStartup < _toolSettingsRegistryWarmupDueTime)
            {
                return;
            }

            CancelToolSettingsRegistryWarmup();

            if (_view == null || !_model.UI.ShowToolSettings)
            {
                ResetToolSettingsRegistryWarmupAttemptCount();
                return;
            }

            CustomToolManager.WarmupRegistry();
            InvalidateToolSettingsCatalog();
            RefreshToolSettingsCatalogIfNeeded();
        }

        private void CancelToolSettingsRegistryWarmup()
        {
            if (!_isToolSettingsRegistryWarmupScheduled)
            {
                return;
            }

            EditorApplication.update -= RunToolSettingsRegistryWarmupWhenDue;
            _isToolSettingsRegistryWarmupScheduled = false;
        }

        private void ResetToolSettingsRegistryWarmupAttemptCount()
        {
            _toolSettingsRegistryWarmupAttemptCount = 0;
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
                await ToolSkillSynchronizer.InstallSkillFilesForTool(toolName, !_installSkillsFlat);

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

        private void UpdateShowConnectedTools(bool show)
        {
            _model.UpdateShowConnectedTools(show);
        }

        private void UpdateShowConfiguration(bool show)
        {
            _model.UpdateShowConfiguration(show);
        }

        private void UpdateDynamicCodeSecurityLevel(DynamicCodeSecurityLevel level)
        {
            ULoopSettings.SetDynamicCodeSecurityLevel(level);
        }

        private void RefreshCliSetupSection(bool includeSkillDirectoryChecks = true)
        {
            if (_view == null)
            {
                return;
            }

            CliSetupData cliData = CreateCliSetupData(includeSkillDirectoryChecks);
            _view.UpdateCliSetup(cliData);
        }

        private CliSetupData CreateCliSetupData(bool includeSkillDirectoryChecks = true)
        {
            string cliVersion = CliInstallationDetector.GetCachedCliVersion();
            string packageVersion = McpConstants.PackageInfo.version;
            string projectRoot = UnityMcpPathResolver.GetProjectRoot();
            CliInstallResult projectLocalResult = ProjectLocalCliAutoInstaller.EnsureProjectLocalCliCurrent(
                projectRoot,
                packageVersion);
            if (!projectLocalResult.Success)
            {
                Debug.LogWarning(
                    $"[{McpConstants.PROJECT_NAME}] Failed to update project-local uLoop CLI: {projectLocalResult.ErrorOutput}");
            }

            bool isCliInstalled = cliVersion != null;
            bool isChecking = !CliInstallationDetector.IsCheckCompleted()
                || _isRefreshingVersion
                || !includeSkillDirectoryChecks;
            bool needsUpdate = cliVersion != null
                && CliVersionComparer.IsVersionLessThan(cliVersion, packageVersion);
            bool needsDowngrade = false;
            bool groupSkillsUnderUnityCliLoop = !_installSkillsFlat;
            SkillInstallState selectedTargetInstallState = includeSkillDirectoryChecks
                ? _selectedTargetInstallState
                : SkillInstallState.Checking;

            return new CliSetupData(
                isCliInstalled,
                cliVersion,
                packageVersion,
                needsUpdate,
                needsDowngrade,
                _isInstallingCli,
                isChecking,
                isClaudeSkillsInstalled: false,
                isAgentsSkillsInstalled: false,
                isCursorSkillsInstalled: false,
                isGeminiSkillsInstalled: false,
                isCodexSkillsInstalled: false,
                isAntigravitySkillsInstalled: false,
                selectedTargetInstallState,
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
            bool wasCliInstalledBeforeInstall = CliInstallationDetector.IsCliInstalled();
            _isInstallingCli = true;
            RefreshCliSetupSection();

            try
            {
                CliInstallResult result = await NativeCliInstaller.InstallAsync(
                    Application.platform,
                    McpConstants.PackageInfo.version);

                if (!result.Success)
                {
                    NativeCliInstallCommand command = NativeCliInstaller.GetInstallCommand(
                        Application.platform,
                        McpConstants.PackageInfo.version);
                    EditorUtility.DisplayDialog(
                        "Installation Failed",
                        $"Failed to install uLoop CLI.\n\n{result.ErrorOutput}\n\nYou can try manually:\n{command.ManualCommand}",
                        "OK");
                    return;
                }
            }
            finally
            {
                _isInstallingCli = false;
                RefreshAllSections(
                    refreshSkillInstallState:
                    CliInstallRefreshPolicy.ShouldRefreshSkillsAfterCliInstall(wasCliInstalledBeforeInstall));
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
            ApplyFlatSkillInstallPreference();
            RefreshSelectedTargetInstallStateFast();
            RefreshSelectedTargetInstallStateInBackground();
        }

        private void ApplyFlatSkillInstallPreference()
        {
            // Claude Code does not resolve nested skill folders, so editor-driven installs stay flat for every target.
            _installSkillsFlat = ForceFlatSkillInstall;
            McpEditorSettings.SetInstallSkillsFlat(_installSkillsFlat);
        }

        private void HandleRefreshSkillsState()
        {
            RefreshSelectedTargetInstallStateFast();
            RefreshSelectedTargetInstallStateInBackground();
        }

    }
}
