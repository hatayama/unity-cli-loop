using System.Collections.Generic;

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
            IThirdPartyToolMigrationAutoScanSeedRepository autoScanSeedRepository,
            CliSetupApplicationService cliSetupApplicationService,
            SkillSetupUseCase skillSetupUseCase,
            ThirdPartyToolMigrationUseCase thirdPartyToolMigrationUseCase)
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
                autoScanSeedRepository,
                cliSetupApplicationService,
                skillSetupUseCase,
                thirdPartyToolMigrationUseCase,
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

        private Button _refreshButton;
        private Toggle _suppressAutoShowToggle;
        private Button _openSettingsButton;
        private Button _closeButton;
        private VisualElement _githubLinkRow;
        private Label _githubLinkLabel;
        private Image _githubLinkIcon;
        private ScrollView _mainScrollView;
        private SetupWizardWorkflowController _controller;

        [SerializeField]
        private string _lastSeenSetupWizardVersionBeforeOpen = string.Empty;
        [SerializeField]
        private bool _shouldRecordLastSeenVersionAfterCreateGui;
        private SkillSetupUseCase _skillSetupUseCase;
        private IUnityCliLoopEditorSettingsPort _editorSettingsPort;
        private CliSetupApplicationService _cliSetupApplicationService;
        private SetupWizardWindowResizer _resizer;

        private void CreateGUI()
        {
            InitializeApplicationServices();
            LoadLayout();
            BindElements();
            _resizer = new SetupWizardWindowResizer(this, _mainScrollView);
            BindEvents();
            BindSizeUpdates();
            _controller.ApplyInitialCheckingState();
            _controller.ScheduleInitialRefresh();
            ScheduleResizeToContent();
            RecordLastSeenSetupWizardStateAfterSuccessfulCreateGui();
        }

        private void InitializeApplicationServices()
        {
            _skillSetupUseCase = GetSkillSetupUseCase();
            _editorSettingsPort = GetEditorSettingsPort();
            _cliSetupApplicationService = GetCliSetupApplicationService();
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
            _controller?.PauseInitialRefresh();
            _resizer?.Pause();
            _controller?.CancelSkillInstallStateRefresh();
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
            VisualElement nodejsWarning = rootVisualElement.Q<VisualElement>("nodejs-warning");
            VisualElement nodejsOk = rootVisualElement.Q<VisualElement>("nodejs-ok");
            _refreshButton = rootVisualElement.Q<Button>("refresh-button");

            VisualElement cliStatusIcon = rootVisualElement.Q<VisualElement>("cli-status-icon");
            Label cliStatusLabel = rootVisualElement.Q<Label>("cli-status-label");
            Button installCliButton = rootVisualElement.Q<Button>("install-cli-button");

            VisualElement groupSkillsRow = rootVisualElement.Q<VisualElement>("group-skills-row");
            EnumField skillsTargetField = rootVisualElement.Q<EnumField>("skills-target-field");
            Toggle groupSkillsToggle = rootVisualElement.Q<Toggle>("group-skills-toggle");
            Label groupSkillsLabel = rootVisualElement.Q<Label>("group-skills-label");
            VisualElement skillsTargetRow = rootVisualElement.Q<VisualElement>("skills-target-row");
            VisualElement skillsTargetList = rootVisualElement.Q<VisualElement>("skills-target-list");
            VisualElement skillsStatusDivider = rootVisualElement.Q<VisualElement>("skills-status-divider");
            Label skillsStatusLabel = rootVisualElement.Q<Label>("skills-status-label");
            Button installSkillsButton = rootVisualElement.Q<Button>("install-skills-button");

            _suppressAutoShowToggle = rootVisualElement.Q<Toggle>("suppress-auto-show-toggle");
            _openSettingsButton = rootVisualElement.Q<Button>("open-settings-button");
            _closeButton = rootVisualElement.Q<Button>("close-button");
            _githubLinkRow = rootVisualElement.Q<VisualElement>("github-link-row");
            _githubLinkLabel = rootVisualElement.Q<Label>("github-link-label");
            _githubLinkIcon = rootVisualElement.Q<Image>("github-link-icon");
            _mainScrollView = rootVisualElement.Q<ScrollView>();

            _controller = new SetupWizardWorkflowController(
                rootVisualElement,
                nodejsWarning,
                nodejsOk,
                cliStatusIcon,
                cliStatusLabel,
                installCliButton,
                groupSkillsRow,
                skillsTargetField,
                groupSkillsToggle,
                groupSkillsLabel,
                skillsTargetRow,
                skillsTargetList,
                skillsStatusDivider,
                skillsStatusLabel,
                installSkillsButton,
                _suppressAutoShowToggle,
                _skillSetupUseCase,
                _editorSettingsPort,
                _cliSetupApplicationService,
                ScheduleResizeToContent,
                _lastSeenSetupWizardVersionBeforeOpen);
        }

        private void BindEvents()
        {
            _refreshButton.clicked += () => _controller.RefreshUI();
            _controller.InitializeSkillsTargetField();
            _controller.InitializeGroupSkillsToggle();
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

        private void BindSizeUpdates()
        {
            _resizer.BindSizeUpdates();
        }

        internal static bool ShouldShowSkillsInstalledDialog(
            IEnumerable<SkillSetupTargetInfo> targets)
        {
            return SkillInstallDialogPolicy.ShouldShowForInstallableTargets(targets);
        }

        internal static bool ShouldUseFirstInstallSkillsUi(string lastSeenSetupWizardVersion)
        {
            return string.IsNullOrEmpty(lastSeenSetupWizardVersion);
        }

        internal static bool CanManageSkills(bool cliInstalled)
        {
            return cliInstalled;
        }

        internal static bool ShouldCheckCliPathSetupForSetupWizard(
            RuntimePlatform platform,
            bool hasPackageOwnedCurrentUserInstall)
        {
            return CliPathSetupCheckPolicy.ShouldCheck(
                isWindowsEditor: platform == RuntimePlatform.WindowsEditor,
                hasPackageOwnedCurrentUserInstall);
        }

        private static string GetMinimumRequiredCliVersion()
        {
            return GetCliSetupApplicationService().GetMinimumRequiredCliVersion();
        }

        internal static bool ShouldRepairCliPathFromPrimaryButton(
            bool needsCliPathSetup,
            bool needsUpdate)
        {
            return CliSetupPrimaryActionPolicy.ShouldRepairCliPath(needsCliPathSetup, needsUpdate);
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

        private void ScheduleResizeToContent()
        {
            _resizer?.ScheduleResizeToContent();
        }
    }
}
