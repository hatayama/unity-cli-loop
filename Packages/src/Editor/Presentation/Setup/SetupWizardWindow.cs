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

        // Step 1
        private VisualElement _cliStatusIcon;
        private Label _cliStatusLabel;
        private Button _installCliButton;

        // Step 2
        private VisualElement _groupSkillsRow;
        private VisualElement _skillsTargetRow;
        private EnumField _skillsTargetField;
        private Toggle _groupSkillsToggle;
        private Label _groupSkillsLabel;
        private VisualElement _skillsTargetList;
        private VisualElement _skillsStatusDivider;
        private Label _skillsStatusLabel;
        private Button _installSkillsButton;

        // Footer
        private Toggle _suppressAutoShowToggle;
        private Button _openSettingsButton;
        private Button _closeButton;
        private VisualElement _githubLinkRow;
        private Label _githubLinkLabel;
        private Image _githubLinkIcon;
        private ScrollView _mainScrollView;

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

            _cliStatusIcon = rootVisualElement.Q<VisualElement>("cli-status-icon");
            _cliStatusLabel = rootVisualElement.Q<Label>("cli-status-label");
            _installCliButton = rootVisualElement.Q<Button>("install-cli-button");

            _groupSkillsRow = rootVisualElement.Q<VisualElement>("group-skills-row");
            _skillsTargetRow = rootVisualElement.Q<VisualElement>("skills-target-row");
            _skillsTargetField = rootVisualElement.Q<EnumField>("skills-target-field");
            _groupSkillsToggle = rootVisualElement.Q<Toggle>("group-skills-toggle");
            _groupSkillsLabel = rootVisualElement.Q<Label>("group-skills-label");
            _skillsTargetList = rootVisualElement.Q<VisualElement>("skills-target-list");
            _skillsStatusDivider = rootVisualElement.Q<VisualElement>("skills-status-divider");
            _skillsStatusLabel = rootVisualElement.Q<Label>("skills-status-label");
            _installSkillsButton = rootVisualElement.Q<Button>("install-skills-button");

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
            _installCliButton.clicked += HandleInstallCli;
            _installSkillsButton.clicked += HandleInstallSkills;
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
            ViewDataBinder.ToggleClass(_cliStatusIcon, "setup-status-icon--success", false);
            ViewDataBinder.ToggleClass(_cliStatusIcon, "setup-status-icon--pending", true);
            _cliStatusLabel.text = "Checking...";
            _installCliButton.SetEnabled(false);
            _installCliButton.text = "Checking...";
            ViewDataBinder.SetVisible(_groupSkillsRow, false);
            _groupSkillsToggle.SetEnabled(false);
            UpdateSkillsStatusLabel("Checking installed skills...");
            _installSkillsButton.SetEnabled(false);
            _installSkillsButton.text = "Checking...";
            ViewDataBinder.SetVisible(_skillsTargetRow, _shouldUseFirstInstallSkillsUi);
            ViewDataBinder.SetVisible(_skillsTargetList, !_shouldUseFirstInstallSkillsUi);
            _skillsTargetList.Clear();
        }

        private void UpdateSkillsStatusLabel(string text)
        {
            _skillsStatusLabel.text = text;
            bool isVisible = !string.IsNullOrEmpty(text);
            ViewDataBinder.SetVisible(_skillsStatusDivider, isVisible);
            ViewDataBinder.SetVisible(_skillsStatusLabel, isVisible);
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
            ViewDataBinder.ToggleClass(_cliStatusIcon, "setup-status-icon--success", false);
            ViewDataBinder.ToggleClass(_cliStatusIcon, "setup-status-icon--pending", true);
            _cliStatusLabel.text = "Checking...";
            _installCliButton.SetEnabled(false);
            _installCliButton.text = "Checking...";
            if (refreshSkillsSection)
            {
                ViewDataBinder.SetVisible(_groupSkillsRow, false);
                _groupSkillsToggle.SetEnabled(false);
                UpdateSkillsStatusLabel("Checking installed skills...");
                _installSkillsButton.SetEnabled(false);
                _installSkillsButton.text = "Checking...";
                ViewDataBinder.SetVisible(_skillsTargetRow, _shouldUseFirstInstallSkillsUi);
                ViewDataBinder.SetVisible(_skillsTargetList, !_shouldUseFirstInstallSkillsUi);
                _skillsTargetList.Clear();
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

            UpdateCliStep(cliInstalled, cliVersion, cliIsDispatcher, requiredCliVersion);

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

        internal static List<SkillSetupTargetInfo> FilterInstallableSkillTargets(
            IEnumerable<SkillSetupTargetInfo> targets)
        {
            Debug.Assert(targets != null, "targets must not be null");
            return targets
                .Where(target => target.HasSkillsDirectory)
                .ToList();
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

        internal static bool ShouldShowSkillsTargetRowForSetupWizard(bool shouldUseFirstInstallSkillsUi)
        {
            return shouldUseFirstInstallSkillsUi;
        }

        internal static bool ShouldShowSkillsTargetListForSetupWizard(
            bool canManageSkills,
            bool shouldUseFirstInstallSkillsUi)
        {
            return canManageSkills && !shouldUseFirstInstallSkillsUi;
        }

        internal static SkillSetupTargetInfo CreateFirstInstallSkillTarget(
            SkillsTarget target,
            bool groupSkillsUnderUnityCliLoop)
        {
            SkillsTargetSelection selection = SkillsTargetSelectionResolver.Resolve(
                target,
                groupSkillsUnderUnityCliLoop);
            return new(
                selection.DisplayName,
                selection.DirectoryName,
                selection.InstallFlag,
                hasSkillsDirectory: false,
                hasExistingSkills: false,
                hasDifferentLayoutSkills: false,
                SkillInstallState.Missing);
        }

        internal static SkillSetupTargetInfo GetSelectedSkillTargetInfo(
            IEnumerable<SkillSetupTargetInfo> targets,
            SkillsTarget target,
            bool groupSkillsUnderUnityCliLoop)
        {
            Debug.Assert(targets != null, "targets must not be null");

            SkillsTargetSelection selection = SkillsTargetSelectionResolver.Resolve(
                target,
                groupSkillsUnderUnityCliLoop);
            SkillSetupTargetInfo selectedTargetInfo = targets
                .FirstOrDefault(info => info.DirName == selection.DirectoryName);
            return string.IsNullOrEmpty(selectedTargetInfo.DirName)
                ? CreateFirstInstallSkillTarget(target, groupSkillsUnderUnityCliLoop)
                : selectedTargetInfo;
        }

        internal static List<SkillSetupTargetInfo> GetFirstInstallableSkillTargets(
            IEnumerable<SkillSetupTargetInfo> targets,
            SkillsTarget target,
            bool groupSkillsUnderUnityCliLoop)
        {
            SkillSetupTargetInfo selectedTargetInfo = GetSelectedSkillTargetInfo(
                targets,
                target,
                groupSkillsUnderUnityCliLoop);
            return selectedTargetInfo.InstallState == SkillInstallState.Installed
                   || selectedTargetInfo.InstallState == SkillInstallState.Checking
                ? new List<SkillSetupTargetInfo>()
                : new List<SkillSetupTargetInfo> { selectedTargetInfo };
        }

        private void UpdateCliStep(
            bool cliInstalled,
            string cliVersion,
            bool cliIsDispatcher,
            string requiredCliVersion)
        {
            CliSetupCompatibilityState state = CliSetupCompatibility.Evaluate(
                cliVersion,
                cliIsDispatcher,
                requiredCliVersion);
            string buttonText = GetCliButtonTextForSetupWizard(
                cliInstalled,
                _isInstallingCli,
                false,
                state.NeedsUpdate,
                _needsCliPathSetup,
                cliVersion,
                requiredCliVersion);
            bool cliVersionMatched = state.IsCompatible && cliInstalled;
            bool buttonEnabled = IsCliButtonEnabledForSetupWizard(
                cliInstalled,
                cliVersionMatched,
                _needsCliPathSetup,
                _isInstallingCli,
                isChecking: false);

            bool cliCompatible = cliInstalled && cliVersionMatched;
            _cliStatusLabel.text = GetCliStatusTextForSetupWizard(
                cliInstalled,
                cliCompatible,
                cliVersion,
                requiredCliVersion);
            ViewDataBinder.ToggleClass(_cliStatusIcon, "setup-status-icon--success", cliCompatible);
            ViewDataBinder.ToggleClass(_cliStatusIcon, "setup-status-icon--pending", !cliCompatible);
            _installCliButton.SetEnabled(buttonEnabled);
            _installCliButton.text = buttonText;
        }

        internal static string GetCliStatusTextForSetupWizard(
            bool cliInstalled,
            bool cliCompatible,
            string cliVersion,
            string requiredCliVersion)
        {
            if (!cliInstalled)
            {
                return "Not installed";
            }

            if (cliCompatible)
            {
                return $"v{cliVersion}";
            }

            if (CliSetupLabelFormatter.ShouldShowRequiredVersionText(cliVersion, requiredCliVersion))
            {
                return $"v{cliVersion} (update required)";
            }

            return $"v{cliVersion} (requires v{requiredCliVersion})";
        }

        internal static string GetCliButtonTextForSetupWizard(
            bool cliInstalled,
            bool isInstallingCli,
            bool isChecking,
            bool needsUpdate,
            bool needsCliPathSetup,
            string cliVersion,
            string requiredCliVersion)
        {
            if (isChecking)
            {
                return "Checking...";
            }

            if (isInstallingCli)
            {
                if (CliSetupPrimaryActionPolicy.ShouldRepairCliPath(needsCliPathSetup, needsUpdate))
                {
                    return "Fixing PATH...";
                }

                return "Installing...";
            }

            if (needsUpdate)
            {
                return CliSetupLabelFormatter.GetCliReplacementButtonText("Update", cliVersion, requiredCliVersion);
            }

            if (needsCliPathSetup)
            {
                return "Fix PATH";
            }

            if (!cliInstalled)
            {
                return "Install CLI";
            }

            return "Installed";
        }

        internal static bool IsCliButtonEnabledForSetupWizard(
            bool cliInstalled,
            bool cliVersionMatched,
            bool needsCliPathSetup,
            bool isInstallingCli,
            bool isChecking)
        {
            return !isInstallingCli && !isChecking && (!cliInstalled || !cliVersionMatched || needsCliPathSetup);
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
            _skillsTargetList.Clear();
            bool useFirstInstallSkillsUi = _shouldUseFirstInstallSkillsUi;
            ViewDataBinder.SetVisible(
                _skillsTargetRow,
                ShouldShowSkillsTargetRowForSetupWizard(useFirstInstallSkillsUi));
            ViewDataBinder.SetVisible(
                _skillsTargetList,
                ShouldShowSkillsTargetListForSetupWizard(canManageSkills, useFirstInstallSkillsUi));

            if (!canManageSkills)
            {
                UpdateSkillsStatusLabel(string.Empty);
                _installSkillsButton.SetEnabled(false);
                _installSkillsButton.text = GetSkillsButtonTextForSetupWizard(
                    cliInstalled: false,
                    _isInstallingSkills,
                    hasOutdatedSkills: false);
                _groupSkillsToggle.SetEnabled(false);
                return;
            }

            _groupSkillsToggle.SetEnabled(!_isInstallingSkills);

            if (useFirstInstallSkillsUi)
            {
                SkillSetupTargetInfo selectedTargetInfo = GetSelectedSkillTargetInfo(
                    targets,
                    _skillsTarget,
                    !_installSkillsFlat);
                UpdateSkillsStatusLabel(string.Empty);
                _installSkillsButton.text = CliSetupSection.GetInstallSkillsButtonText(
                    isCliInstalled: true,
                    _isInstallingSkills,
                    selectedTargetInfo.InstallState);
                _installSkillsButton.SetEnabled(CliSetupSection.IsInstallSkillsButtonEnabled(
                    isCliInstalled: true,
                    _isInstallingSkills,
                    selectedTargetInfo.InstallState));
                return;
            }

            List<SkillSetupTargetInfo> installableTargets = FilterInstallableSkillTargets(targets);

            foreach (SkillSetupTargetInfo target in installableTargets)
            {
                VisualElement item = new();
                item.AddToClassList("setup-target-item");

                Label nameLabel = new($"{target.DisplayName} ({target.DirName}/)");
                nameLabel.AddToClassList("setup-target-item__label");
                item.Add(nameLabel);

                Label statusLabel = new(GetSkillInstallStatusText(
                    target.InstallState,
                    target.HasDifferentLayoutSkills,
                    !_installSkillsFlat));
                statusLabel.AddToClassList("setup-target-item__status");
                statusLabel.AddToClassList(GetSkillInstallStatusClass(
                    target.InstallState,
                    target.HasDifferentLayoutSkills,
                    !_installSkillsFlat));
                item.Add(statusLabel);

                _skillsTargetList.Add(item);
            }

            if (installableTargets.Count == 0)
            {
                UpdateSkillsStatusLabel(
                    "Create a tool folder to enable skill installation (.claude/, .agents/, etc.)");
                _installSkillsButton.SetEnabled(false);
                _installSkillsButton.text = "Install Skills";
                return;
            }

            bool isCheckingSkills = installableTargets.Any(
                t => t.InstallState == SkillInstallState.Checking);
            if (isCheckingSkills)
            {
                UpdateSkillsStatusLabel("Checking installed skills...");
                _installSkillsButton.SetEnabled(false);
                _installSkillsButton.text = "Checking...";
                return;
            }

            bool allSkillsInstalled = installableTargets.All(
                t => t.InstallState == SkillInstallState.Installed);
            if (allSkillsInstalled)
            {
                UpdateSkillsStatusLabel($"Installed for {installableTargets.Count} targets");
                _installSkillsButton.SetEnabled(false);
                _installSkillsButton.text = "Installed";
            }
            else
            {
                bool hasOutdatedSkills = installableTargets.Any(
                    t => t.InstallState == SkillInstallState.Outdated);
                UpdateSkillsStatusLabel(string.Empty);
                _installSkillsButton.SetEnabled(!_isInstallingSkills);
                _installSkillsButton.text = GetSkillsButtonTextForSetupWizard(
                    cliInstalled: true,
                    _isInstallingSkills,
                    hasOutdatedSkills);
            }
        }

        internal static string GetSkillsButtonTextForSetupWizard(
            bool cliInstalled,
            bool isInstallingSkills,
            bool hasOutdatedSkills)
        {
            return !cliInstalled
                ? "Install Skills"
                : GetInstallSkillsButtonText(isInstallingSkills, hasOutdatedSkills);
        }

        internal static string GetInstallSkillsButtonText(
            bool isInstallingSkills,
            bool hasOutdatedSkills)
        {
            if (isInstallingSkills)
            {
                return "Installing...";
            }

            return hasOutdatedSkills ? "Update Skills" : "Install Skills";
        }

        internal static string GetSkillInstallStatusText(
            SkillInstallState installState,
            bool hasDifferentLayoutSkills,
            bool groupSkillsUnderUnityCliLoop)
        {
            if (installState == SkillInstallState.Checking)
            {
                return "Checking...";
            }

            if (installState == SkillInstallState.Installed)
            {
                return "Installed";
            }

            if (installState == SkillInstallState.Outdated)
            {
                return "Outdated";
            }

            if (!hasDifferentLayoutSkills)
            {
                return "Missing";
            }

            return groupSkillsUnderUnityCliLoop ? "Not grouped" : "Grouped";
        }

        internal static string GetSkillInstallStatusClass(
            SkillInstallState installState,
            bool hasDifferentLayoutSkills,
            bool groupSkillsUnderUnityCliLoop)
        {
            if (installState == SkillInstallState.Checking)
            {
                return "setup-target-item__status--checking";
            }

            if (installState == SkillInstallState.Installed)
            {
                return "setup-target-item__status--installed";
            }

            if (installState == SkillInstallState.Outdated)
            {
                return "setup-target-item__status--outdated";
            }

            if (!hasDifferentLayoutSkills)
            {
                return "setup-target-item__status--missing";
            }

            return groupSkillsUnderUnityCliLoop
                ? "setup-target-item__status--different-layout"
                : "setup-target-item__status--different-layout";
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
            UpdateCliStep(false, null, false, GetMinimumRequiredCliVersion());

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
            _installCliButton.SetEnabled(false);
            _installCliButton.text = "Checking...";

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
            UpdateCliStep(cliInstalled, cliVersion, cliIsDispatcher, requiredCliVersion);
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
            UpdateCliStep(
                cliInstalled: true,
                cliVersion: _cliSetupApplicationService.GetCachedCliVersion(),
                cliIsDispatcher: _cliSetupApplicationService.GetCachedCliIsDispatcher(),
                requiredCliVersion: GetMinimumRequiredCliVersion());

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
                ? GetFirstInstallableSkillTargets(targets, _skillsTarget, !_installSkillsFlat)
                : FilterInstallableSkillTargets(targets);
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
