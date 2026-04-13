using System.Collections.Generic;
using System.Linq;
using System.Threading;

using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace io.github.hatayama.uLoopMCP
{
    public class SetupWizardWindow : EditorWindow
    {
        private const string UXML_RELATIVE_PATH = "Editor/UI/Setup/SetupWizardWindow.uxml";
        private const string USS_RELATIVE_PATH = "Editor/UI/Setup/SetupWizardWindow.uss";
        private const int PreferredWrappedTextLineCount = 2;

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
            SetupWizardWindow window = GetWindow<SetupWizardWindow>(true, "Unity CLI Loop Setup");
            window.ShowUtility();
            window.ScheduleResizeToContent();
            MaybeRecordLastSeenVersion(shouldRecordVersion, McpConstants.PackageInfo.version);
        }

        internal static Rect WithContentSize(Rect currentRect, Vector2 contentSize, Vector2 frameSize)
        {
            currentRect.size = contentSize + frameSize;
            return currentRect;
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
        private VisualElement _skillsTargetRow;
        private EnumField _skillsTargetField;
        private VisualElement _skillsTargetList;
        private Label _skillsStatusLabel;
        private Button _installSkillsButton;

        // Footer
        private Toggle _suppressAutoShowToggle;
        private Button _openSettingsButton;
        private Button _closeButton;

        // State
        private bool _isInstallingCli;
        private bool _isInstallingSkills;
        private bool _isApplyingContentSize;
        private bool _isSkillsTargetFieldInitialized;
        private bool _shouldUseFirstInstallSkillsUi;
        private IVisualElementScheduledItem _resizeScheduledItem;
        private SkillsTarget _skillsTarget = SkillsTarget.Claude;

        private void CreateGUI()
        {
            InitializeFirstInstallSkillsUiState();
            LoadLayout();
            BindElements();
            BindEvents();
            BindSizeUpdates();
            RefreshUI();
            ScheduleResizeToContent();
        }

        private void InitializeFirstInstallSkillsUiState()
        {
            _shouldUseFirstInstallSkillsUi = ShouldUseFirstInstallSkillsUi(
                McpEditorSettings.GetHasShownSetupWizardSkillsSelection());
            McpEditorSettings.SetHasShownSetupWizardSkillsSelection(true);
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

            _skillsTargetRow = rootVisualElement.Q<VisualElement>("skills-target-row");
            _skillsTargetField = rootVisualElement.Q<EnumField>("skills-target-field");
            _skillsTargetList = rootVisualElement.Q<VisualElement>("skills-target-list");
            _skillsStatusLabel = rootVisualElement.Q<Label>("skills-status-label");
            _installSkillsButton = rootVisualElement.Q<Button>("install-skills-button");

            _suppressAutoShowToggle = rootVisualElement.Q<Toggle>("suppress-auto-show-toggle");
            _openSettingsButton = rootVisualElement.Q<Button>("open-settings-button");
            _closeButton = rootVisualElement.Q<Button>("close-button");
        }

        private void BindEvents()
        {
            _refreshButton.clicked += () => RefreshUI();
            _installCliButton.clicked += HandleInstallCli;
            _installSkillsButton.clicked += HandleInstallSkills;
            InitializeSkillsTargetField();
            _suppressAutoShowToggle.RegisterValueChangedCallback(evt => HandleSuppressAutoShowChanged(evt.newValue));
            _openSettingsButton.clicked += HandleOpenSettings;
            _closeButton.clicked += HandleClose;
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
                }
            });
            _isSkillsTargetFieldInitialized = true;
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

        private async void RefreshUI()
        {
            RefreshAutoShowToggle();

            string nodePath = NodeEnvironmentResolver.FindNodePath();
            bool nodeDetected = !string.IsNullOrEmpty(nodePath);

            ViewDataBinder.SetVisible(_nodejsWarning, !nodeDetected);
            ViewDataBinder.SetVisible(_nodejsOk, nodeDetected);

            if (!nodeDetected)
            {
                _cliStatusLabel.text = "Requires Node.js";
                ViewDataBinder.ToggleClass(_cliStatusIcon, "setup-status-icon--success", false);
                ViewDataBinder.ToggleClass(_cliStatusIcon, "setup-status-icon--pending", true);
                _installCliButton.SetEnabled(false);
                _installSkillsButton.SetEnabled(false);
                _skillsStatusLabel.text = "";
                _skillsTargetList.Clear();
                ScheduleResizeToContent();
                return;
            }

            await CliInstallationDetector.ForceRefreshCliVersionAsync(CancellationToken.None);
            string cliVersion = CliInstallationDetector.GetCachedCliVersion();
            bool cliInstalled = cliVersion != null;
            bool cliVersionMatched = IsCliVersionMatched(cliVersion);

            UpdateCliStep(cliInstalled, cliVersion, cliVersionMatched);

            List<ToolSkillSynchronizer.SkillTargetInfo> targets = DetectDisplayedSkillTargets();
            UpdateSkillsStep(cliVersionMatched, targets);
            ScheduleResizeToContent();
        }

        private static List<ToolSkillSynchronizer.SkillTargetInfo> DetectDisplayedSkillTargets()
        {
            return ToolSkillSynchronizer.DetectTargets();
        }

        internal static List<ToolSkillSynchronizer.SkillTargetInfo> FilterInstallableSkillTargets(
            IEnumerable<ToolSkillSynchronizer.SkillTargetInfo> targets)
        {
            Debug.Assert(targets != null, "targets must not be null");
            return targets.Where(target => target.HasSkillsDirectory).ToList();
        }

        internal static bool ShouldUseFirstInstallSkillsUi(bool hasShownSetupWizardSkillsSelection)
        {
            return !hasShownSetupWizardSkillsSelection;
        }

        internal static ToolSkillSynchronizer.SkillTargetInfo CreateFirstInstallSkillTarget(
            SkillsTarget target)
        {
            return target switch
            {
                SkillsTarget.Claude => new("Claude Code", ".claude", false, false),
                SkillsTarget.Agents => new("Codex CLI / Gemini CLI", ".agents", false, false),
                _ => new("Claude Code", ".claude", false, false)
            };
        }

        private void UpdateCliStep(bool cliInstalled, string cliVersion, bool cliVersionMatched)
        {
            if (cliInstalled && cliVersionMatched)
            {
                _cliStatusLabel.text = $"v{cliVersion}";
                ViewDataBinder.ToggleClass(_cliStatusIcon, "setup-status-icon--success", true);
                ViewDataBinder.ToggleClass(_cliStatusIcon, "setup-status-icon--pending", false);
                _installCliButton.SetEnabled(false);
                _installCliButton.text = "Installed";
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
            _installCliButton.SetEnabled(!_isInstallingCli);
            _installCliButton.text = _isInstallingCli ? "Installing..." : "Install CLI";
        }
        private static bool IsCliVersionMatched(string cliVersion)
        {
            if (string.IsNullOrEmpty(cliVersion)) return false;

            string normalized = cliVersion.Trim().TrimStart('v', 'V');
            if (!System.Version.TryParse(normalized, out System.Version installed)) return false;
            if (!System.Version.TryParse(McpConstants.PackageInfo.version, out System.Version required)) return false;

            return installed.CompareTo(required) == 0;
        }

        private void UpdateSkillsStep(
            bool cliInstalled,
            List<ToolSkillSynchronizer.SkillTargetInfo> targets)
        {
            _skillsTargetList.Clear();

            if (!cliInstalled)
            {
                _skillsStatusLabel.text = "";
                _installSkillsButton.SetEnabled(false);
                ViewDataBinder.SetVisible(_skillsTargetRow, false);
                ViewDataBinder.SetVisible(_skillsTargetList, false);
                return;
            }

            bool useFirstInstallSkillsUi = _shouldUseFirstInstallSkillsUi;
            ViewDataBinder.SetVisible(_skillsTargetRow, useFirstInstallSkillsUi);
            ViewDataBinder.SetVisible(_skillsTargetList, !useFirstInstallSkillsUi);

            if (useFirstInstallSkillsUi)
            {
                _skillsStatusLabel.text = "";
                _installSkillsButton.SetEnabled(!_isInstallingSkills);
                _installSkillsButton.text = _isInstallingSkills ? "Installing..." : "Install Skills";
                return;
            }

            List<ToolSkillSynchronizer.SkillTargetInfo> installableTargets = FilterInstallableSkillTargets(targets);

            foreach (ToolSkillSynchronizer.SkillTargetInfo target in targets)
            {
                VisualElement item = new VisualElement();
                item.AddToClassList("setup-target-item");

                string prefix = target.HasExistingSkills ? "✓" : "○";
                Label label = new Label($"  {prefix} {target.DisplayName} ({target.DirName}/)");
                label.AddToClassList("setup-target-item__label");
                item.Add(label);

                _skillsTargetList.Add(item);
            }

            if (installableTargets.Count == 0)
            {
                _skillsStatusLabel.text = "Create a skills directory to opt in (.claude/skills/, .agents/skills/, etc.)";
                _installSkillsButton.SetEnabled(false);
                _installSkillsButton.text = "Install Skills";
                return;
            }

            bool allSkillsInstalled = installableTargets.All(t => t.HasExistingSkills);
            if (allSkillsInstalled)
            {
                _skillsStatusLabel.text = $"Installed for {installableTargets.Count} opted-in targets";
                _installSkillsButton.SetEnabled(false);
                _installSkillsButton.text = "Installed";
            }
            else
            {
                _skillsStatusLabel.text = installableTargets.Count == targets.Count
                    ? ""
                    : "Only opted-in targets will be installed.";
                _installSkillsButton.SetEnabled(!_isInstallingSkills);
                _installSkillsButton.text = _isInstallingSkills ? "Installing..." : "Install Skills";
            }
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

            bool permissionOk = CliInstaller.CheckWindowsPermissions(
                npmPath, installTarget, out string globalPrefix, out string manualCommand);
            if (!permissionOk)
            {
                EditorUtility.DisplayDialog(
                    "Permission Issue",
                    $"npm's global directory ({globalPrefix}) requires elevated permissions.\n\n"
                    + NpmInstallDiagnostics.BuildPermissionSolutions(manualCommand),
                    "OK");
                return;
            }

            _isInstallingCli = true;
            UpdateCliStep(false, null, false);

            try
            {
                string nodePath = NodeEnvironmentResolver.FindNodePath();
                CliInstallResult result = await CliInstaller.InstallAsync(npmPath, installTarget, nodePath);

                if (!result.Success)
                {
                    EditorUtility.DisplayDialog(
                        "Installation Failed",
                        $"Failed to install uloop-cli.\n\n{result.ErrorOutput}\n\n"
                        + $"You can install manually:\n  npm install -g {installTarget}",
                        "OK");
                }
            }
            finally
            {
                _isInstallingCli = false;
                RefreshUI();
            }
        }

        private async void HandleInstallSkills()
        {
            List<ToolSkillSynchronizer.SkillTargetInfo> targets = DetectDisplayedSkillTargets();
            List<ToolSkillSynchronizer.SkillTargetInfo> installableTargets = _shouldUseFirstInstallSkillsUi
                ? new List<ToolSkillSynchronizer.SkillTargetInfo> { CreateFirstInstallSkillTarget(_skillsTarget) }
                : FilterInstallableSkillTargets(targets);
            if (installableTargets.Count == 0) return;

            _isInstallingSkills = true;
            UpdateSkillsStep(true, targets);

            try
            {
                ToolSkillSynchronizer.SkillInstallResult result =
                    await ToolSkillSynchronizer.InstallSkillFiles(installableTargets);

                if (!result.IsSuccessful)
                {
                    EditorUtility.DisplayDialog(
                        "Installation Partially Failed",
                        $"{result.SucceededTargets}/{result.AttemptedTargets} targets succeeded.\n"
                        + "Run 'uloop skills install' to retry failed targets.",
                        "OK");
                }
            }
            finally
            {
                _isInstallingSkills = false;
                RefreshUI();
            }
        }

        private void HandleOpenSettings()
        {
            McpEditorWindow.ShowWindow();
            Close();
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
            if (lineCount <= PreferredWrappedTextLineCount) return laidOutWidth;

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
