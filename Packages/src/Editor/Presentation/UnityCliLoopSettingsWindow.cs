using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// Defines the Unity Editor window for Unity CLI Loop Settings workflows.
    /// </summary>
    public class UnityCliLoopSettingsWindow : EditorWindow
    {
        private const bool ForceFlatSkillInstall = true;
        private const double DeferredInitialRefreshDelaySeconds = 0.05;
        private const double ToolSettingsRegistryWarmupInitialDelaySeconds = 0.05;
        private const double ToolSettingsRegistryWarmupMaxDelaySeconds = 0.8;
        private const int ToolSettingsRegistryWarmupMaxAttempts = 5;

        private UnityCliLoopSettingsWindowUI _view;
        private UnityCliLoopSettingsModel _model;
        private UnityCliLoopSettingsWindowEventHandler _eventHandler;

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
            UnityCliLoopSettingsWindow window = GetWindow<UnityCliLoopSettingsWindow>("Unity CLI Loop");
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
            RefreshAllSections(refreshMode: UnityCliLoopSettingsWindowRefreshMode.InitialPaint);
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
            _model = new UnityCliLoopSettingsModel();
        }

        private void InitializeView()
        {
            _view = new UnityCliLoopSettingsWindowUI(rootVisualElement);
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
            _view.OnToolSettingsFoldoutChanged += UpdateShowToolSettings;
            _view.OnToolToggled += HandleToolToggled;
            _view.OnSecurityLevelChanged += UpdateDynamicCodeSecurityLevel;
        }

        private void InitializeEventHandler()
        {
            _eventHandler = new UnityCliLoopSettingsWindowEventHandler(_model, this);
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
            UnityCliLoopEditorSettings.SetShowReconnectingUI(false);

            Task recoveryTask = UnityCliLoopServerApplicationFacade.RecoveryTask;
            if (recoveryTask != null && !recoveryTask.IsCompleted)
            {
                await recoveryTask;
            }

            bool isAfterCompile = UnityCliLoopEditorSettings.GetIsAfterCompile();

            if (isAfterCompile)
            {
                UnityCliLoopEditorSettings.ClearAfterCompileFlag();
                return;
            }

            // The server lifecycle owns automatic recovery after domain reload.
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
                refreshMode: UnityCliLoopSettingsWindowRefreshMode.Full);
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
                RefreshAllSections(refreshMode: UnityCliLoopSettingsWindowRefreshMode.InitialPaint);
            }

            ScheduleDeferredInitialRefresh();
        }

        internal void RefreshAllSections(
            bool refreshSkillInstallState = false,
            UnityCliLoopSettingsWindowRefreshMode refreshMode = UnityCliLoopSettingsWindowRefreshMode.Full)
        {
            if (_view == null)
            {
                return;
            }

            bool runExpensiveChecks = UnityCliLoopSettingsWindowRefreshPolicy.ShouldRunExpensiveChecks(refreshMode);

            _view.UpdateConfigurationFoldout(_model.UI.ShowConfiguration);

            if (UnityCliLoopSettingsWindowRefreshPolicy.ShouldRefreshSkillInstallState(refreshMode, refreshSkillInstallState))
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

            RefreshToolSettingsHeader();
            if (runExpensiveChecks)
            {
                RefreshToolSettingsCatalogIfNeeded();
            }
        }

        private async void RefreshCliVersionInBackground()
        {
            if (CliSetupApplicationFacade.IsCliCheckCompleted())
            {
                return;
            }

            await CliSetupApplicationFacade.RefreshCliVersionAsync(CancellationToken.None);
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
                Task forceRefresh = CliSetupApplicationFacade.ForceRefreshCliVersionAsync(CancellationToken.None);
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

            if (UnityCliLoopSettingsWindowRefreshPolicy.ShouldKeepToolSettingsCatalogDirty(toolSettingsData))
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
                ToolSettingsApplicationFacade.GetDynamicCodeSecurityLevel(),
                System.Array.Empty<ToolToggleItem>(),
                System.Array.Empty<ToolToggleItem>(),
                true,
                false);
        }

        private ToolSettingsSectionData CreateToolSettingsData()
        {
            bool isRegistryAvailable =
                ToolSettingsApplicationFacade.TryGetToolCatalog(
                    out ToolSettingsApplicationFacade.ToolCatalogItem[] allTools);
            if (!isRegistryAvailable)
            {
                return new ToolSettingsSectionData(
                    _model.UI.ShowToolSettings,
                    ToolSettingsApplicationFacade.GetDynamicCodeSecurityLevel(),
                    System.Array.Empty<ToolToggleItem>(),
                    System.Array.Empty<ToolToggleItem>(),
                    false,
                    true);
            }

            List<ToolToggleItem> builtIn = new();
            List<ToolToggleItem> thirdParty = new();

            foreach (ToolSettingsApplicationFacade.ToolCatalogItem tool in allTools)
            {
                if (tool.DisplayDevelopmentOnly)
                {
                    continue;
                }

                bool isEnabled = ToolSettingsApplicationFacade.IsToolEnabled(tool.Name);
                bool isThirdPartyTool = tool.IsThirdParty;

                ToolToggleItem item = new(tool.Name, isEnabled, isThirdPartyTool);
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
                ToolSettingsApplicationFacade.GetDynamicCodeSecurityLevel(),
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
            if (UnityCliLoopSettingsWindowRefreshPolicy.ShouldStartToolSettingsRegistryWarmup(
                    _isToolSettingsRegistryWarmupScheduled,
                    _toolSettingsRegistryWarmupAttemptCount,
                    ToolSettingsRegistryWarmupMaxAttempts))
            {
                double delaySeconds = UnityCliLoopSettingsWindowRefreshPolicy.CalculateToolSettingsRegistryWarmupDelaySeconds(
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

            ToolSettingsApplicationFacade.WarmupRegistry();
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
            if (!enabled)
            {
                SkillSetupApplicationFacade.RemoveSkillFiles(toolName);
            }
            else
            {
                await SkillSetupApplicationFacade.InstallSkillFilesForToolAsync(
                    toolName,
                    !_installSkillsFlat,
                    CancellationToken.None);

                if (!SkillSetupApplicationFacade.IsSkillInstalled(toolName))
                {
                    Debug.LogWarning(
                        $"[UnityCliLoop] Skill for '{toolName}' was not installed after enabling. " +
                        "The skill source may have an incorrect directory structure " +
                        "(expected: <ToolDir>/Skill/SKILL.md). Run 'uloop skills list' for details."
                    );
                }
            }
        }

        private void UpdateShowConfiguration(bool show)
        {
            _model.UpdateShowConfiguration(show);
        }

        private void UpdateDynamicCodeSecurityLevel(DynamicCodeSecurityLevel level)
        {
            ToolSettingsApplicationFacade.SetDynamicCodeSecurityLevel(level);
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
            string cliVersion = CliSetupApplicationFacade.GetCachedCliVersion();
            string cliExecutablePath = CliSetupApplicationFacade.GetCachedCliExecutablePath();
            string packageVersion = UnityCliLoopConstants.PackageInfo.version;
            string requiredDispatcherVersion = GetRequiredDispatcherVersion();
            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            CliInstallResult projectLocalResult = CliSetupApplicationFacade.EnsureProjectLocalCliCurrent(
                projectRoot,
                packageVersion);
            if (!projectLocalResult.Success)
            {
                Debug.LogWarning(
                    $"[{UnityCliLoopConstants.PROJECT_NAME}] Failed to update project-local uLoop CLI: {projectLocalResult.ErrorOutput}");
            }

            bool isCliInstalled = cliVersion != null;
            bool canUninstallCli = CliSetupApplicationFacade.IsPackageOwnedCurrentUserInstallPath(
                cliExecutablePath,
                UnityEngine.Application.platform);
            bool isChecking = !CliSetupApplicationFacade.IsCliCheckCompleted()
                || _isRefreshingVersion
                || !includeSkillDirectoryChecks;
            bool needsUpdate = IsCliUpdateNeeded(cliVersion, requiredDispatcherVersion);
            bool needsDowngrade = false;
            bool groupSkillsUnderUnityCliLoop = !_installSkillsFlat;
            SkillInstallState selectedTargetInstallState = includeSkillDirectoryChecks
                ? _selectedTargetInstallState
                : SkillInstallState.Checking;

            return new CliSetupData(
                isCliInstalled,
                cliVersion,
                requiredDispatcherVersion,
                needsUpdate,
                needsDowngrade,
                canUninstallCli,
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

        private static string GetRequiredDispatcherVersion()
        {
            string requiredDispatcherVersion = CliSetupApplicationFacade.GetRequiredDispatcherVersion(UnityCliLoopConstants.PackageInfo.version);
            return string.IsNullOrEmpty(requiredDispatcherVersion)
                ? UnityCliLoopConstants.PackageInfo.version
                : requiredDispatcherVersion;
        }

        private void RefreshSelectedTargetInstallStateFast()
        {
            if (!CliSetupApplicationFacade.IsCliInstalled())
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
            if (!CliSetupApplicationFacade.IsCliInstalled() || _isRefreshingVersion || _isInstallingSkills)
            {
                return;
            }

            CancellationTokenSource cts = new();
            _skillInstallStateRefreshCts = cts;
            RefreshSelectedTargetInstallStateAsync(cts.Token);
        }

        private async void RefreshSelectedTargetInstallStateAsync(CancellationToken ct)
        {
            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
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
            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            return GetSelectedTargetInstallState(projectRoot, includeFreshnessCheck);
        }

        private SkillInstallState GetSelectedTargetInstallState(
            string projectRoot,
            bool includeFreshnessCheck)
        {
            SkillsTargetSelection selection = SkillsTargetSelectionResolver.Resolve(
                _skillsTarget,
                !_installSkillsFlat);
            List<SkillSetupApplicationFacade.SkillTargetInfo> targets = includeFreshnessCheck
                ? SkillSetupApplicationFacade.DetectSkillTargetsForLayoutAtProjectRoot(projectRoot, !_installSkillsFlat)
                : SkillSetupApplicationFacade.DetectSkillTargetsForLayoutFastAtProjectRoot(projectRoot, !_installSkillsFlat);
            SkillSetupApplicationFacade.SkillTargetInfo targetInfo = targets
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
            if (ShouldUninstallCliFromPrimaryButton())
            {
                await HandleUninstallCli();
                return;
            }

            bool wasCliInstalledBeforeInstall = CliSetupApplicationFacade.IsCliInstalled();
            _isInstallingCli = true;
            RefreshCliSetupSection();

            try
            {
                CliInstallResult result = await CliSetupApplicationFacade.InstallGlobalCliAsync(
                    UnityEngine.Application.platform,
                    UnityCliLoopConstants.PackageInfo.version,
                    CancellationToken.None);

                if (!result.Success)
                {
                    NativeCliInstallCommand command = CliSetupApplicationFacade.GetGlobalCliInstallCommand(
                        UnityEngine.Application.platform,
                        UnityCliLoopConstants.PackageInfo.version,
                        true);
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

        private bool ShouldUninstallCliFromPrimaryButton()
        {
            string cliVersion = CliSetupApplicationFacade.GetCachedCliVersion();
            string cliExecutablePath = CliSetupApplicationFacade.GetCachedCliExecutablePath();
            bool canUninstallCli = CliSetupApplicationFacade.IsPackageOwnedCurrentUserInstallPath(
                cliExecutablePath,
                UnityEngine.Application.platform);
            return ShouldUninstallCliFromPrimaryButton(
                cliVersion,
                GetRequiredDispatcherVersion(),
                canUninstallCli);
        }

        internal static bool ShouldUninstallCliFromPrimaryButton(
            string cliVersion,
            string requiredDispatcherVersion,
            bool canUninstallCli)
        {
            bool isCliInstalled = cliVersion != null;
            bool needsUpdate = IsCliUpdateNeeded(cliVersion, requiredDispatcherVersion);
            return CliSetupSection.IsUninstallCliAction(isCliInstalled, needsUpdate, needsDowngrade: false, canUninstallCli);
        }

        internal static bool IsCliUpdateNeeded(string cliVersion, string requiredDispatcherVersion)
        {
            return cliVersion != null
                && CliSetupApplicationFacade.IsCliVersionLessThan(cliVersion, requiredDispatcherVersion);
        }

        private async Task HandleUninstallCli()
        {
            if (!CliUninstallPrompt.ConfirmUninstall())
            {
                return;
            }

            _isInstallingCli = true;
            RefreshCliSetupSection();

            try
            {
                CliInstallResult result = await CliSetupApplicationFacade.UninstallGlobalCliAsync(
                    UnityEngine.Application.platform,
                    CancellationToken.None);
                if (!result.Success)
                {
                    EditorUtility.DisplayDialog(
                        "Uninstallation Failed",
                        $"Failed to uninstall uLoop CLI.\n\n{result.ErrorOutput}",
                        "OK");
                    return;
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
            if (!CliSetupApplicationFacade.IsCliInstalled())
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
                SkillSetupApplicationFacade.SkillTargetInfo target = new(
                    selection.DisplayName,
                    selection.DirectoryName,
                    selection.InstallFlag,
                    hasSkillsDirectory: true,
                    hasExistingSkills: false);
                await SkillSetupApplicationFacade.InstallSkillFilesAsync(
                    new List<SkillSetupApplicationFacade.SkillTargetInfo> { target },
                    !_installSkillsFlat,
                    CancellationToken.None);
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
            UnityCliLoopEditorSettings.SetInstallSkillsFlat(_installSkillsFlat);
        }

        private void HandleRefreshSkillsState()
        {
            RefreshSelectedTargetInstallStateFast();
            RefreshSelectedTargetInstallStateInBackground();
        }

    }
}
