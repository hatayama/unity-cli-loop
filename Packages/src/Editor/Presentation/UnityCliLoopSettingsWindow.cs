using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// Defines the Unity Editor window for Unity CLI Loop Settings workflows.
    /// </summary>
    public class UnityCliLoopSettingsWindow : EditorWindow
    {
        internal enum CliPrimaryButtonAction
        {
            None,
            InstallOrUpdate,
            RepairPath,
            Uninstall
        }

        private const bool ForceFlatSkillInstall = true;
        private const double DeferredInitialRefreshDelaySeconds = 0.05;
        private const double ToolSettingsRegistryWarmupInitialDelaySeconds = 0.05;
        private const double ToolSettingsRegistryWarmupMaxDelaySeconds = 0.8;
        private const int ToolSettingsRegistryWarmupMaxAttempts = 5;

        private static UnityCliLoopEditorSettingsService RegisteredEditorSettingsService;
        private static UnityCliLoopEditorSessionStateService RegisteredSessionStateService;

        private UnityCliLoopSettingsWindowUI _view;
        private UnityCliLoopSettingsModel _model;
        private UnityCliLoopSettingsWindowEventHandler _eventHandler;
        private SkillSetupUseCase _skillSetupUseCase;
        private ToolSettingsUseCase _toolSettingsUseCase;
        private UnityCliLoopEditorSettingsService _editorSettingsService;
        private UnityCliLoopEditorSessionStateService _sessionStateService;

        private SkillsTarget _skillsTarget = SkillsTarget.Claude;
        private bool _installSkillsFlat;
        private bool _isInstallingCli;
        private bool _isInstallingSkills;
        private bool _isRefreshingVersion;
        private bool _isRefreshingCliPathSetup;
        private bool _needsCliPathSetup;
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

        internal static void InitializeEditorServices(
            UnityCliLoopEditorSettingsService editorSettingsService,
            UnityCliLoopEditorSessionStateService sessionStateService)
        {
            System.Diagnostics.Debug.Assert(editorSettingsService != null, "editorSettingsService must not be null");
            System.Diagnostics.Debug.Assert(sessionStateService != null, "sessionStateService must not be null");

            RegisteredEditorSettingsService = editorSettingsService
                ?? throw new ArgumentNullException(nameof(editorSettingsService));
            RegisteredSessionStateService = sessionStateService;
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
            InitializeApplicationServices();
            InitializeModel();
            InitializeEventHandler();
            LoadSavedSettings();
            RestoreSessionState();
            HandlePostCompileMode();
        }

        private void InitializeModel()
        {
            _model = new UnityCliLoopSettingsModel(
                _toolSettingsUseCase,
                _editorSettingsService);
        }

        private void InitializeApplicationServices()
        {
            _skillSetupUseCase = SkillSetupUseCaseRegistry.GetRegisteredUseCase();
            _toolSettingsUseCase = ToolSettingsUseCaseRegistry.GetRegisteredUseCase();
            _editorSettingsService = GetEditorSettingsService();
            _sessionStateService = GetSessionStateService();
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
                RefreshSelectedTargetInstallStateInBackground(allowDuringCliRefresh: true);
            };
            _view.OnGroupSkillsChanged += HandleGroupSkillsChanged;
            _view.OnConfigurationFoldoutChanged += UpdateShowConfiguration;
            _view.OnToolSettingsFoldoutChanged += UpdateShowToolSettings;
            _view.OnToolToggled += HandleToolToggled;
        }

        private void InitializeEventHandler()
        {
            _eventHandler = new UnityCliLoopSettingsWindowEventHandler(
                _model,
                this,
                _toolSettingsUseCase);
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
            _sessionStateService.SetShowReconnectingUI(false);

            Task recoveryTask = UnityCliLoopServerApplicationFacade.RecoveryTask;
            if (recoveryTask != null && !recoveryTask.IsCompleted)
            {
                await recoveryTask;
            }

            bool isAfterCompile = _sessionStateService.GetIsAfterCompile();

            if (isAfterCompile)
            {
                _sessionStateService.ClearAfterCompileFlag();
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
            if (!UnityCliLoopSettingsWindowRefreshPolicy.ShouldScheduleDeferredInitialRefresh(
                    _isDeferredInitialRefreshScheduled,
                    _hasCompletedDeferredInitialRefresh))
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
                RefreshCliPathSetupInBackground();
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
            RefreshCliPathSetupInBackground();
            RefreshCliSetupSection();
            RefreshSelectedTargetInstallStateInBackground();
        }

        private async void RefreshCliPathSetupInBackground()
        {
            if (_isRefreshingCliPathSetup)
            {
                return;
            }

            if (!ShouldCheckCliPathSetup())
            {
                _needsCliPathSetup = false;
                return;
            }

            _isRefreshingCliPathSetup = true;
            RefreshCliSetupSection();

            try
            {
                bool isCliVisibleFromShell = await CliSetupApplicationFacade.IsCliVisibleFromShellAsync(
                    UnityEngine.Application.platform,
                    CancellationToken.None);
                _needsCliPathSetup = !isCliVisibleFromShell;
            }
            finally
            {
                _isRefreshingCliPathSetup = false;
                RefreshCliSetupSection();
            }
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
                RefreshCliPathSetupInBackground();
            }
            finally
            {
                _isRefreshingVersion = false;
                RefreshCliSetupSection();
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
                System.Array.Empty<ToolToggleItem>(),
                System.Array.Empty<ToolToggleItem>(),
                true,
                false);
        }

        private ToolSettingsSectionData CreateToolSettingsData()
        {
            bool isRegistryAvailable =
                _toolSettingsUseCase.TryGetToolCatalog(
                    out ToolSettingsUseCase.ToolCatalogItem[] allTools);
            if (!isRegistryAvailable)
            {
                return new ToolSettingsSectionData(
                    _model.UI.ShowToolSettings,
                    System.Array.Empty<ToolToggleItem>(),
                    System.Array.Empty<ToolToggleItem>(),
                    false,
                    true);
            }

            List<ToolToggleItem> builtIn = new();
            List<ToolToggleItem> thirdParty = new();

            foreach (ToolSettingsUseCase.ToolCatalogItem tool in allTools)
            {
                if (tool.DisplayDevelopmentOnly)
                {
                    continue;
                }

                bool isEnabled = _toolSettingsUseCase.IsToolEnabled(tool.Name);
                bool isThirdPartyTool = tool.IsThirdParty;

                ToolToggleItem item = new(
                    tool.Name,
                    isEnabled,
                    isThirdPartyTool,
                    tool.SkillDescription);
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

            _toolSettingsUseCase.WarmupRegistry();
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
                _skillSetupUseCase.RemoveSkillFiles(toolName);
            }
            else
            {
                await _skillSetupUseCase.InstallSkillFilesForToolAsync(
                    toolName,
                    !_installSkillsFlat,
                    CancellationToken.None);

                if (!_skillSetupUseCase.IsSkillInstalled(toolName))
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
            bool cliIsDispatcher = CliSetupApplicationFacade.GetCachedCliIsDispatcher();
            string cliExecutablePath = CliSetupApplicationFacade.GetCachedCliExecutablePath();
            string requiredCliVersion = GetMinimumRequiredCliVersion();

            bool isCliInstalled = cliVersion != null || _needsCliPathSetup;
            bool canUninstallCli = CliSetupApplicationFacade.IsPackageOwnedCurrentUserInstallPath(
                cliExecutablePath,
                UnityEngine.Application.platform);
            bool isChecking = !CliSetupApplicationFacade.IsCliCheckCompleted()
                || _isRefreshingVersion
                || _isRefreshingCliPathSetup
                || !includeSkillDirectoryChecks;
            CliSetupCompatibilityState state = CliSetupCompatibility.Evaluate(
                cliVersion,
                cliIsDispatcher,
                requiredCliVersion);
            bool groupSkillsUnderUnityCliLoop = !_installSkillsFlat;
            SkillInstallState selectedTargetInstallState = includeSkillDirectoryChecks
                ? _selectedTargetInstallState
                : SkillInstallState.Checking;

            return new CliSetupData(
                isCliInstalled,
                cliVersion,
                requiredCliVersion,
                state.NeedsUpdate,
                state.NeedsDowngrade,
                canUninstallCli,
                _needsCliPathSetup,
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

        private static string GetMinimumRequiredCliVersion()
        {
            return CliSetupApplicationFacade.GetMinimumRequiredCliVersion();
        }

        private static bool ShouldCheckCliPathSetup()
        {
            return ShouldCheckCliPathSetupForPlatform(
                UnityEngine.Application.platform,
                CliSetupApplicationFacade.HasPackageOwnedCurrentUserInstall(UnityEngine.Application.platform));
        }

        internal static bool ShouldCheckCliPathSetupForPlatform(
            RuntimePlatform platform,
            bool hasPackageOwnedCurrentUserInstall)
        {
            return platform != RuntimePlatform.WindowsEditor && hasPackageOwnedCurrentUserInstall;
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

        private void RefreshSelectedTargetInstallStateInBackground(bool allowDuringCliRefresh = false)
        {
            CancelSkillInstallStateRefresh();
            bool isCliInstalled = CliSetupApplicationFacade.IsCliInstalled();
            if (!UnityCliLoopSettingsWindowRefreshPolicy.ShouldStartSkillInstallStateRefresh(
                    isCliInstalled,
                    _isRefreshingVersion,
                    _isInstallingSkills,
                    allowDuringCliRefresh))
            {
                SkillInstallState resolvedInstallState =
                    UnityCliLoopSettingsWindowRefreshPolicy.ResolveSkillInstallStateWhenRefreshCannotStart(
                        isCliInstalled,
                        _selectedTargetInstallState);
                if (_selectedTargetInstallState != resolvedInstallState)
                {
                    _selectedTargetInstallState = resolvedInstallState;
                    RefreshCliSetupSection();
                }
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
            List<SkillSetupTargetInfo> targets = includeFreshnessCheck
                ? _skillSetupUseCase.DetectSkillTargetsForLayoutAtProjectRoot(projectRoot, !_installSkillsFlat)
                : _skillSetupUseCase.DetectSkillTargetsForLayoutFastAtProjectRoot(projectRoot, !_installSkillsFlat);
            SkillSetupTargetInfo targetInfo = targets
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
            CliPrimaryButtonAction clickedAction = GetCurrentCliPrimaryButtonAction();

            await RefreshCliPrimaryActionStateAsync(CancellationToken.None);
            CliPrimaryButtonAction refreshedAction = GetCurrentCliPrimaryButtonAction();
            CliPrimaryButtonAction executableAction = ResolveExecutableCliPrimaryButtonAction(
                clickedAction,
                refreshedAction);
            if (executableAction == CliPrimaryButtonAction.None)
            {
                return;
            }

            if (executableAction == CliPrimaryButtonAction.RepairPath)
            {
                await HandleRepairCliPathSetup();
                return;
            }

            if (executableAction == CliPrimaryButtonAction.Uninstall)
            {
                await HandleUninstallCli();
                return;
            }

            bool wasCliInstalledBeforeInstall = CliSetupApplicationFacade.IsCliInstalled();
            _needsCliPathSetup = false;
            _isInstallingCli = true;
            RefreshCliSetupSection();

            try
            {
                CliInstallResult result = await CliSetupApplicationFacade.InstallGlobalCliAsync(
                    UnityEngine.Application.platform,
                    CancellationToken.None);

                if (!result.Success)
                {
                    NativeCliInstallCommand command = CliSetupApplicationFacade.GetGlobalCliInstallCommand(
                        UnityEngine.Application.platform,
                        true);
                    EditorUtility.DisplayDialog(
                        "Installation Failed",
                        $"Failed to install uLoop CLI.\n\n{result.ErrorOutput}\n\nYou can try manually:\n{command.ManualCommand}",
                        "OK");
                    return;
                }

                await CliPathSetupPrompt.EnsureVisibleAndShowResultAsync(
                    UnityEngine.Application.platform,
                    CancellationToken.None);
                await RefreshCliPathSetupAsync(CancellationToken.None);
            }
            finally
            {
                _isInstallingCli = false;
                RefreshAllSections(
                    refreshSkillInstallState:
                    CliInstallRefreshPolicy.ShouldRefreshSkillsAfterCliInstall(wasCliInstalledBeforeInstall));
            }
        }

        private CliPrimaryButtonAction GetCurrentCliPrimaryButtonAction()
        {
            string cliVersion = CliSetupApplicationFacade.GetCachedCliVersion();
            bool cliIsDispatcher = CliSetupApplicationFacade.GetCachedCliIsDispatcher();
            string cliExecutablePath = CliSetupApplicationFacade.GetCachedCliExecutablePath();
            bool canUninstallCli = CliSetupApplicationFacade.IsPackageOwnedCurrentUserInstallPath(
                cliExecutablePath,
                UnityEngine.Application.platform);
            return ResolveCliPrimaryButtonAction(
                _needsCliPathSetup,
                cliVersion,
                cliIsDispatcher,
                canUninstallCli);
        }

        private bool ShouldUninstallCliFromPrimaryButton()
        {
            string cliVersion = CliSetupApplicationFacade.GetCachedCliVersion();
            bool cliIsDispatcher = CliSetupApplicationFacade.GetCachedCliIsDispatcher();
            string cliExecutablePath = CliSetupApplicationFacade.GetCachedCliExecutablePath();
            bool canUninstallCli = CliSetupApplicationFacade.IsPackageOwnedCurrentUserInstallPath(
                cliExecutablePath,
                UnityEngine.Application.platform);
            return ShouldUninstallCliFromPrimaryButton(
                cliVersion,
                cliIsDispatcher,
                canUninstallCli);
        }

        internal static bool ShouldUninstallCliFromPrimaryButton(
            string cliVersion,
            bool cliIsDispatcher,
            bool canUninstallCli)
        {
            bool isCliInstalled = cliVersion != null;
            bool needsUpdate = IsCliUpdateNeeded(cliVersion, cliIsDispatcher);
            bool needsDowngrade = IsCliDowngradeNeeded(cliVersion, cliIsDispatcher);
            return CliSetupSection.IsUninstallCliAction(isCliInstalled, needsUpdate, needsDowngrade, canUninstallCli);
        }

        internal static CliPrimaryButtonAction ResolveCliPrimaryButtonAction(
            bool needsCliPathSetup,
            string cliVersion,
            bool cliIsDispatcher,
            bool canUninstallCli)
        {
            bool needsUpdate = IsCliUpdateNeeded(cliVersion, cliIsDispatcher);
            bool needsDowngrade = IsCliDowngradeNeeded(cliVersion, cliIsDispatcher);
            if (ShouldRepairCliPathFromPrimaryButton(
                    needsCliPathSetup,
                    needsUpdate,
                    needsDowngrade))
            {
                return CliPrimaryButtonAction.RepairPath;
            }

            if (ShouldUninstallCliFromPrimaryButton(cliVersion, cliIsDispatcher, canUninstallCli))
            {
                return CliPrimaryButtonAction.Uninstall;
            }

            return CliPrimaryButtonAction.InstallOrUpdate;
        }

        internal static CliPrimaryButtonAction ResolveExecutableCliPrimaryButtonAction(
            CliPrimaryButtonAction clickedAction,
            CliPrimaryButtonAction refreshedAction)
        {
            if (clickedAction == refreshedAction)
            {
                return clickedAction;
            }

            if (clickedAction == CliPrimaryButtonAction.InstallOrUpdate
                && refreshedAction == CliPrimaryButtonAction.RepairPath)
            {
                return CliPrimaryButtonAction.RepairPath;
            }

            return CliPrimaryButtonAction.None;
        }

        internal static bool ShouldRepairCliPathFromPrimaryButton(
            bool needsCliPathSetup,
            bool needsUpdate,
            bool needsDowngrade)
        {
            return needsCliPathSetup && !needsUpdate && !needsDowngrade;
        }

        private async Task RefreshCliPrimaryActionStateAsync(CancellationToken ct)
        {
            _isRefreshingVersion = true;
            RefreshCliSetupSection();

            try
            {
                await CliSetupApplicationFacade.ForceRefreshCliVersionAsync(ct);
                await RefreshCliPathSetupAsync(ct);
            }
            finally
            {
                _isRefreshingVersion = false;
                RefreshCliSetupSection();
            }
        }

        private async Task RefreshCliPathSetupAsync(CancellationToken ct)
        {
            if (!ShouldCheckCliPathSetup())
            {
                _needsCliPathSetup = false;
                return;
            }

            bool isCliVisibleFromShell = await CliSetupApplicationFacade.IsCliVisibleFromShellAsync(
                UnityEngine.Application.platform,
                ct);
            _needsCliPathSetup = !isCliVisibleFromShell;
        }

        private async Task HandleRepairCliPathSetup()
        {
            _isInstallingCli = true;
            RefreshCliSetupSection();

            try
            {
                await CliPathSetupPrompt.EnsureVisibleAndShowResultAsync(
                    UnityEngine.Application.platform,
                    CancellationToken.None);
                await RefreshCliPathSetupAsync(CancellationToken.None);
            }
            finally
            {
                _isInstallingCli = false;
                RefreshAllSections();
            }
        }

        internal static bool IsCliUpdateNeeded(string cliVersion, bool cliIsDispatcher)
        {
            return EvaluateCliSetupCompatibility(cliVersion, cliIsDispatcher).NeedsUpdate;
        }

        internal static bool IsCliDowngradeNeeded(string cliVersion, bool cliIsDispatcher)
        {
            return EvaluateCliSetupCompatibility(cliVersion, cliIsDispatcher).NeedsDowngrade;
        }

        private static CliSetupCompatibilityState EvaluateCliSetupCompatibility(
            string cliVersion,
            bool cliIsDispatcher)
        {
            return CliSetupCompatibility.Evaluate(
                cliVersion,
                cliIsDispatcher,
                CliConstants.MINIMUM_REQUIRED_DISPATCHER_VERSION);
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
                SkillSetupTargetInfo target = new(
                    selection.DisplayName,
                    selection.DirectoryName,
                    selection.InstallFlag,
                    hasSkillsDirectory: true,
                    hasExistingSkills: false,
                    hasDifferentLayoutSkills: false,
                    SkillInstallState.Missing);
                await _skillSetupUseCase.InstallSkillFilesAsync(
                    new List<SkillSetupTargetInfo> { target },
                    !_installSkillsFlat,
                    CancellationToken.None);
                EditorDialogHelper.ShowSkillsInstalledDialog();
            }
            finally
            {
                _isInstallingSkills = false;
                RefreshSelectedTargetInstallStateFast();
                RefreshSelectedTargetInstallStateInBackground(allowDuringCliRefresh: true);
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
            _editorSettingsService.SetInstallSkillsFlat(_installSkillsFlat);
        }

        private static UnityCliLoopEditorSettingsService GetEditorSettingsService()
        {
            if (RegisteredEditorSettingsService == null)
            {
                throw new InvalidOperationException("Unity CLI Loop editor settings service is not registered.");
            }

            return RegisteredEditorSettingsService;
        }

        private static UnityCliLoopEditorSessionStateService GetSessionStateService()
        {
            if (RegisteredSessionStateService == null)
            {
                throw new InvalidOperationException("Unity CLI Loop editor session state service is not registered.");
            }

            return RegisteredSessionStateService;
        }

        private void HandleRefreshSkillsState()
        {
            RefreshSelectedTargetInstallStateFast();
            RefreshSelectedTargetInstallStateInBackground(allowDuringCliRefresh: true);
        }

    }
}
