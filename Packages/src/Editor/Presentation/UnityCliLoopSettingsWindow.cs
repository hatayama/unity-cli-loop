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
        private const bool ForceFlatSkillInstall = true;
        private const double DeferredInitialRefreshDelaySeconds = 0.05;

        private static IUnityCliLoopEditorSettingsPort RegisteredEditorSettingsPort;
        private static ISessionFlagsRepository RegisteredSessionFlagsRepository;
        private static UnityCliLoopServerApplicationService RegisteredServerApplicationService;
        private static CliSetupApplicationService RegisteredCliSetupApplicationService;
        private static ToolSettingsUseCase RegisteredToolSettingsUseCase;
        private static SkillSetupUseCase RegisteredSkillSetupUseCase;

        private UnityCliLoopSettingsWindowUI _view;
        private UnityCliLoopSettingsModel _model;
        private UnityCliLoopSettingsWindowEventHandler _eventHandler;
        private UnityCliLoopSettingsCliSetupPresenter _cliSetupPresenter;
        private UnityCliLoopSettingsToolSettingsPresenter _toolSettingsPresenter;
        private SkillSetupUseCase _skillSetupUseCase;
        private ToolSettingsUseCase _toolSettingsUseCase;
        private IUnityCliLoopEditorSettingsPort _editorSettingsPort;
        private ISessionFlagsRepository _sessionFlagsRepository;
        private UnityCliLoopServerApplicationService _serverApplicationService;
        private CliSetupApplicationService _cliSetupApplicationService;

        private SkillsTarget _skillsTarget = SkillsTarget.Claude;
        private bool _installSkillsFlat;
        private bool _isInstallingCli;
        private bool _isInstallingSkills;
        private bool _isRefreshingVersion;
        private bool _isRefreshingCliPathSetup;
        private bool _needsCliPathSetup;
        private bool _isDeferredInitialRefreshScheduled;
        private bool _hasCompletedDeferredInitialRefresh;
        private double _deferredInitialRefreshDueTime;
        private SkillInstallState _selectedTargetInstallState = SkillInstallState.Missing;
        private CancellationTokenSource _skillInstallStateRefreshCts;

        [MenuItem("Window/Unity CLI Loop/Settings", priority = 0)]
        public static void ShowWindow()
        {
            UnityCliLoopSettingsWindow window = GetWindow<UnityCliLoopSettingsWindow>("Unity CLI Loop");
            window.Show();
        }

        internal static void InitializeEditorServices(
            IUnityCliLoopEditorSettingsPort editorSettingsPort,
            ISessionFlagsRepository sessionFlagsRepository,
            UnityCliLoopServerApplicationService serverApplicationService,
            CliSetupApplicationService cliSetupApplicationService,
            ToolSettingsUseCase toolSettingsUseCase,
            SkillSetupUseCase skillSetupUseCase)
        {
            System.Diagnostics.Debug.Assert(editorSettingsPort != null, "editorSettingsPort must not be null");
            System.Diagnostics.Debug.Assert(sessionFlagsRepository != null, "sessionFlagsRepository must not be null");
            System.Diagnostics.Debug.Assert(
                serverApplicationService != null,
                "serverApplicationService must not be null");
            System.Diagnostics.Debug.Assert(
                cliSetupApplicationService != null,
                "cliSetupApplicationService must not be null");
            System.Diagnostics.Debug.Assert(toolSettingsUseCase != null, "toolSettingsUseCase must not be null");
            System.Diagnostics.Debug.Assert(skillSetupUseCase != null, "skillSetupUseCase must not be null");

            RegisteredEditorSettingsPort = editorSettingsPort
                ?? throw new ArgumentNullException(nameof(editorSettingsPort));
            RegisteredSessionFlagsRepository = sessionFlagsRepository
                ?? throw new ArgumentNullException(nameof(sessionFlagsRepository));
            RegisteredServerApplicationService = serverApplicationService
                ?? throw new ArgumentNullException(nameof(serverApplicationService));
            RegisteredCliSetupApplicationService = cliSetupApplicationService
                ?? throw new ArgumentNullException(nameof(cliSetupApplicationService));
            RegisteredToolSettingsUseCase = toolSettingsUseCase
                ?? throw new ArgumentNullException(nameof(toolSettingsUseCase));
            RegisteredSkillSetupUseCase = skillSetupUseCase
                ?? throw new ArgumentNullException(nameof(skillSetupUseCase));
        }

        private void OnEnable()
        {
            InitializeAll();
        }

        private void OnDestroy()
        {
            CancelDeferredInitialRefresh();
            _toolSettingsPresenter?.CancelRegistryWarmup();
            _toolSettingsPresenter?.ResetRegistryWarmupAttemptCount();
            CancelSkillInstallStateRefresh();
            _toolSettingsPresenter?.SetViewReady(false);
            _view?.Dispose();
            _view = null;
            _cliSetupPresenter = null;
            _toolSettingsPresenter = null;
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
            _model.LoadFromSessionState();
            HandlePostCompileMode().Forget();
        }

        private void InitializeModel()
        {
            _model = new UnityCliLoopSettingsModel(
                _toolSettingsUseCase,
                _editorSettingsPort);
        }

        private void InitializeApplicationServices()
        {
            _skillSetupUseCase = GetSkillSetupUseCase();
            _toolSettingsUseCase = GetToolSettingsUseCase();
            _editorSettingsPort = GetEditorSettingsPort();
            _sessionFlagsRepository = GetSessionFlagsRepository();
            _serverApplicationService = GetServerApplicationService();
            _cliSetupApplicationService = GetCliSetupApplicationService();
        }

        private void InitializeView()
        {
            _view = new UnityCliLoopSettingsWindowUI(rootVisualElement);
            _cliSetupPresenter = new UnityCliLoopSettingsCliSetupPresenter(
                _view,
                _cliSetupApplicationService);
            _toolSettingsPresenter = new UnityCliLoopSettingsToolSettingsPresenter(
                _view,
                _toolSettingsUseCase);
            _toolSettingsPresenter.SetViewReady(true);
            SetupViewCallbacks();
        }

        private void SetupViewCallbacks()
        {
            _view.OnRefreshCliVersion += () => HandleRefreshCliVersion().Forget();
            _view.OnInstallCli += () => HandleInstallCli().Forget();
            _view.OnInstallSkills += () => HandleInstallSkills().Forget();
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
                _toolSettingsUseCase,
                _serverApplicationService);
            _eventHandler.Initialize();
        }

        private void LoadSavedSettings()
        {
            _model.LoadFromSettings();
            _installSkillsFlat = ForceFlatSkillInstall;
        }

        private async Task HandlePostCompileMode()
        {
            _model.EnablePostCompileMode();
            _sessionFlagsRepository.SetShowReconnectingUI(false);

            Task recoveryTask = _serverApplicationService.RecoveryTask;
            if (recoveryTask != null && !recoveryTask.IsCompleted)
            {
                await recoveryTask;
            }

            bool isAfterCompile = _sessionFlagsRepository.GetIsAfterCompile();

            if (isAfterCompile)
            {
                _sessionFlagsRepository.ClearAfterCompileFlag();
                return;
            }

            // The server lifecycle owns automatic recovery after domain reload.
        }

        private void OnDisable()
        {
            CancelDeferredInitialRefresh();
            _toolSettingsPresenter?.CancelRegistryWarmup();
            _toolSettingsPresenter?.ResetRegistryWarmupAttemptCount();
            CancelSkillInstallStateRefresh();
            CleanupEventHandler();
            _model?.SaveToSessionState();
            _toolSettingsPresenter?.SetViewReady(false);
            _view?.Dispose();
            _view = null;
            _cliSetupPresenter = null;
            _toolSettingsPresenter = null;
        }

        private void CleanupEventHandler()
        {
            _eventHandler?.Cleanup();
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
                RefreshCliVersionInBackground().Forget();
                RefreshCliPathSetupInBackground().Forget();
                if (refreshSkillInstallState)
                {
                    RefreshSelectedTargetInstallStateInBackground();
                }
            }
            RefreshCliSetupSection(runExpensiveChecks);

            RefreshToolSettingsHeader();
            if (runExpensiveChecks)
            {
                _toolSettingsPresenter?.RefreshCatalogIfNeeded(_model.UI.ShowToolSettings);
            }
        }

        private async Task RefreshCliVersionInBackground()
        {
            if (_cliSetupApplicationService.IsCliCheckCompleted())
            {
                return;
            }

            await _cliSetupApplicationService.RefreshCliVersionAsync(CancellationToken.None);
            RefreshCliPathSetupInBackground().Forget();
            RefreshCliSetupSection();
            RefreshSelectedTargetInstallStateInBackground();
        }

        private async Task RefreshCliPathSetupInBackground()
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
                bool isCliVisibleFromShell = await _cliSetupApplicationService.IsCliVisibleFromShellAsync(
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

        private async Task HandleRefreshCliVersion()
        {
            if (_isRefreshingVersion)
            {
                return;
            }

            _isRefreshingVersion = true;
            RefreshCliSetupSection();

            try
            {
                Task forceRefresh = _cliSetupApplicationService.ForceRefreshCliVersionAsync(CancellationToken.None);
                Task minimumDelay = Task.Delay(500);
                await Task.WhenAll(forceRefresh, minimumDelay);
                RefreshCliPathSetupInBackground().Forget();
            }
            finally
            {
                _isRefreshingVersion = false;
                RefreshCliSetupSection();
            }
        }

        public void InvalidateToolSettingsCatalog()
        {
            _toolSettingsPresenter?.InvalidateCatalog();
        }

        private void RefreshToolSettingsHeader()
        {
            if (_toolSettingsPresenter == null)
            {
                return;
            }

            _toolSettingsPresenter.UpdateHeader(_model.UI.ShowToolSettings);
        }

        private void UpdateShowToolSettings(bool show)
        {
            _model.UpdateShowToolSettings(show);
            _toolSettingsPresenter?.HandleShowToolSettingsChanged(show);
        }

        private void HandleToolToggled(string toolName, bool enabled)
        {
            _model.UpdateToolEnabled(toolName, enabled);
            _view?.UpdateSingleToolToggle(toolName, enabled);

            // Skill synchronization can touch many files, so defer it to keep UI input responsive.
            EditorApplication.delayCall += () => ApplyToolToggleSideEffects(toolName, enabled).Forget();
        }

        private async Task ApplyToolToggleSideEffects(string toolName, bool enabled)
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
            if (_cliSetupPresenter == null)
            {
                return;
            }

            _cliSetupPresenter.Update(
                _needsCliPathSetup,
                _isInstallingCli,
                _isRefreshingVersion,
                _isRefreshingCliPathSetup,
                includeSkillDirectoryChecks,
                _installSkillsFlat,
                _selectedTargetInstallState,
                _skillsTarget,
                _isInstallingSkills);
        }

        private bool ShouldCheckCliPathSetup()
        {
            return UnityCliLoopSettingsCliSetupPresenter.ShouldCheckCliPathSetupForPlatform(
                UnityEngine.Application.platform,
                _cliSetupApplicationService.HasPackageOwnedCurrentUserInstall(UnityEngine.Application.platform));
        }

        private void RefreshSelectedTargetInstallStateFast()
        {
            if (!_cliSetupApplicationService.IsCliInstalled())
            {
                _selectedTargetInstallState = SkillInstallState.Missing;
                RefreshCliSetupSection();
                return;
            }

            _selectedTargetInstallState = GetSelectedTargetInstallStateForCurrentProject(includeFreshnessCheck: false);
            RefreshCliSetupSection();
        }

        private void RefreshSelectedTargetInstallStateInBackground(bool allowDuringCliRefresh = false)
        {
            CancelSkillInstallStateRefresh();
            bool isCliInstalled = _cliSetupApplicationService.IsCliInstalled();
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
            RefreshSelectedTargetInstallStateAsync(cts.Token).Forget();
        }

        private async Task RefreshSelectedTargetInstallStateAsync(CancellationToken ct)
        {
            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            SkillInstallState installState = await Task.Run(
                () => GetSelectedTargetInstallStateAtProjectRoot(projectRoot, includeFreshnessCheck: true));
            if (ct.IsCancellationRequested)
            {
                return;
            }

            _selectedTargetInstallState = installState;
            RefreshCliSetupSection();
        }

        private SkillInstallState GetSelectedTargetInstallStateForCurrentProject(bool includeFreshnessCheck)
        {
            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            return GetSelectedTargetInstallStateAtProjectRoot(projectRoot, includeFreshnessCheck);
        }

        private SkillInstallState GetSelectedTargetInstallStateAtProjectRoot(
            string projectRoot,
            bool includeFreshnessCheck)
        {
            SkillSetupTargetInfo targetInfo = GetSelectedTargetInfo(projectRoot, includeFreshnessCheck);
            return string.IsNullOrEmpty(targetInfo.DirName)
                ? SkillInstallState.Missing
                : targetInfo.InstallState;
        }

        private SkillSetupTargetInfo GetSelectedTargetInfo(
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

            return targetInfo;
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

        private async Task HandleInstallCli()
        {
            CliSetupPrimaryAction clickedAction = _cliSetupPresenter.ResolveCurrentPrimaryButtonAction(_needsCliPathSetup);

            await RefreshCliPrimaryActionStateAsync(CancellationToken.None);
            CliSetupPrimaryAction refreshedAction = _cliSetupPresenter.ResolveCurrentPrimaryButtonAction(_needsCliPathSetup);
            CliSetupPrimaryAction executableAction =
                UnityCliLoopSettingsCliSetupPresenter.ResolveExecutableCliPrimaryButtonAction(
                    clickedAction,
                    refreshedAction);
            if (executableAction == CliSetupPrimaryAction.None)
            {
                return;
            }

            if (executableAction == CliSetupPrimaryAction.RepairPath)
            {
                await HandleRepairCliPathSetup();
                return;
            }

            if (executableAction == CliSetupPrimaryAction.Uninstall)
            {
                await HandleUninstallCli();
                return;
            }

            bool wasCliInstalledBeforeInstall = _cliSetupApplicationService.IsCliInstalled();
            _needsCliPathSetup = false;
            _isInstallingCli = true;
            RefreshCliSetupSection();

            try
            {
                CliInstallResult result = await _cliSetupApplicationService.InstallGlobalCliAsync(
                    UnityEngine.Application.platform,
                    CancellationToken.None);

                if (!result.Success)
                {
                    NativeCliInstallCommand command = _cliSetupApplicationService.GetGlobalCliInstallCommand(
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
                    _cliSetupApplicationService,
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

        private async Task RefreshCliPrimaryActionStateAsync(CancellationToken ct)
        {
            _isRefreshingVersion = true;
            RefreshCliSetupSection();

            try
            {
                await _cliSetupApplicationService.ForceRefreshCliVersionAsync(ct);
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

            bool isCliVisibleFromShell = await _cliSetupApplicationService.IsCliVisibleFromShellAsync(
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
                    _cliSetupApplicationService,
                    CancellationToken.None);
                await RefreshCliPathSetupAsync(CancellationToken.None);
            }
            finally
            {
                _isInstallingCli = false;
                RefreshAllSections();
            }
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
                CliInstallResult result = await _cliSetupApplicationService.UninstallGlobalCliAsync(
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

        private async Task HandleInstallSkills()
        {
            if (!_cliSetupApplicationService.IsCliInstalled())
            {
                EditorUtility.DisplayDialog(
                    "CLI Not Found",
                    "uloop CLI is not installed. Please install the CLI first.",
                    "OK");
                return;
            }

            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            SkillSetupTargetInfo selectedTargetInfo =
                GetSelectedTargetInfo(projectRoot, includeFreshnessCheck: true);
            bool shouldShowSkillsInstalledDialog =
                SkillInstallDialogPolicy.ShouldShowForSelectedTarget(selectedTargetInfo);
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
                if (shouldShowSkillsInstalledDialog)
                {
                    EditorDialogHelper.ShowSkillsInstalledDialog();
                }
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
            _editorSettingsPort.SetInstallSkillsFlat(_installSkillsFlat);
        }

        private static IUnityCliLoopEditorSettingsPort GetEditorSettingsPort()
        {
            if (RegisteredEditorSettingsPort == null)
            {
                throw new InvalidOperationException("Unity CLI Loop editor settings port is not registered.");
            }

            return RegisteredEditorSettingsPort;
        }

        private static ISessionFlagsRepository GetSessionFlagsRepository()
        {
            if (RegisteredSessionFlagsRepository == null)
            {
                throw new InvalidOperationException("Unity CLI Loop editor session flags repository is not registered.");
            }

            return RegisteredSessionFlagsRepository;
        }

        private static UnityCliLoopServerApplicationService GetServerApplicationService()
        {
            if (RegisteredServerApplicationService == null)
            {
                throw new InvalidOperationException(
                    "Unity CLI Loop server application service is not registered.");
            }

            return RegisteredServerApplicationService;
        }

        private static CliSetupApplicationService GetCliSetupApplicationService()
        {
            if (RegisteredCliSetupApplicationService == null)
            {
                throw new InvalidOperationException(
                    "Unity CLI Loop CLI setup application service is not registered.");
            }

            return RegisteredCliSetupApplicationService;
        }

        private static ToolSettingsUseCase GetToolSettingsUseCase()
        {
            if (RegisteredToolSettingsUseCase == null)
            {
                throw new InvalidOperationException("Unity CLI Loop tool settings use case is not registered.");
            }

            return RegisteredToolSettingsUseCase;
        }

        private static SkillSetupUseCase GetSkillSetupUseCase()
        {
            if (RegisteredSkillSetupUseCase == null)
            {
                throw new InvalidOperationException("Unity CLI Loop skill setup use case is not registered.");
            }

            return RegisteredSkillSetupUseCase;
        }

        private void HandleRefreshSkillsState()
        {
            RefreshSelectedTargetInstallStateFast();
            RefreshSelectedTargetInstallStateInBackground(allowDuringCliRefresh: true);
        }

    }
}
