using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using RuntimePlatform = UnityEngine.RuntimePlatform;

namespace io.github.hatayama.UnityCliLoop
{
    public class SetupWizardWindow : EditorWindow
    {
        private const string WindowTitle = "Unity CLI Loop Setup";
        private const string UXML_RELATIVE_PATH = "Editor/UI/Setup/SetupWizardWindow.uxml";
        private const string USS_RELATIVE_PATH = "Editor/UI/Setup/SetupWizardWindow.uss";
        private const string GITHUB_ICON_RELATIVE_PATH = "Editor/UI/Setup/GitHub_Invertocat_White.png";
        private const int PreferredWrappedTextLineCount = 2;
        private const bool ForceFlatSkillInstall = true;
        private static readonly Vector2 MinimumWindowSize = new(360f, 380f);

        [InitializeOnLoadMethod]
        private static void InitializeOnLoad()
        {
            if (AssetDatabase.IsAssetImportWorkerProcess()) return;
            if (Application.isBatchMode) return;

            TryShowOnVersionChange();
        }

        [MenuItem("Window/Unity CLI Loop/Setup Wizard", priority = 3)]
        public static void ShowWindow()
        {
            ShowWindowInternal(false);
        }

        internal static bool ShouldAutoShowForVersion(
            string currentVersion,
            string lastSeenVersion,
            bool suppressAutoShow)
        {
            if (suppressAutoShow) return false;

            return !string.Equals(currentVersion, lastSeenVersion, System.StringComparison.Ordinal);
        }

        internal static void MaybeRecordLastSeenVersion(bool shouldRecordVersion, string version)
        {
            if (!shouldRecordVersion) return;

            Debug.Assert(!string.IsNullOrEmpty(version), "version must not be null or empty");
            McpEditorSettings.SetLastSeenSetupWizardVersion(version);
        }

        internal static void MaybeRecordSuppressedVersion(bool suppressAutoShow, string version)
        {
            if (!suppressAutoShow) return;

            Debug.Assert(!string.IsNullOrEmpty(version), "version must not be null or empty");
            McpEditorSettings.SetLastSeenSetupWizardVersion(version);
        }

        private static void TryShowOnVersionChange()
        {
            string currentVersion = McpConstants.PackageInfo.version;
            bool suppressAutoShow = McpEditorSettings.GetSuppressSetupWizardAutoShow();
            MaybeRecordSuppressedVersion(suppressAutoShow, currentVersion);
            string lastSeenVersion = McpEditorSettings.GetLastSeenSetupWizardVersion();
            if (!ShouldAutoShowForVersion(currentVersion, lastSeenVersion, suppressAutoShow)) return;

            EditorApplication.delayCall += ShowWindowOnVersionChange;
        }

        private static void ShowWindowOnVersionChange()
        {
            ShowWindowInternal(true);
        }

        private static void ShowWindowInternal(bool shouldRecordVersion)
        {
            string currentVersion = McpConstants.PackageInfo.version;
            if (TryReuseOpenWindow(
                HasOpenInstances<SetupWizardWindow>(),
                shouldRecordVersion,
                currentVersion,
                FocusExistingWindow))
            {
                return;
            }

            string lastSeenSetupWizardVersionBeforeOpen = McpEditorSettings.GetLastSeenSetupWizardVersion();
            Rect windowPosition = CreateCenteredRect(EditorGUIUtility.GetMainWindowPosition(), MinimumWindowSize);
            SetupWizardWindow window = CreateInstance<SetupWizardWindow>();
            PrepareForOpen(window, WindowTitle, windowPosition, lastSeenSetupWizardVersionBeforeOpen);
            window.ShowUtility();
            window.ScheduleResizeToContent();
            MaybeRecordLastSeenVersion(shouldRecordVersion, currentVersion);
        }

        internal static Rect WithContentSize(Rect currentRect, Vector2 contentSize, Vector2 frameSize)
        {
            Vector2 measuredSize = contentSize + frameSize;
            Vector2 targetSize = new(
                Mathf.Max(measuredSize.x, MinimumWindowSize.x),
                Mathf.Max(measuredSize.y, MinimumWindowSize.y));
            return CreateCenteredRect(currentRect, targetSize);
        }

        internal static Rect CreateCenteredRect(Rect bounds, Vector2 size)
        {
            Vector2 centeredPosition = bounds.center - (size * 0.5f);
            return new Rect(centeredPosition, size);
        }

