using System.Threading;
using System.Threading.Tasks;

using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// Provides the dedicated Editor window for V3 third-party custom tool migration.
    /// </summary>
    public class ThirdPartyToolMigrationWizardWindow : EditorWindow
    {
        private const string WindowTitle = "Unity CLI Loop Migration";
        private const string USS_RELATIVE_PATH = "Editor/Presentation/Setup/SetupWizardWindow.uss";
        private const string MigrationNotCheckedText = "Migration status has not been checked.";
        private const string MigrationCheckingText = "Scanning project for V3 custom tool migration...";
        private const string MigrationApplyingText = "Migrating project files for V3 custom tools...";
        private const string NoMigrationTargetsText = "No V3 custom tool migration is needed.";
        private const string MigrationButtonReadyText = "Migrate";
        private const string MigrationButtonMigratingText = "Migrating...";
        private const string MigrationButtonNoTargetsText = "Nothing to migrate";
        private const string MigrationButtonCheckRequiredText = "Check required";
        private const int MigrationProgressUiUpdateIntervalMilliseconds = 100;
        private static readonly Vector2 InitialWindowSize = new(360f, 220f);
        private static readonly Vector2 MinimumWindowSize = new(360f, 120f);
        private static UnityCliLoopEditorSessionStateService RegisteredSessionStateService;

        private ScrollView _mainScrollView;
        private VisualElement _migrationSection;
        private Label _migrationStatusLabel;
        private ProgressBar _migrationProgressBar;
        private Button _migrateButton;
        private Button _refreshButton;
        private Button _closeButton;

        private bool _isMigrating;
        private bool _isApplyingContentSize;
        [SerializeField]
        private bool _shouldRefreshAfterCreateGui;
        private IVisualElementScheduledItem _resizeScheduledItem;
        private CancellationTokenSource _migrationOperationCts;
        private ThirdPartyToolMigrationUseCase _thirdPartyToolMigrationUseCase;

        internal static void InitializeEditorServices(UnityCliLoopEditorSessionStateService sessionStateService)
        {
            Debug.Assert(sessionStateService != null, "sessionStateService must not be null");

            RegisteredSessionStateService = sessionStateService
                ?? throw new System.ArgumentNullException(nameof(sessionStateService));
        }

        [MenuItem("Window/Unity CLI Loop/Custom Tool Migration", priority = 4)]
        public static void ShowWindow()
        {
            ShowWindowInternal(false);
        }

        internal static void ShowWindowForAutoScan()
        {
            ShowWindowInternal(true);
        }

        internal static bool ShouldStartInitialRefresh(
            bool shouldRefreshAfterCreateGui,
            bool shouldAutoScanThirdPartyToolMigration)
        {
            return shouldRefreshAfterCreateGui && shouldAutoScanThirdPartyToolMigration;
        }

        internal static void PrepareForOpen(
            ThirdPartyToolMigrationWizardWindow window,
            string title,
            Rect position,
            bool shouldRefreshAfterCreateGui)
        {
            Debug.Assert(window != null, "window must not be null");
            Debug.Assert(!string.IsNullOrEmpty(title), "title must not be null or empty");

            window.titleContent = new GUIContent(title);
            window.position = position;
            window.minSize = MinimumWindowSize;
            window._shouldRefreshAfterCreateGui = shouldRefreshAfterCreateGui;
        }

        internal static Rect CreateCenteredRect(Rect bounds, Vector2 size)
        {
            Vector2 centeredPosition = bounds.center - (size * 0.5f);
            return new Rect(centeredPosition, size);
        }

        internal static Rect WithContentHeight(Rect currentRect, float contentHeight, Vector2 frameSize)
        {
            Debug.Assert(contentHeight >= 0f, "contentHeight must not be negative");

            float measuredHeight = contentHeight + frameSize.y;
            Vector2 targetSize = new(
                MinimumWindowSize.x,
                Mathf.Max(measuredHeight, MinimumWindowSize.y));
            return CreateCenteredRect(currentRect, targetSize);
        }

        internal static string GetMigrationStatusText(int fileCount)
        {
            Debug.Assert(fileCount >= 0, "fileCount must not be negative");

            string noun = fileCount == 1 ? "file" : "files";
            string verb = fileCount == 1 ? "needs" : "need";
            string subject = fileCount == 1 ? "this file still uses" : "these files still use";
            string objectPronoun = fileCount == 1 ? "it" : "them";

            return $"{fileCount} {noun} {verb} V3 custom tool migration.\n" +
                $"The Unity Console is showing errors because {subject} the old custom tool API.\n\n" +
                $"Click Migrate to update {objectPronoun} automatically. " +
                "The errors should disappear after migration.";
        }

        internal static string GetMigrationProgressText(
            ThirdPartyToolMigrationProgress progress,
            bool isMigrating)
        {
            string statusText = isMigrating ? MigrationApplyingText : MigrationCheckingText;
            if (progress.TotalItemCount <= 0)
            {
                return statusText;
            }

            return $"{statusText}\n" +
                $"{progress.ProcessedItemCount}/{progress.TotalItemCount} steps complete.";
        }

        internal static string GetMigrationButtonText(
            bool isMigrating,
            bool hasMigrationTargets,
            bool hasCheckedMigrationStatus)
        {
            if (!hasCheckedMigrationStatus)
            {
                return MigrationButtonCheckRequiredText;
            }

            if (isMigrating)
            {
                return MigrationButtonMigratingText;
            }

            return hasMigrationTargets ? MigrationButtonReadyText : MigrationButtonNoTargetsText;
        }

        private static void ShowWindowInternal(bool shouldRefreshAfterCreateGui)
        {
            if (HasOpenInstances<ThirdPartyToolMigrationWizardWindow>())
            {
                FocusExistingWindow(shouldRefreshAfterCreateGui);
                return;
            }

            Rect windowPosition = CreateCenteredRect(
                EditorGUIUtility.GetMainWindowPosition(),
                InitialWindowSize);
            ThirdPartyToolMigrationWizardWindow window =
                CreateInstance<ThirdPartyToolMigrationWizardWindow>();
            PrepareForOpen(window, WindowTitle, windowPosition, shouldRefreshAfterCreateGui);
            window.ShowUtility();
            window.ScheduleResizeToContent();
        }

        private void CreateGUI()
        {
            InitializeApplicationServices();
            BuildLayout();
            BindEvents();
            BindSizeUpdates();
            bool shouldStartInitialRefresh = ConsumeShouldStartInitialRefresh();
            ShowInitialState(shouldStartInitialRefresh);
            ScheduleInitialRefresh(shouldStartInitialRefresh);
            ScheduleResizeToContent();
        }

        private void InitializeApplicationServices()
        {
            _thirdPartyToolMigrationUseCase = ThirdPartyToolMigrationUseCaseRegistry.GetRegisteredUseCase();
        }

        private void OnDisable()
        {
            _resizeScheduledItem?.Pause();
            CancelMigrationOperation();
        }

        private void BuildLayout()
        {
            string ussPath = $"{UnityCliLoopConstants.PackageAssetPath}/{USS_RELATIVE_PATH}";
            StyleSheet styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(ussPath);
            Debug.Assert(styleSheet != null, $"USS not found at {ussPath}");
            rootVisualElement.styleSheets.Add(styleSheet);

            _mainScrollView = new ScrollView();
            _mainScrollView.AddToClassList("setup-main-container");
            rootVisualElement.Add(_mainScrollView);

            _migrationSection = new VisualElement();
            _migrationSection.AddToClassList("setup-step");
            _migrationSection.AddToClassList("setup-step--migration-alert");
            _mainScrollView.Add(_migrationSection);

            Label titleLabel = new("Custom Tool Migration");
            titleLabel.AddToClassList("setup-step__title");
            _migrationSection.Add(titleLabel);

            VisualElement content = new();
            content.AddToClassList("setup-step__content");
            _migrationSection.Add(content);

            _migrationStatusLabel = new Label();
            _migrationStatusLabel.AddToClassList("setup-step__status-label");
            _migrationStatusLabel.AddToClassList("setup-step__status-label--standalone");
            content.Add(_migrationStatusLabel);

            _migrationProgressBar = new ProgressBar();
            _migrationProgressBar.AddToClassList("setup-progress-bar");
            content.Add(_migrationProgressBar);

            VisualElement migrationButtonRow = new();
            migrationButtonRow.AddToClassList("setup-step__button-row");
            content.Add(migrationButtonRow);

            _migrateButton = new Button();
            _migrateButton.text = GetMigrationButtonText(false, false, false);
            _migrateButton.AddToClassList("setup-button");
            migrationButtonRow.Add(_migrateButton);

            VisualElement footer = new();
            footer.AddToClassList("setup-footer");
            _mainScrollView.Add(footer);

            VisualElement footerButtonRow = new();
            footerButtonRow.AddToClassList("setup-footer__button-row");
            footer.Add(footerButtonRow);

            _refreshButton = new Button();
            _refreshButton.text = "Check";
            _refreshButton.AddToClassList("setup-button");
            _refreshButton.AddToClassList("setup-button--primary");
            footerButtonRow.Add(_refreshButton);

            _closeButton = new Button();
            _closeButton.text = "Close";
            _closeButton.AddToClassList("setup-button");
            footerButtonRow.Add(_closeButton);

            Label reopenHintLabel = new(
                "You can close this wizard and reopen it later from\n" +
                "Window > Unity CLI Loop > Custom Tool Migration.");
            reopenHintLabel.AddToClassList("setup-footer__hint-label");
            footer.Add(reopenHintLabel);
        }

        private void BindEvents()
        {
            _refreshButton.clicked += RefreshUI;
            _migrateButton.clicked += HandleMigrateThirdPartyTools;
            _closeButton.clicked += Close;
        }

        private void BindSizeUpdates()
        {
            rootVisualElement.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (_isApplyingContentSize)
                {
                    return;
                }

                ScheduleResizeToContent();
            });
        }

        private void ShowInitialState(bool shouldStartInitialRefresh)
        {
            if (shouldStartInitialRefresh)
            {
                ShowCheckingState(new ThirdPartyToolMigrationProgress(0, 0));
                return;
            }

            ShowNotCheckedState();
        }

        private void ScheduleInitialRefresh(bool shouldStartInitialRefresh)
        {
            if (!shouldStartInitialRefresh)
            {
                return;
            }

            rootVisualElement.schedule.Execute(RefreshUI).StartingIn(0);
        }

        private async void RefreshUI()
        {
            CancellationToken ct = BeginMigrationOperation();
            ShowCheckingState(new ThirdPartyToolMigrationProgress(0, 0));
            await Task.Yield();

            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            System.IProgress<ThirdPartyToolMigrationProgress> progress =
                new ThirdPartyToolMigrationProgressReporter(this, ct);
            ThirdPartyToolMigrationPreview preview =
                await Task.Run(async () =>
                    await _thirdPartyToolMigrationUseCase.PreviewMigrationAsync(projectRoot, progress, ct));
            if (ct.IsCancellationRequested)
            {
                return;
            }

            await MainThreadSwitcher.SwitchToMainThread();
            if (ct.IsCancellationRequested)
            {
                return;
            }

            CompleteMigrationOperation(ct);

            if (!preview.HasTargets)
            {
                ShowNoMigrationTargetsState();
                return;
            }

            ShowMigrationTargetsState(preview.FileCount);
        }

        private async void HandleMigrateThirdPartyTools()
        {
            CancellationToken ct = BeginMigrationOperation();
            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            ThirdPartyToolMigrationResult result = default;
            bool isMigrationCompletionPending = true;
            _isMigrating = true;
            ShowCheckingState(new ThirdPartyToolMigrationProgress(0, 0));
            await Task.Yield();

            try
            {
                System.IProgress<ThirdPartyToolMigrationProgress> progress =
                    new ThirdPartyToolMigrationProgressReporter(this, ct);
                result = await Task.Run(async () =>
                    await _thirdPartyToolMigrationUseCase.ApplyMigrationAsync(projectRoot, progress, ct));
                if (!ShouldFinishMigrationOnMainThread(ct.IsCancellationRequested, result))
                {
                    return;
                }

                await MainThreadSwitcher.SwitchToMainThread();
                if (!ShouldFinishMigrationOnMainThread(ct.IsCancellationRequested, result))
                {
                    return;
                }

                CompleteMigrationOperation(ct);
                if (result.Changed)
                {
                    if (!ct.IsCancellationRequested)
                    {
                        ShowCheckingState(new ThirdPartyToolMigrationProgress(1, 1));
                        await Task.Yield();
                    }

                    AssetDatabase.Refresh();
                }

                isMigrationCompletionPending = false;
            }
            finally
            {
                _isMigrating = false;
                bool shouldRefreshAfterInterruptedMigration = ShouldRefreshAfterInterruptedMigration(
                    isMigrationCompletionPending,
                    ct.IsCancellationRequested);
                CompleteMigrationOperation(ct);
                if (shouldRefreshAfterInterruptedMigration)
                {
                    RefreshUI();
                }
            }

            if (ct.IsCancellationRequested)
            {
                return;
            }

            if (ShouldRefreshAfterMigration(result))
            {
                RefreshUI();
                return;
            }

            ShowNoMigrationTargetsState();
        }

        private void ShowNotCheckedState()
        {
            _migrationStatusLabel.text = MigrationNotCheckedText;
            ViewDataBinder.SetVisible(_migrationProgressBar, false);
            _migrateButton.SetEnabled(false);
            _migrateButton.text = GetMigrationButtonText(_isMigrating, false, false);
            _refreshButton.SetEnabled(true);
            _closeButton.SetEnabled(true);
            ScheduleResizeToContent();
        }

        private void ShowMigrationTargetsState(int fileCount)
        {
            _migrationStatusLabel.text = GetMigrationStatusText(fileCount);
            ViewDataBinder.SetVisible(_migrationProgressBar, false);
            _migrateButton.SetEnabled(!_isMigrating);
            _migrateButton.text = GetMigrationButtonText(_isMigrating, true, true);
            _refreshButton.SetEnabled(true);
            _closeButton.SetEnabled(true);
            ScheduleResizeToContent();
        }

        private void ShowNoMigrationTargetsState()
        {
            _migrationStatusLabel.text = NoMigrationTargetsText;
            ViewDataBinder.SetVisible(_migrationProgressBar, false);
            _migrateButton.SetEnabled(false);
            _migrateButton.text = GetMigrationButtonText(_isMigrating, false, true);
            _refreshButton.SetEnabled(true);
            _closeButton.SetEnabled(true);
            ScheduleResizeToContent();
        }

        private void ShowCheckingState(ThirdPartyToolMigrationProgress progress)
        {
            _migrationStatusLabel.text = GetMigrationProgressText(progress, _isMigrating);
            ViewDataBinder.SetVisible(_migrationProgressBar, true);
            UpdateMigrationProgressBar(progress);
            _migrateButton.SetEnabled(false);
            _migrateButton.text = GetMigrationButtonText(_isMigrating, true, true);
            _refreshButton.SetEnabled(false);
            _closeButton.SetEnabled(!_isMigrating);
            ScheduleResizeToContent();
        }

        private void ScheduleResizeToContent()
        {
            _resizeScheduledItem?.Pause();
            _resizeScheduledItem = rootVisualElement.schedule.Execute(ResizeToContent).StartingIn(0);
        }

        private void ResizeToContent()
        {
            if (_mainScrollView == null) return;
            if (rootVisualElement.layout.width <= 0f || rootVisualElement.layout.height <= 0f) return;

            float contentHeight = MeasurePreferredContentHeight(_mainScrollView, _mainScrollView.contentContainer);
            if (!IsFinite(contentHeight)) return;
            if (contentHeight <= 0f) return;

            Vector2 frameSize = position.size - rootVisualElement.layout.size;
            if (!HasFiniteSize(frameSize)) return;
            Rect targetRect = WithContentHeight(position, contentHeight, frameSize);
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

        private void UpdateMigrationProgressBar(ThirdPartyToolMigrationProgress progress)
        {
            int totalItemCount = Mathf.Max(progress.TotalItemCount, 1);
            int processedItemCount = Mathf.Clamp(progress.ProcessedItemCount, 0, totalItemCount);
            _migrationProgressBar.lowValue = 0;
            _migrationProgressBar.highValue = totalItemCount;
            _migrationProgressBar.value = processedItemCount;
        }

        private CancellationToken BeginMigrationOperation()
        {
            CancelMigrationOperation();
            CancellationTokenSource cts = new();
            _migrationOperationCts = cts;
            return cts.Token;
        }

        private void CancelMigrationOperation()
        {
            if (_migrationOperationCts == null)
            {
                return;
            }

            _migrationOperationCts.Cancel();
            _migrationOperationCts.Dispose();
            _migrationOperationCts = null;
        }

        private static UnityCliLoopEditorSessionStateService GetSessionStateService()
        {
            if (RegisteredSessionStateService == null)
            {
                throw new System.InvalidOperationException(
                    "Migration Wizard session-state service is not initialized.");
            }

            return RegisteredSessionStateService;
        }

        private static void FocusExistingWindow(bool shouldRefreshAfterCreateGui)
        {
            ThirdPartyToolMigrationWizardWindow[] windows =
                Resources.FindObjectsOfTypeAll<ThirdPartyToolMigrationWizardWindow>();
            if (windows.Length == 0)
            {
                return;
            }

            ThirdPartyToolMigrationWizardWindow window = windows[0];
            window.Focus();
            if (!shouldRefreshAfterCreateGui)
            {
                return;
            }

            window._shouldRefreshAfterCreateGui = true;
            window.TryStartInitialRefresh();
        }

        private bool ConsumeShouldStartInitialRefresh()
        {
            if (!_shouldRefreshAfterCreateGui)
            {
                return false;
            }

            _shouldRefreshAfterCreateGui = false;
            bool shouldAutoScanThirdPartyToolMigration =
                GetSessionStateService().ConsumeShouldAutoScanThirdPartyToolMigration();
            return ShouldStartInitialRefresh(true, shouldAutoScanThirdPartyToolMigration);
        }

        private void TryStartInitialRefresh()
        {
            bool shouldStartInitialRefresh = ConsumeShouldStartInitialRefresh();
            if (!shouldStartInitialRefresh)
            {
                return;
            }

            ShowCheckingState(new ThirdPartyToolMigrationProgress(0, 0));
            rootVisualElement.schedule.Execute(RefreshUI).StartingIn(0);
        }

        internal static bool ShouldReportMigrationProgress(
            long lastReportTimestamp,
            long currentTimestamp,
            ThirdPartyToolMigrationProgress progress,
            long stopwatchFrequency,
            int updateIntervalMilliseconds)
        {
            Debug.Assert(currentTimestamp >= 0, "currentTimestamp must not be negative");
            Debug.Assert(stopwatchFrequency > 0, "stopwatchFrequency must be positive");
            Debug.Assert(updateIntervalMilliseconds >= 0, "updateIntervalMilliseconds must not be negative");

            if (lastReportTimestamp == 0)
            {
                return true;
            }

            if (progress.TotalItemCount > 0 && progress.ProcessedItemCount >= progress.TotalItemCount)
            {
                return true;
            }

            long elapsedTicks = currentTimestamp - lastReportTimestamp;
            long requiredTicks = stopwatchFrequency * updateIntervalMilliseconds / 1000;
            return elapsedTicks >= requiredTicks;
        }

        internal static bool ShouldApplyMigrationProgress(
            bool isCancellationRequested,
            bool hasActiveOperation)
        {
            return !isCancellationRequested && hasActiveOperation;
        }

        internal static bool ShouldRefreshAfterMigration(ThirdPartyToolMigrationResult result)
        {
            Debug.Assert(result.FileCount >= 0, "result file count must not be negative");

            return false;
        }

        internal static bool ShouldFinishMigrationOnMainThread(
            bool isCancellationRequested,
            ThirdPartyToolMigrationResult result)
        {
            Debug.Assert(result.FileCount >= 0, "result file count must not be negative");

            return !isCancellationRequested || result.Changed;
        }

        internal static bool ShouldRefreshAfterInterruptedMigration(
            bool isMigrationCompletionPending,
            bool isCancellationRequested)
        {
            return isMigrationCompletionPending && !isCancellationRequested;
        }

        private void CompleteMigrationOperation(CancellationToken ct)
        {
            if (!IsMigrationOperationActive(ct))
            {
                return;
            }

            _migrationOperationCts.Dispose();
            _migrationOperationCts = null;
        }

        private bool IsMigrationOperationActive(CancellationToken ct)
        {
            return _migrationOperationCts != null && _migrationOperationCts.Token.Equals(ct);
        }

        private sealed class ThirdPartyToolMigrationProgressReporter
            : System.IProgress<ThirdPartyToolMigrationProgress>
        {
            private readonly ThirdPartyToolMigrationWizardWindow _window;
            private readonly CancellationToken _ct;
            private long _lastReportTimestamp;

            public ThirdPartyToolMigrationProgressReporter(
                ThirdPartyToolMigrationWizardWindow window,
                CancellationToken ct)
            {
                Debug.Assert(window != null, "window must not be null");

                _window = window;
                _ct = ct;
            }

            public void Report(ThirdPartyToolMigrationProgress value)
            {
                if (_ct.IsCancellationRequested)
                {
                    return;
                }

                long currentTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
                if (!ShouldReportMigrationProgress(
                        _lastReportTimestamp,
                        currentTimestamp,
                        value,
                        System.Diagnostics.Stopwatch.Frequency,
                        MigrationProgressUiUpdateIntervalMilliseconds))
                {
                    return;
                }

                _lastReportTimestamp = currentTimestamp;
                _ = ReportAsync(value, _ct);
            }

            private async Task ReportAsync(ThirdPartyToolMigrationProgress value, CancellationToken ct)
            {
                await MainThreadSwitcher.SwitchToMainThread();
                if (!ShouldApplyMigrationProgress(
                        ct.IsCancellationRequested,
                        _window.IsMigrationOperationActive(ct)))
                {
                    return;
                }

                _window.ShowCheckingState(value);
            }
        }
    }
}
