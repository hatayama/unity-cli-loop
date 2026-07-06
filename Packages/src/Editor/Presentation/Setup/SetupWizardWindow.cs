using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using RuntimePlatform = UnityEngine.RuntimePlatform;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// Defines the Unity Editor window for Setup Wizard workflows.
    /// </summary>
    public class SetupWizardWindow : EditorWindow
    {
        private const string WindowTitle = "Unity CLI Loop Setup";
        private const string UXML_RELATIVE_PATH = "Editor/Presentation/Setup/SetupWizardWindow.uxml";
        private const string USS_RELATIVE_PATH = "Editor/Presentation/Setup/SetupWizardWindow.uss";
        private const string GITHUB_ICON_RELATIVE_PATH = "Editor/Presentation/Setup/GitHub_Invertocat_White.png";
        internal const bool ForceFlatSkillInstall = true;
        private static IUnityCliLoopEditorSettingsPort RegisteredEditorSettingsPort;
        private static CliSetupApplicationService RegisteredCliSetupApplicationService;
        private static SkillSetupUseCase RegisteredSkillSetupUseCase;

        internal static void InitializeForEditorStartup(
            IUnityCliLoopEditorSettingsPort editorSettingsPort,
            ISessionFlagsRepository sessionFlagsRepository,
            CliSetupApplicationService cliSetupApplicationService,
            SkillSetupUseCase skillSetupUseCase)
        {
            InitializeEditorServices(
                editorSettingsPort,
                cliSetupApplicationService,
                skillSetupUseCase);

            if (AssetDatabase.IsAssetImportWorkerProcess()) return;
            if (UnityEngine.Application.isBatchMode) return;

            SetupWizardStartupFlow startupFlow = new(
                editorSettingsPort,
                sessionFlagsRepository,
                cliSetupApplicationService,
                skillSetupUseCase,
                ShowWindowOnVersionChange,
                ThirdPartyToolMigrationWizardWindow.ShowWindowForAutoScan);
            EditorApplication.delayCall += startupFlow.TryShowOnVersionChange;
        }

        internal static void InitializeEditorServices(
            IUnityCliLoopEditorSettingsPort editorSettingsPort,
            CliSetupApplicationService cliSetupApplicationService,
            SkillSetupUseCase skillSetupUseCase)
        {
            Debug.Assert(editorSettingsPort != null, "editorSettingsPort must not be null");
            Debug.Assert(cliSetupApplicationService != null, "cliSetupApplicationService must not be null");
            Debug.Assert(skillSetupUseCase != null, "skillSetupUseCase must not be null");

            RegisteredEditorSettingsPort = editorSettingsPort
                ?? throw new System.ArgumentNullException(nameof(editorSettingsPort));
            RegisteredCliSetupApplicationService = cliSetupApplicationService
                ?? throw new System.ArgumentNullException(nameof(cliSetupApplicationService));
            RegisteredSkillSetupUseCase = skillSetupUseCase
                ?? throw new System.ArgumentNullException(nameof(skillSetupUseCase));
        }

        [MenuItem("Window/Unity CLI Loop/Setup Wizard", priority = 3)]
        public static void ShowWindow()
        {
            ShowWindowInternal(false);
        }

        private static void ShowWindowOnVersionChange()
        {
            ShowWindowInternal(true);
        }

        private static void ShowWindowInternal(bool shouldRecordVersion)
        {
            string currentVersion = UnityCliLoopConstants.PackageInfo.version;
            string currentMinimumDispatcherVersion = GetMinimumRequiredCliVersion();
            if (TryReuseOpenWindow(
                HasOpenInstances<SetupWizardWindow>(),
                shouldRecordVersion,
                currentVersion,
                currentMinimumDispatcherVersion,
                FocusExistingWindow))
            {
                return;
            }

            string lastSeenSetupWizardVersionBeforeOpen =
                GetEditorSettingsPort().GetLastSeenSetupWizardVersion();
            Rect windowPosition = SetupWizardWindowResizer.CreateCenteredRect(
                EditorGUIUtility.GetMainWindowPosition(),
                SetupWizardWindowResizer.MinimumWindowSize);
            SetupWizardWindow window = CreateInstance<SetupWizardWindow>();
            PrepareForOpen(
                window,
                WindowTitle,
                windowPosition,
                lastSeenSetupWizardVersionBeforeOpen,
                shouldRecordVersion);
            window.ShowUtility();
            window.ScheduleResizeToContent();
        }

        internal static string GetGitHubRepositoryUrl()
        {
            return UnityCliLoopUIConstants.PROJECT_REPOSITORY_URL;
        }

        internal static bool TryReuseOpenWindow(
            bool hasOpenWindow,
            bool shouldRecordVersion,
            string currentVersion,
            string currentMinimumDispatcherVersion,
            System.Action focusExistingWindow)
        {
            if (!hasOpenWindow) return false;

            Debug.Assert(focusExistingWindow != null, "focusExistingWindow must not be null");
            Debug.Assert(!string.IsNullOrEmpty(currentVersion), "currentVersion must not be null or empty");
            Debug.Assert(
                !string.IsNullOrEmpty(currentMinimumDispatcherVersion),
                "currentMinimumDispatcherVersion must not be null or empty");
            focusExistingWindow();
            SetupWizardStartupFlow.MaybeRecordLastSeenSetupWizardState(
                GetEditorSettingsPort(),
                shouldRecordVersion,
                currentVersion,
                currentMinimumDispatcherVersion);
            return true;
        }

        internal static void PrepareForOpen(
            SetupWizardWindow window,
            string title,
            Rect position,
            string lastSeenSetupWizardVersionBeforeOpen,
            bool shouldRecordVersionAfterCreateGui)
        {
            Debug.Assert(window != null, "window must not be null");
            Debug.Assert(!string.IsNullOrEmpty(title), "title must not be null or empty");

            window.titleContent = new GUIContent(title);
            window.position = position;
            window._lastSeenSetupWizardVersionBeforeOpen =
                lastSeenSetupWizardVersionBeforeOpen ?? string.Empty;
            window._shouldRecordLastSeenVersionAfterCreateGui = shouldRecordVersionAfterCreateGui;
        }

        private static void FocusExistingWindow()
        {
            FocusWindowIfItsOpen<SetupWizardWindow>();
        }

        private static IUnityCliLoopEditorSettingsPort GetEditorSettingsPort()
        {
            if (RegisteredEditorSettingsPort == null)
            {
                throw new System.InvalidOperationException("Unity CLI Loop editor settings port is not registered.");
            }

            return RegisteredEditorSettingsPort;
        }

        private static CliSetupApplicationService GetCliSetupApplicationService()
        {
            if (RegisteredCliSetupApplicationService == null)
            {
                throw new System.InvalidOperationException(
                    "Setup Wizard CLI setup application service is not initialized.");
            }

            return RegisteredCliSetupApplicationService;
        }

        private static SkillSetupUseCase GetSkillSetupUseCase()
        {
            if (RegisteredSkillSetupUseCase == null)
            {
                throw new System.InvalidOperationException("Setup Wizard skill setup use case is not initialized.");
            }

            return RegisteredSkillSetupUseCase;
        }

        // Prerequisite
        private VisualElement _nodejsWarning;
        private VisualElement _nodejsOk;
        private Button _refreshButton;

        // Step 2
        private VisualElement _groupSkillsRow;
        private EnumField _skillsTargetField;
        private Toggle _groupSkillsToggle;
        private Label _groupSkillsLabel;

        // Footer
        private Toggle _suppressAutoShowToggle;
        private Button _openSettingsButton;
        private Button _closeButton;
        private VisualElement _githubLinkRow;
        private Label _githubLinkLabel;
        private Image _githubLinkIcon;
        private ScrollView _mainScrollView;
        private SetupWizardCliStepPresenter _cliStepPresenter;
        private SetupWizardSkillsStepPresenter _skillsStepPresenter;

        // State
        private bool _isInstallingCli;
        private bool _isInstallingSkills;
        private bool _needsCliPathSetup;
        private bool _isSkillsTargetFieldInitialized;
        private bool _shouldUseFirstInstallSkillsUi;
        private bool _installSkillsFlat;
        [SerializeField]
        private string _lastSeenSetupWizardVersionBeforeOpen = string.Empty;
        [SerializeField]
        private bool _shouldRecordLastSeenVersionAfterCreateGui;
        private IVisualElementScheduledItem _initialRefreshScheduledItem;
        private CancellationTokenSource _skillInstallStateRefreshCts;
        private SkillsTarget _skillsTarget = SkillsTarget.Claude;
        private SkillSetupUseCase _skillSetupUseCase;
        private IUnityCliLoopEditorSettingsPort _editorSettingsPort;
        private CliSetupApplicationService _cliSetupApplicationService;
        private SetupWizardWindowResizer _resizer;

        private void CreateGUI()
        {
            InitializeApplicationServices();
            InitializeFirstInstallSkillsUiState();
            LoadLayout();
            BindElements();
            _resizer = new SetupWizardWindowResizer(this, _mainScrollView);
            BindEvents();
            BindSizeUpdates();
            ApplyInitialCheckingState();
            ScheduleInitialRefresh();
            ScheduleResizeToContent();
            RecordLastSeenSetupWizardStateAfterSuccessfulCreateGui();
        }

        private void InitializeApplicationServices()
        {
            _skillSetupUseCase = GetSkillSetupUseCase();
            _editorSettingsPort = GetEditorSettingsPort();
            _cliSetupApplicationService = GetCliSetupApplicationService();
        }

        private void InitializeFirstInstallSkillsUiState()
        {
            _shouldUseFirstInstallSkillsUi = ShouldUseFirstInstallSkillsUi(
                _lastSeenSetupWizardVersionBeforeOpen);
        }

        private void RecordLastSeenSetupWizardStateAfterSuccessfulCreateGui()
        {
            SetupWizardStartupFlow.MaybeRecordLastSeenSetupWizardState(
                _editorSettingsPort,
                _shouldRecordLastSeenVersionAfterCreateGui,
                UnityCliLoopConstants.PackageInfo.version,
                GetMinimumRequiredCliVersion());
            _shouldRecordLastSeenVersionAfterCreateGui = false;
        }

        private void OnDisable()
        {
            _initialRefreshScheduledItem?.Pause();
            _resizer?.Pause();
            CancelSkillInstallStateRefresh();
        }

        private void LoadLayout()
        {
            string uxmlPath = $"{UnityCliLoopConstants.PackageAssetPath}/{UXML_RELATIVE_PATH}";
            VisualTreeAsset visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlPath);
            Debug.Assert(visualTree != null, $"UXML not found at {uxmlPath}");
            visualTree.CloneTree(rootVisualElement);

            string ussPath = $"{UnityCliLoopConstants.PackageAssetPath}/{USS_RELATIVE_PATH}";
            StyleSheet styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(ussPath);
            Debug.Assert(styleSheet != null, $"USS not found at {ussPath}");
            rootVisualElement.styleSheets.Add(styleSheet);
        }

        private void BindElements()
        {
            _nodejsWarning = rootVisualElement.Q<VisualElement>("nodejs-warning");
            _nodejsOk = rootVisualElement.Q<VisualElement>("nodejs-ok");
            _refreshButton = rootVisualElement.Q<Button>("refresh-button");

            VisualElement cliStatusIcon = rootVisualElement.Q<VisualElement>("cli-status-icon");
            Label cliStatusLabel = rootVisualElement.Q<Label>("cli-status-label");
            Button installCliButton = rootVisualElement.Q<Button>("install-cli-button");
            _cliStepPresenter = new SetupWizardCliStepPresenter(
                cliStatusIcon,
                cliStatusLabel,
                installCliButton,
                HandleInstallCli);

            _groupSkillsRow = rootVisualElement.Q<VisualElement>("group-skills-row");
            _skillsTargetField = rootVisualElement.Q<EnumField>("skills-target-field");
            _groupSkillsToggle = rootVisualElement.Q<Toggle>("group-skills-toggle");
            _groupSkillsLabel = rootVisualElement.Q<Label>("group-skills-label");
            VisualElement skillsTargetRow = rootVisualElement.Q<VisualElement>("skills-target-row");
            VisualElement skillsTargetList = rootVisualElement.Q<VisualElement>("skills-target-list");
            VisualElement skillsStatusDivider = rootVisualElement.Q<VisualElement>("skills-status-divider");
            Label skillsStatusLabel = rootVisualElement.Q<Label>("skills-status-label");
            Button installSkillsButton = rootVisualElement.Q<Button>("install-skills-button");
            _skillsStepPresenter = new SetupWizardSkillsStepPresenter(
                skillsTargetRow,
                skillsTargetList,
                skillsStatusDivider,
                skillsStatusLabel,
                installSkillsButton,
                HandleInstallSkills);

            _suppressAutoShowToggle = rootVisualElement.Q<Toggle>("suppress-auto-show-toggle");
            _openSettingsButton = rootVisualElement.Q<Button>("open-settings-button");
            _closeButton = rootVisualElement.Q<Button>("close-button");
            _githubLinkRow = rootVisualElement.Q<VisualElement>("github-link-row");
            _githubLinkLabel = rootVisualElement.Q<Label>("github-link-label");
            _githubLinkIcon = rootVisualElement.Q<Image>("github-link-icon");
            _mainScrollView = rootVisualElement.Q<ScrollView>();
        }

        private void BindEvents()
        {
            _refreshButton.clicked += () => RefreshUI();
            InitializeSkillsTargetField();
            InitializeGroupSkillsToggle();
            _suppressAutoShowToggle.RegisterValueChangedCallback(evt => HandleSuppressAutoShowChanged(evt.newValue));
            _openSettingsButton.clicked += HandleOpenSettings;
            _closeButton.clicked += HandleClose;
            _githubLinkRow.RegisterCallback<ClickEvent>(_ => HandleOpenGitHub());
            _githubLinkRow.RegisterCallback<MouseEnterEvent>(_ => HandleGitHubHoverChanged(true));
            _githubLinkRow.RegisterCallback<MouseLeaveEvent>(_ => HandleGitHubHoverChanged(false));
            ConfigureScrollView();
            InitializeGitHubIcon();
        }

        private void ConfigureScrollView()
        {
            Debug.Assert(_mainScrollView != null, "mainScrollView must not be null");
            _mainScrollView.verticalScrollerVisibility = ScrollerVisibility.Auto;
            _mainScrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        }

        private void InitializeGitHubIcon()
        {
            string iconPath = $"{UnityCliLoopConstants.PackageAssetPath}/{GITHUB_ICON_RELATIVE_PATH}";
            Texture2D iconTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(iconPath);
            Debug.Assert(iconTexture != null, $"GitHub icon not found at {iconPath}");
            _githubLinkIcon.image = iconTexture;
        }

        private void InitializeSkillsTargetField()
        {
            if (_isSkillsTargetFieldInitialized) return;

            _skillsTargetField.Init(_skillsTarget);
            _skillsTargetField.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue is SkillsTarget newTarget)
                {
                    _skillsTarget = newTarget;
                    RefreshSkillsSection();
                }
            });
            _isSkillsTargetFieldInitialized = true;
        }

        private void InitializeGroupSkillsToggle()
        {
            ApplyFlatSkillInstallPreference();
            ViewDataBinder.SetVisible(_groupSkillsRow, false);
            _groupSkillsToggle.SetValueWithoutNotify(!_installSkillsFlat);
            _groupSkillsToggle.RegisterValueChangedCallback(evt =>
            {
                evt.StopPropagation();
                ApplyFlatSkillInstallPreference();
                RefreshSkillsSection();
            });
            _groupSkillsLabel.RegisterCallback<ClickEvent>(HandleGroupSkillsRowClicked);
        }

        private void BindSizeUpdates()
        {
            _resizer.BindSizeUpdates();
        }

        private void RefreshAutoShowToggle()
        {
            _suppressAutoShowToggle.SetValueWithoutNotify(_editorSettingsPort.GetSuppressSetupWizardAutoShow());
        }

        private void ApplyInitialCheckingState()
        {
            RefreshAutoShowToggle();
            ViewDataBinder.SetVisible(_nodejsWarning, false);
            ViewDataBinder.SetVisible(_nodejsOk, false);
            _cliStepPresenter.ShowChecking();
            ViewDataBinder.SetVisible(_groupSkillsRow, false);
            _groupSkillsToggle.SetEnabled(false);
            _skillsStepPresenter.ShowChecking(_shouldUseFirstInstallSkillsUi);
        }

        private void ScheduleInitialRefresh()
        {
            _initialRefreshScheduledItem?.Pause();
            _initialRefreshScheduledItem = rootVisualElement.schedule.Execute(() => RefreshUI()).StartingIn(0);
        }

        private void RefreshSkillsSection()
        {
            string cachedCliVersion = _cliSetupApplicationService.GetCachedCliVersion();
            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            bool cliInstalled = IsCliInstalled(cachedCliVersion);
            List<SkillSetupTargetInfo> targets = DetectDisplayedSkillTargetsFast(projectRoot);
            bool canManageSkills = CanManageSkills(cliInstalled);
            UpdateSkillsStep(canManageSkills, targets);
            BeginRefreshDisplayedSkillTargets(canManageSkills);
            ScheduleResizeToContent();
        }

        private async void RefreshUI(bool refreshSkillsSection = true)
        {
            CancelSkillInstallStateRefresh();
            RefreshAutoShowToggle();
            ViewDataBinder.SetVisible(_nodejsWarning, false);
            ViewDataBinder.SetVisible(_nodejsOk, false);
            _cliStepPresenter.ShowChecking();
            if (refreshSkillsSection)
            {
                ViewDataBinder.SetVisible(_groupSkillsRow, false);
                _groupSkillsToggle.SetEnabled(false);
                _skillsStepPresenter.ShowChecking(_shouldUseFirstInstallSkillsUi);
            }

            await Task.Yield();

            ViewDataBinder.SetVisible(_nodejsWarning, false);
            ViewDataBinder.SetVisible(_nodejsOk, false);

            await _cliSetupApplicationService.ForceRefreshCliVersionAsync(CancellationToken.None);
            string cliVersion = _cliSetupApplicationService.GetCachedCliVersion();
            bool cliIsDispatcher = _cliSetupApplicationService.GetCachedCliIsDispatcher();
            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            string requiredCliVersion = GetMinimumRequiredCliVersion();
            bool cliInstalled = IsCliInstalled(cliVersion);
            _needsCliPathSetup = await ShouldRepairCliPathSetupAsync(CancellationToken.None);

            _cliStepPresenter.Update(
                cliInstalled,
                cliVersion,
                cliIsDispatcher,
                requiredCliVersion,
                _isInstallingCli,
                _needsCliPathSetup);

            if (!refreshSkillsSection)
            {
                ScheduleResizeToContent();
                return;
            }

            List<SkillSetupTargetInfo> targets = DetectDisplayedSkillTargetsFast(projectRoot);
            bool canManageSkills = CanManageSkills(cliInstalled);
            UpdateSkillsStep(canManageSkills, targets);
            BeginRefreshDisplayedSkillTargets(canManageSkills);

            ScheduleResizeToContent();
        }

        private List<SkillSetupTargetInfo> DetectDisplayedSkillTargets(string projectRoot)
        {
            return _skillSetupUseCase.DetectSkillTargetsForLayoutAtProjectRoot(projectRoot, !_installSkillsFlat);
        }

        private List<SkillSetupTargetInfo> DetectDisplayedSkillTargetsFast(string projectRoot)
        {
            return _skillSetupUseCase.DetectSkillTargetsForLayoutFastAtProjectRoot(projectRoot, !_installSkillsFlat);
        }

        private void BeginRefreshDisplayedSkillTargets(bool canManageSkills)
        {
            CancelSkillInstallStateRefresh();
            if (!canManageSkills || _isInstallingSkills)
            {
                return;
            }

            CancellationTokenSource cts = new();
            _skillInstallStateRefreshCts = cts;
            RefreshDisplayedSkillTargetsAsync(cts.Token);
        }

        private async void RefreshDisplayedSkillTargetsAsync(CancellationToken ct)
        {
            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            List<SkillSetupTargetInfo> targets =
                await Task.Run(() => DetectDisplayedSkillTargets(projectRoot));
            if (ct.IsCancellationRequested)
            {
                return;
            }

            UpdateSkillsStep(canManageSkills: true, targets);
            ScheduleResizeToContent();
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

        internal static bool ShouldShowSkillsInstalledDialog(
            IEnumerable<SkillSetupTargetInfo> targets)
        {
            Debug.Assert(targets != null, "targets must not be null");
            return targets.All(target =>
                target.InstallState != SkillInstallState.Outdated
                && !target.HasDifferentLayoutSkills);
        }

        internal static bool ShouldUseFirstInstallSkillsUi(string lastSeenSetupWizardVersion)
        {
            return string.IsNullOrEmpty(lastSeenSetupWizardVersion);
        }

        internal static bool CanManageSkills(bool cliInstalled)
        {
            return cliInstalled;
        }

        private static async Task<bool> ShouldRepairCliPathSetupAsync(CancellationToken ct)
        {
            CliSetupApplicationService cliSetupApplicationService = GetCliSetupApplicationService();
            bool hasPackageOwnedCurrentUserInstall =
                cliSetupApplicationService.HasPackageOwnedCurrentUserInstall(UnityEngine.Application.platform);
            if (!ShouldCheckCliPathSetupForSetupWizard(
                    UnityEngine.Application.platform,
                    hasPackageOwnedCurrentUserInstall))
            {
                return false;
            }

            bool isCliVisibleFromShell = await cliSetupApplicationService.IsCliVisibleFromShellAsync(
                UnityEngine.Application.platform,
                ct);
            return !isCliVisibleFromShell;
        }

        internal static bool ShouldCheckCliPathSetupForSetupWizard(
            RuntimePlatform platform,
            bool hasPackageOwnedCurrentUserInstall)
        {
            return platform != RuntimePlatform.WindowsEditor
                && hasPackageOwnedCurrentUserInstall;
        }

        private static CliSetupCompatibilityState EvaluateCliSetupCompatibilityForSetupWizard(
            string cliVersion,
            bool cliIsDispatcher)
        {
            return CliSetupCompatibility.Evaluate(
                cliVersion,
                cliIsDispatcher,
                GetMinimumRequiredCliVersion());
        }

        private static string GetMinimumRequiredCliVersion()
        {
            return GetCliSetupApplicationService().GetMinimumRequiredCliVersion();
        }

        private static bool IsCliInstalled(string cliVersion)
        {
            return !string.IsNullOrEmpty(cliVersion);
        }

        private void UpdateSkillsStep(
            bool canManageSkills,
            List<SkillSetupTargetInfo> targets)
        {
            _groupSkillsToggle.SetEnabled(canManageSkills && !_isInstallingSkills);
            _skillsStepPresenter.Update(
                canManageSkills,
                targets,
                _shouldUseFirstInstallSkillsUi,
                _skillsTarget,
                !_installSkillsFlat,
                _isInstallingSkills);
        }

        private async void HandleInstallCli()
        {
            await RefreshCliPrimaryActionStateAsync(CancellationToken.None);

            string cliVersion = _cliSetupApplicationService.GetCachedCliVersion();
            bool cliIsDispatcher = _cliSetupApplicationService.GetCachedCliIsDispatcher();
            CliSetupCompatibilityState state = EvaluateCliSetupCompatibilityForSetupWizard(
                cliVersion,
                cliIsDispatcher);
            if (ShouldRepairCliPathFromPrimaryButton(_needsCliPathSetup, state.NeedsUpdate))
            {
                await HandleRepairCliPathSetup();
                return;
            }

            bool wasCliInstalledBeforeInstall = _cliSetupApplicationService.IsCliInstalled();
            _needsCliPathSetup = false;
            _isInstallingCli = true;
            _cliStepPresenter.Update(
                cliInstalled: false,
                cliVersion: null,
                cliIsDispatcher: false,
                requiredCliVersion: GetMinimumRequiredCliVersion(),
                isInstallingCli: _isInstallingCli,
                needsCliPathSetup: _needsCliPathSetup);

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
                        $"Failed to install uloop CLI.\n\n{result.ErrorOutput}\n\n"
                        + $"You can install manually:\n  {command.ManualCommand}",
                        "OK");
                    return;
                }

                await CliPathSetupPrompt.EnsureVisibleAndShowResultAsync(
                    UnityEngine.Application.platform,
                    _cliSetupApplicationService,
                    CancellationToken.None);
                _needsCliPathSetup = await ShouldRepairCliPathSetupAsync(CancellationToken.None);
            }
            finally
            {
                _isInstallingCli = false;
                RefreshUI(CliInstallRefreshPolicy.ShouldRefreshSkillsAfterCliInstall(
                    wasCliInstalledBeforeInstall));
            }
        }

        private async Task RefreshCliPrimaryActionStateAsync(CancellationToken ct)
        {
            _cliStepPresenter.ShowRefreshingPrimaryAction();

            try
            {
                await _cliSetupApplicationService.ForceRefreshCliVersionAsync(ct);
                _needsCliPathSetup = await ShouldRepairCliPathSetupAsync(ct);
            }
            finally
            {
                RefreshCliStepFromCachedState();
            }
        }

        private void RefreshCliStepFromCachedState()
        {
            string cliVersion = _cliSetupApplicationService.GetCachedCliVersion();
            bool cliIsDispatcher = _cliSetupApplicationService.GetCachedCliIsDispatcher();
            string requiredCliVersion = GetMinimumRequiredCliVersion();
            bool cliInstalled = IsCliInstalled(cliVersion);
            _cliStepPresenter.Update(
                cliInstalled,
                cliVersion,
                cliIsDispatcher,
                requiredCliVersion,
                _isInstallingCli,
                _needsCliPathSetup);
        }

        internal static bool ShouldRepairCliPathFromPrimaryButton(
            bool needsCliPathSetup,
            bool needsUpdate)
        {
            return CliSetupPrimaryActionPolicy.ShouldRepairCliPath(needsCliPathSetup, needsUpdate);
        }

        private async Task HandleRepairCliPathSetup()
        {
            _isInstallingCli = true;
            _cliStepPresenter.Update(
                cliInstalled: true,
                cliVersion: _cliSetupApplicationService.GetCachedCliVersion(),
                cliIsDispatcher: _cliSetupApplicationService.GetCachedCliIsDispatcher(),
                requiredCliVersion: GetMinimumRequiredCliVersion(),
                isInstallingCli: _isInstallingCli,
                needsCliPathSetup: _needsCliPathSetup);

            try
            {
                await CliPathSetupPrompt.EnsureVisibleAndShowResultAsync(
                    UnityEngine.Application.platform,
                    _cliSetupApplicationService,
                    CancellationToken.None);
                _needsCliPathSetup = await ShouldRepairCliPathSetupAsync(CancellationToken.None);
            }
            finally
            {
                _isInstallingCli = false;
                RefreshUI();
            }
        }

        private async void HandleInstallSkills()
        {
            CancelSkillInstallStateRefresh();
            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            List<SkillSetupTargetInfo> targets = DetectDisplayedSkillTargets(projectRoot);
            List<SkillSetupTargetInfo> installableTargets = _shouldUseFirstInstallSkillsUi
                ? SetupWizardSkillsStepPresenter.GetFirstInstallableSkillTargets(
                    targets,
                    _skillsTarget,
                    !_installSkillsFlat)
                : SetupWizardSkillsStepPresenter.FilterInstallableSkillTargets(targets);
            if (installableTargets.Count == 0) return;

            bool shouldShowSkillsInstalledDialog = ShouldShowSkillsInstalledDialog(installableTargets);
            _isInstallingSkills = true;
            UpdateSkillsStep(true, targets);

            try
            {
                await _skillSetupUseCase.InstallSkillFilesAsync(
                    installableTargets,
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
                RefreshSkillsSection();
            }
        }

        private void HandleOpenSettings()
        {
            UnityCliLoopSettingsWindow.ShowWindow();
        }

        private void HandleSuppressAutoShowChanged(bool suppressAutoShow)
        {
            _editorSettingsPort.SetSuppressSetupWizardAutoShow(suppressAutoShow);
            SetupWizardStartupFlow.MaybeRecordSuppressedSetupWizardState(
                _editorSettingsPort,
                suppressAutoShow,
                UnityCliLoopConstants.PackageInfo.version,
                GetMinimumRequiredCliVersion());
            ScheduleResizeToContent();
        }

        private void HandleClose()
        {
            Close();
        }

        private void HandleOpenGitHub()
        {
            UnityEngine.Application.OpenURL(GetGitHubRepositoryUrl());
        }

        private void HandleGitHubHoverChanged(bool isHovered)
        {
            ViewDataBinder.ToggleClass(_githubLinkRow, "setup-footer__github-link--hover", isHovered);
            ViewDataBinder.ToggleClass(_githubLinkLabel, "setup-footer__github-link-label--hover", isHovered);
            ViewDataBinder.ToggleClass(_githubLinkIcon, "setup-footer__github-link-icon--hover", isHovered);
        }

        private void HandleGroupSkillsRowClicked(ClickEvent evt)
        {
            evt.StopPropagation();
            if (!_groupSkillsToggle.enabledSelf)
            {
                return;
            }

            if (evt.target is VisualElement targetElement && _groupSkillsToggle.Contains(targetElement))
            {
                return;
            }

            bool newValue = !_groupSkillsToggle.value;
            _groupSkillsToggle.SetValueWithoutNotify(newValue);
            ApplyFlatSkillInstallPreference();
            RefreshSkillsSection();
        }

        private void ApplyFlatSkillInstallPreference()
        {
            // Claude Code does not resolve nested skill folders, so setup keeps every editor target on the flat layout.
            _installSkillsFlat = ForceFlatSkillInstall;
            _editorSettingsPort.SetInstallSkillsFlat(_installSkillsFlat);
        }

        private void ScheduleResizeToContent()
        {
            _resizer?.ScheduleResizeToContent();
        }

    }
}