        internal static string GetGitHubRepositoryUrl()
        {
            return McpUIConstants.PROJECT_REPOSITORY_URL;
        }

        internal static bool TryReuseOpenWindow(
            bool hasOpenWindow,
            bool shouldRecordVersion,
            string currentVersion,
            System.Action focusExistingWindow)
        {
            if (!hasOpenWindow) return false;

            Debug.Assert(focusExistingWindow != null, "focusExistingWindow must not be null");
            Debug.Assert(!string.IsNullOrEmpty(currentVersion), "currentVersion must not be null or empty");
            focusExistingWindow();
            MaybeRecordLastSeenVersion(shouldRecordVersion, currentVersion);
            return true;
        }

        internal static void PrepareForOpen(
            SetupWizardWindow window,
            string title,
            Rect position,
            string lastSeenSetupWizardVersionBeforeOpen)
        {
            Debug.Assert(window != null, "window must not be null");
            Debug.Assert(!string.IsNullOrEmpty(title), "title must not be null or empty");

            window.titleContent = new GUIContent(title);
            window.position = position;
            window._lastSeenSetupWizardVersionBeforeOpen =
                lastSeenSetupWizardVersionBeforeOpen ?? string.Empty;
        }

        private static void FocusExistingWindow()
        {
            FocusWindowIfItsOpen<SetupWizardWindow>();
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
        private bool _isApplyingContentSize;
        private bool _isSkillsTargetFieldInitialized;
        private bool _shouldUseFirstInstallSkillsUi;
        private bool _installSkillsFlat;
        [SerializeField]
        private string _lastSeenSetupWizardVersionBeforeOpen = string.Empty;
        private IVisualElementScheduledItem _initialRefreshScheduledItem;
        private IVisualElementScheduledItem _resizeScheduledItem;
        private CancellationTokenSource _skillInstallStateRefreshCts;
        private SkillsTarget _skillsTarget = SkillsTarget.Claude;

        private void CreateGUI()
        {
            InitializeFirstInstallSkillsUiState();
            LoadLayout();
            BindElements();
            BindEvents();
            BindSizeUpdates();
            ApplyInitialCheckingState();
            ScheduleInitialRefresh();
            ScheduleResizeToContent();
        }

        private void InitializeFirstInstallSkillsUiState()
        {
            _shouldUseFirstInstallSkillsUi = ShouldUseFirstInstallSkillsUi(
                _lastSeenSetupWizardVersionBeforeOpen);
        }

        private void OnDisable()
        {
            _initialRefreshScheduledItem?.Pause();
            CancelSkillInstallStateRefresh();
        }

        private void LoadLayout()
        {
            string uxmlPath = $"{McpConstants.PackageAssetPath}/{UXML_RELATIVE_PATH}";
            VisualTreeAsset visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlPath);
            Debug.Assert(visualTree != null, $"UXML not found at {uxmlPath}");
            visualTree.CloneTree(rootVisualElement);

            string ussPath = $"{McpConstants.PackageAssetPath}/{USS_RELATIVE_PATH}";
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
            string iconPath = $"{McpConstants.PackageAssetPath}/{GITHUB_ICON_RELATIVE_PATH}";
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
            rootVisualElement.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (_isApplyingContentSize) return;
                ScheduleResizeToContent();
            });
        }

        private void RefreshAutoShowToggle()
        {
            _suppressAutoShowToggle.SetValueWithoutNotify(McpEditorSettings.GetSuppressSetupWizardAutoShow());
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
            string cachedCliVersion = CliInstallationDetector.GetCachedCliVersion();
            string projectRoot = UnityMcpPathResolver.GetProjectRoot();
            EnsureProjectLocalCliCurrent(projectRoot);
            bool cliInstalled = IsCliInstalled(cachedCliVersion);
            List<ToolSkillSynchronizer.SkillTargetInfo> targets = DetectDisplayedSkillTargetsFast(projectRoot);
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

            await CliInstallationDetector.ForceRefreshCliVersionAsync(CancellationToken.None);
            string cliVersion = CliInstallationDetector.GetCachedCliVersion();
            string projectRoot = UnityMcpPathResolver.GetProjectRoot();
            EnsureProjectLocalCliCurrent(projectRoot);
            bool cliInstalled = IsCliInstalled(cliVersion);
            bool cliVersionMatched = IsCliVersionMatched(cliVersion) && cliInstalled;

            UpdateCliStep(cliInstalled, cliVersion, cliVersionMatched);

            if (!refreshSkillsSection)
            {
                ScheduleResizeToContent();
                return;
            }

            List<ToolSkillSynchronizer.SkillTargetInfo> targets = DetectDisplayedSkillTargetsFast(projectRoot);
            bool canManageSkills = CanManageSkills(cliInstalled);
            UpdateSkillsStep(canManageSkills, targets);
            BeginRefreshDisplayedSkillTargets(canManageSkills);
            ScheduleResizeToContent();
        }

        private List<ToolSkillSynchronizer.SkillTargetInfo> DetectDisplayedSkillTargets(string projectRoot)
        {
            return ToolSkillSynchronizer.DetectTargetsForLayoutAtProjectRoot(projectRoot, !_installSkillsFlat);
        }

        private List<ToolSkillSynchronizer.SkillTargetInfo> DetectDisplayedSkillTargetsFast(string projectRoot)
        {
            return ToolSkillSynchronizer.DetectTargetsForLayoutFastAtProjectRoot(projectRoot, !_installSkillsFlat);
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
            string projectRoot = UnityMcpPathResolver.GetProjectRoot();
            List<ToolSkillSynchronizer.SkillTargetInfo> targets =
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

        internal static List<ToolSkillSynchronizer.SkillTargetInfo> FilterInstallableSkillTargets(
            IEnumerable<ToolSkillSynchronizer.SkillTargetInfo> targets)
        {
            Debug.Assert(targets != null, "targets must not be null");
            return targets
                .Where(target => target.HasSkillsDirectory)
                .ToList();
        }

        internal static bool ShouldUseFirstInstallSkillsUi(string lastSeenSetupWizardVersion)
        {
            return string.IsNullOrEmpty(lastSeenSetupWizardVersion);
        }

        internal static bool CanManageSkills(bool cliInstalled)
        {
            return cliInstalled;
        }

        internal static ToolSkillSynchronizer.SkillTargetInfo CreateFirstInstallSkillTarget(
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
                hasExistingSkills: false);
        }

        internal static ToolSkillSynchronizer.SkillTargetInfo GetSelectedSkillTargetInfo(
            IEnumerable<ToolSkillSynchronizer.SkillTargetInfo> targets,
            SkillsTarget target,
            bool groupSkillsUnderUnityCliLoop)
        {
            Debug.Assert(targets != null, "targets must not be null");

            SkillsTargetSelection selection = SkillsTargetSelectionResolver.Resolve(
                target,
                groupSkillsUnderUnityCliLoop);
            ToolSkillSynchronizer.SkillTargetInfo selectedTargetInfo = targets
                .FirstOrDefault(info => info.DirName == selection.DirectoryName);
            return string.IsNullOrEmpty(selectedTargetInfo.DirName)
                ? CreateFirstInstallSkillTarget(target, groupSkillsUnderUnityCliLoop)
                : selectedTargetInfo;
        }

        internal static List<ToolSkillSynchronizer.SkillTargetInfo> GetFirstInstallableSkillTargets(
            IEnumerable<ToolSkillSynchronizer.SkillTargetInfo> targets,
            SkillsTarget target,
            bool groupSkillsUnderUnityCliLoop)
        {
            ToolSkillSynchronizer.SkillTargetInfo selectedTargetInfo = GetSelectedSkillTargetInfo(
                targets,
                target,
                groupSkillsUnderUnityCliLoop);
            return selectedTargetInfo.InstallState == SkillInstallState.Installed
                   || selectedTargetInfo.InstallState == SkillInstallState.Checking
                ? new List<ToolSkillSynchronizer.SkillTargetInfo>()
                : new List<ToolSkillSynchronizer.SkillTargetInfo> { selectedTargetInfo };
        }

        private void UpdateCliStep(bool cliInstalled, string cliVersion, bool cliVersionMatched)
        {
            bool needsUpdate = cliInstalled && !cliVersionMatched;
            string buttonText = GetCliButtonTextForSetupWizard(
                cliInstalled,
                _isInstallingCli,
                false,
                needsUpdate,
                cliVersion,
                McpConstants.PackageInfo.version);
            bool buttonEnabled = IsCliButtonEnabledForSetupWizard(
                cliInstalled,
                cliVersionMatched,
                _isInstallingCli,
                isChecking: false);

            if (cliInstalled && cliVersionMatched)
            {
                _cliStatusLabel.text = $"v{cliVersion}";
                ViewDataBinder.ToggleClass(_cliStatusIcon, "setup-status-icon--success", true);
                ViewDataBinder.ToggleClass(_cliStatusIcon, "setup-status-icon--pending", false);
                _installCliButton.SetEnabled(buttonEnabled);
                _installCliButton.text = buttonText;
                return;
            }

            if (cliInstalled)
            {
                string requiredVersion = McpConstants.PackageInfo.version;
                _cliStatusLabel.text = $"v{cliVersion} (requires v{requiredVersion})";
            }
            else
            {
                _cliStatusLabel.text = "Not installed";
            }

            ViewDataBinder.ToggleClass(_cliStatusIcon, "setup-status-icon--success", false);
            ViewDataBinder.ToggleClass(_cliStatusIcon, "setup-status-icon--pending", true);
            _installCliButton.SetEnabled(buttonEnabled);
            _installCliButton.text = buttonText;
        }

        internal static string GetCliButtonTextForSetupWizard(
            bool cliInstalled,
            bool isInstallingCli,
            bool isChecking,
            bool needsUpdate,
            string cliVersion,
            string packageVersion)
        {
            if (isChecking)
            {
                return "Checking...";
            }

            if (isInstallingCli)
            {
                return "Installing...";
            }

            if (!cliInstalled)
            {
                return "Install CLI";
            }

            if (needsUpdate)
            {
                return $"Update CLI (v{cliVersion} \u2192 v{packageVersion})";
            }

            return "Installed";
        }

        internal static bool IsCliButtonEnabledForSetupWizard(
            bool cliInstalled,
            bool cliVersionMatched,
            bool isInstallingCli,
            bool isChecking)
        {
            return !isInstallingCli && !isChecking && (!cliInstalled || !cliVersionMatched);
        }

        private static bool IsCliVersionMatched(string cliVersion)
        {
            if (string.IsNullOrEmpty(cliVersion)) return false;

            return CliVersionComparer.IsVersionGreaterThanOrEqual(cliVersion, McpConstants.PackageInfo.version);
        }

        private static bool IsCliInstalled(string cliVersion)
        {
            return !string.IsNullOrEmpty(cliVersion);
        }

        private static void EnsureProjectLocalCliCurrent(string projectRoot)
        {
            CliInstallResult result = ProjectLocalCliAutoInstaller.EnsureProjectLocalCliCurrent(
                projectRoot,
                McpConstants.PackageInfo.version);
            if (result.Success)
            {
                return;
            }

            Debug.LogWarning(
                $"[{McpConstants.PROJECT_NAME}] Failed to update project-local uLoop CLI: {result.ErrorOutput}");
        }

        private void UpdateSkillsStep(
            bool canManageSkills,
            List<ToolSkillSynchronizer.SkillTargetInfo> targets)
        {
            _skillsTargetList.Clear();

            if (!canManageSkills)
            {
                UpdateSkillsStatusLabel(string.Empty);
                _installSkillsButton.SetEnabled(false);
                _installSkillsButton.text = GetSkillsButtonTextForSetupWizard(
                    cliInstalled: false,
                    _isInstallingSkills,
                    hasOutdatedSkills: false);
                _groupSkillsToggle.SetEnabled(false);
                ViewDataBinder.SetVisible(_skillsTargetRow, false);
                ViewDataBinder.SetVisible(_skillsTargetList, false);
                return;
            }

            _groupSkillsToggle.SetEnabled(!_isInstallingSkills);

            bool useFirstInstallSkillsUi = _shouldUseFirstInstallSkillsUi;
            ViewDataBinder.SetVisible(_skillsTargetRow, useFirstInstallSkillsUi);
            ViewDataBinder.SetVisible(_skillsTargetList, !useFirstInstallSkillsUi);

            if (useFirstInstallSkillsUi)
            {
                ToolSkillSynchronizer.SkillTargetInfo selectedTargetInfo = GetSelectedSkillTargetInfo(
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
                    isChecking: false,
                    selectedTargetInfo.InstallState));
                return;
            }

            List<ToolSkillSynchronizer.SkillTargetInfo> installableTargets = FilterInstallableSkillTargets(targets);

            foreach (ToolSkillSynchronizer.SkillTargetInfo target in installableTargets)
            {
                VisualElement item = new VisualElement();
                item.AddToClassList("setup-target-item");

                Label nameLabel = new Label($"{target.DisplayName} ({target.DirName}/)");
                nameLabel.AddToClassList("setup-target-item__label");
                item.Add(nameLabel);

                Label statusLabel = new Label(GetSkillInstallStatusText(
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
            bool wasCliInstalledBeforeInstall = CliInstallationDetector.IsCliInstalled();
            _isInstallingCli = true;
            UpdateCliStep(false, null, false);

            try
            {
                CliInstallResult result = await NativeCliInstaller.InstallAsync(
                    Application.platform,
                    McpConstants.PackageInfo.version);

                if (!result.Success)
                {
                    NativeCliInstallCommand command = NativeCliInstaller.GetInstallCommand(
                        Application.platform,
                        McpConstants.PackageInfo.version,
                        true);
                    EditorUtility.DisplayDialog(
                        "Installation Failed",
                        $"Failed to install uloop-cli.\n\n{result.ErrorOutput}\n\n"
                        + $"You can install manually:\n  {command.ManualCommand}",
                        "OK");
                    return;
                }
            }
            finally
            {
                _isInstallingCli = false;
                RefreshUI(CliInstallRefreshPolicy.ShouldRefreshSkillsAfterCliInstall(
                    wasCliInstalledBeforeInstall));
            }
        }

        private async void HandleInstallSkills()
        {
            CancelSkillInstallStateRefresh();
            string projectRoot = UnityMcpPathResolver.GetProjectRoot();
            List<ToolSkillSynchronizer.SkillTargetInfo> targets = DetectDisplayedSkillTargets(projectRoot);
            List<ToolSkillSynchronizer.SkillTargetInfo> installableTargets = _shouldUseFirstInstallSkillsUi
                ? GetFirstInstallableSkillTargets(targets, _skillsTarget, !_installSkillsFlat)
                : FilterInstallableSkillTargets(targets);
            if (installableTargets.Count == 0) return;

            _isInstallingSkills = true;
            UpdateSkillsStep(true, targets);

            try
            {
                await ToolSkillSynchronizer.InstallSkillFiles(
                    installableTargets,
                    !_installSkillsFlat);
                EditorDialogHelper.ShowSkillsInstalledDialog();
            }
            finally
            {
                _isInstallingSkills = false;
                RefreshSkillsSection();
            }
        }

        private void HandleOpenSettings()
        {
            McpEditorWindow.ShowWindow();
        }

        private void HandleSuppressAutoShowChanged(bool suppressAutoShow)
        {
            McpEditorSettings.SetSuppressSetupWizardAutoShow(suppressAutoShow);
            MaybeRecordSuppressedVersion(suppressAutoShow, McpConstants.PackageInfo.version);
            ScheduleResizeToContent();
        }

        private void HandleClose()
        {
            Close();
        }

        private void HandleOpenGitHub()
        {
            Application.OpenURL(GetGitHubRepositoryUrl());
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
            McpEditorSettings.SetInstallSkillsFlat(_installSkillsFlat);
        }

        private void ScheduleResizeToContent()
        {
            _resizeScheduledItem?.Pause();
            _resizeScheduledItem = rootVisualElement.schedule.Execute(ResizeToContent).StartingIn(0);
        }

        private void ResizeToContent()
        {
            ScrollView mainContainer = rootVisualElement.Q<ScrollView>();
            if (mainContainer == null) return;
            if (rootVisualElement.layout.width <= 0f || rootVisualElement.layout.height <= 0f) return;

            Vector2 contentSize = MeasureContentSize(mainContainer);
            if (!HasFiniteSize(contentSize)) return;
            if (contentSize.x <= 0f || contentSize.y <= 0f) return;

            Vector2 frameSize = position.size - rootVisualElement.layout.size;
            if (!HasFiniteSize(frameSize)) return;
            Rect targetRect = WithContentSize(position, contentSize, frameSize);
            if (!HasFiniteSize(targetRect.size)) return;
            if (Approximately(position.size, targetRect.size))
            {
                minSize = targetRect.size;
                maxSize = targetRect.size;
                return;
            }

            _isApplyingContentSize = true;
            minSize = targetRect.size;
            maxSize = targetRect.size;
            position = targetRect;
            _isApplyingContentSize = false;
        }

        private static Vector2 MeasureContentSize(ScrollView mainContainer)
        {
            VisualElement contentContainer = mainContainer.contentContainer;
            float width = MeasurePreferredContentWidth(mainContainer, contentContainer);
            float height = MeasurePreferredContentHeight(mainContainer, contentContainer);
            return new Vector2(width, height);
        }

        private static float MeasurePreferredContentWidth(VisualElement mainContainer, VisualElement contentContainer)
        {
            float maxRight = 0f;
            foreach (TextElement textElement in contentContainer.Query<TextElement>().Build())
            {
                if (!textElement.visible) continue;
                if (string.IsNullOrEmpty(textElement.text)) continue;
                if (!HasFiniteRect(textElement.worldBound)) continue;

                float left = textElement.worldBound.xMin - contentContainer.worldBound.xMin;
                float horizontalChrome =
                    textElement.resolvedStyle.paddingLeft
                    + textElement.resolvedStyle.paddingRight
                    + textElement.resolvedStyle.borderLeftWidth
                    + textElement.resolvedStyle.borderRightWidth;
                float verticalChrome =
                    textElement.resolvedStyle.paddingTop
                    + textElement.resolvedStyle.paddingBottom
                    + textElement.resolvedStyle.borderTopWidth
                    + textElement.resolvedStyle.borderBottomWidth;
                float laidOutWidth = textElement.worldBound.width;
                Vector2 measuredTextSize = textElement.MeasureTextSize(
                    textElement.text,
                    0f,
                    VisualElement.MeasureMode.Undefined,
                    0f,
                    VisualElement.MeasureMode.Undefined);
                if (!IsFinite(left)) continue;
                if (!IsFinite(horizontalChrome) || !IsFinite(verticalChrome)) continue;
                if (!HasFiniteSize(measuredTextSize)) continue;
                if (!IsFinite(laidOutWidth)) continue;
                float measuredWidth = measuredTextSize.x + horizontalChrome;
                int lineCount = EstimateWrappedLineCount(
                    textElement.worldBound.height - verticalChrome,
                    measuredTextSize.y);
                float preferredWidth = SelectPreferredTextWidth(
                    laidOutWidth,
                    measuredWidth,
                    lineCount,
                    textElement.resolvedStyle.whiteSpace);
                if (!IsFinite(preferredWidth)) continue;
                float right = left + preferredWidth;
                maxRight = Mathf.Max(maxRight, right);
            }

            float width =
                mainContainer.resolvedStyle.paddingLeft
                + maxRight
                + mainContainer.resolvedStyle.paddingRight;
            return IsFinite(width) ? Mathf.Ceil(width) : 0f;
        }

        internal static int EstimateWrappedLineCount(float laidOutTextHeight, float singleLineTextHeight)
        {
            if (singleLineTextHeight <= 0f) return 1;

            return Mathf.Max(1, Mathf.RoundToInt(laidOutTextHeight / singleLineTextHeight));
        }

        internal static float SelectPreferredTextWidth(
            float laidOutWidth,
            float measuredWidth,
            int lineCount,
            WhiteSpace whiteSpace)
        {
            if (whiteSpace != WhiteSpace.Normal) return measuredWidth;
            if (lineCount <= PreferredWrappedTextLineCount) return Mathf.Min(laidOutWidth, measuredWidth);

            return Mathf.Max(laidOutWidth, measuredWidth / PreferredWrappedTextLineCount);
        }

        private static float MeasurePreferredContentHeight(VisualElement mainContainer, VisualElement contentContainer)
        {
            float maxBottom = 0f;
            foreach (VisualElement child in contentContainer.Children())
            {
                if (!child.visible) continue;
                if (!HasFiniteRect(child.worldBound)) continue;
                float bottom = child.worldBound.yMax - contentContainer.worldBound.yMin;
                if (!IsFinite(bottom)) continue;
                maxBottom = Mathf.Max(maxBottom, bottom);
            }

            float height =
                mainContainer.resolvedStyle.paddingTop
                + maxBottom
                + mainContainer.resolvedStyle.paddingBottom;
            return IsFinite(height) ? Mathf.Ceil(height) : 0f;
        }

        private static bool Approximately(Vector2 left, Vector2 right)
        {
            const float Tolerance = 0.5f;
            return Mathf.Abs(left.x - right.x) < Tolerance && Mathf.Abs(left.y - right.y) < Tolerance;
        }

        internal static bool HasFiniteSize(Vector2 size)
        {
            return IsFinite(size.x) && IsFinite(size.y);
        }

        private static bool HasFiniteRect(Rect rect)
        {
            return IsFinite(rect.xMin)
                && IsFinite(rect.xMax)
                && IsFinite(rect.yMin)
                && IsFinite(rect.yMax);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

    }
}
