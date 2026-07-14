using UnityEditor;
using System;
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
        private UnityCliLoopSettingsSkillsPresenter _skillsPresenter;
        private UnityCliLoopSettingsToolSettingsPresenter _toolSettingsPresenter;
        private SkillSetupUseCase _skillSetupUseCase;
        private ToolSettingsUseCase _toolSettingsUseCase;
        private IUnityCliLoopEditorSettingsPort _editorSettingsPort;
        private ISessionFlagsRepository _sessionFlagsRepository;
        private UnityCliLoopServerApplicationService _serverApplicationService;
        private CliSetupApplicationService _cliSetupApplicationService;

        private bool _isDeferredInitialRefreshScheduled;
        private bool _hasCompletedDeferredInitialRefresh;
        private double _deferredInitialRefreshDueTime;

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
            _skillsPresenter?.CancelSkillInstallStateRefresh();
            _toolSettingsPresenter?.SetViewReady(false);
            _view?.Dispose();
            _view = null;
            _cliSetupPresenter = null;
            _skillsPresenter = null;
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
            _skillsPresenter = new UnityCliLoopSettingsSkillsPresenter(
                _skillSetupUseCase,
                _cliSetupApplicationService,
                _editorSettingsPort);
            _toolSettingsPresenter = new UnityCliLoopSettingsToolSettingsPresenter(
                _view,
                _toolSettingsUseCase);
            BindPresenterCoordination();
            _toolSettingsPresenter.SetViewReady(true);
            SetupViewCallbacks();
        }

        private void BindPresenterCoordination()
        {
            _cliSetupPresenter.BindCoordination(
                getSkillsSnapshot: () => _skillsPresenter.GetSnapshot(),
                refreshSkillsInstallStateInBackground: () =>
                    _skillsPresenter.RefreshSelectedTargetInstallStateInBackground(),
                refreshAllSections: refreshSkillInstallState =>
                    RefreshAllSections(refreshSkillInstallState: refreshSkillInstallState));
            _skillsPresenter.BindCoordination(
                refreshCliSetupSection: includeSkillDirectoryChecks =>
                    _cliSetupPresenter.RefreshSection(includeSkillDirectoryChecks),
                isRefreshingVersion: () => _cliSetupPresenter.IsRefreshingVersion);
        }

        private void SetupViewCallbacks()
        {
            _view.OnRefreshCliVersion += () => _cliSetupPresenter.HandleRefreshCliVersion().Forget();
            _view.OnInstallCli += () => _cliSetupPresenter.HandleInstallCli().Forget();
            _view.OnInstallSkills += () => _skillsPresenter.HandleInstallSkills().Forget();
            _view.OnRefreshSkillsState += _skillsPresenter.HandleRefreshSkillsState;
            _view.OnSkillsTargetChanged += _skillsPresenter.HandleSkillsTargetChanged;
            _view.OnGroupSkillsChanged += _skillsPresenter.HandleGroupSkillsChanged;
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
            _skillsPresenter?.CancelSkillInstallStateRefresh();
            CleanupEventHandler();
            _model?.SaveToSessionState();
            _toolSettingsPresenter?.SetViewReady(false);
            _view?.Dispose();
            _view = null;
            _cliSetupPresenter = null;
            _skillsPresenter = null;
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
            _skillsPresenter.MarkSelectedTargetInstallStateChecking();
            _skillsPresenter.ApplyFlatSkillInstallPreference();
            RefreshAllSections(
                refreshSkillInstallState: false,
                refreshMode: UnityCliLoopSettingsWindowRefreshMode.Full);
            _skillsPresenter.RefreshSelectedTargetInstallStateInBackground();
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
                _skillsPresenter.RefreshSelectedTargetInstallStateFast();
            }

            if (runExpensiveChecks)
            {
                _cliSetupPresenter.RefreshCliVersionInBackground().Forget();
                _cliSetupPresenter.RefreshCliPathSetupInBackground().Forget();
                if (refreshSkillInstallState)
                {
                    _skillsPresenter.RefreshSelectedTargetInstallStateInBackground();
                }
            }
            _cliSetupPresenter.RefreshSection(runExpensiveChecks);

            RefreshToolSettingsHeader();
            if (runExpensiveChecks)
            {
                _toolSettingsPresenter?.RefreshCatalogIfNeeded(_model.UI.ShowToolSettings);
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
            EditorApplication.delayCall += () => _skillsPresenter.ApplyToolToggleSideEffects(toolName, enabled).Forget();
        }

        private void UpdateShowConfiguration(bool show)
        {
            _model.UpdateShowConfiguration(show);
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
    }
}
