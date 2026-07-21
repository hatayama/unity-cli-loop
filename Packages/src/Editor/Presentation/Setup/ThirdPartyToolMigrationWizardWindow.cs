using System;
using System.Threading;
using System.Threading.Tasks;

using UnityEditor;
using UnityEngine;

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
        private static readonly Vector2 InitialWindowSize =
            ThirdPartyToolMigrationWizardWindowResizer.InitialWindowSize;
        private static readonly Vector2 MinimumWindowSize =
            ThirdPartyToolMigrationWizardWindowResizer.MinimumWindowSize;
        private static ISessionFlagsRepository RegisteredSessionFlagsRepository;
        private static SkillSetupUseCase RegisteredSkillSetupUseCase;
        private static ThirdPartyToolMigrationUseCase RegisteredThirdPartyToolMigrationUseCase;

        [SerializeField]
        private bool _shouldRefreshAfterCreateGui;

        private SkillSetupUseCase _skillSetupUseCase;
        private ThirdPartyToolMigrationUseCase _thirdPartyToolMigrationUseCase;
        private ThirdPartyToolMigrationWizardView _view;
        private ThirdPartyToolMigrationWizardWorkflowController _controller;
        private ThirdPartyToolMigrationWizardWindowResizer _resizer;

        internal static void InitializeEditorServices(
            ISessionFlagsRepository sessionFlagsRepository,
            SkillSetupUseCase skillSetupUseCase,
            ThirdPartyToolMigrationUseCase thirdPartyToolMigrationUseCase)
        {
            Debug.Assert(sessionFlagsRepository != null, "sessionFlagsRepository must not be null");
            Debug.Assert(skillSetupUseCase != null, "skillSetupUseCase must not be null");
            Debug.Assert(
                thirdPartyToolMigrationUseCase != null,
                "thirdPartyToolMigrationUseCase must not be null");

            RegisteredSessionFlagsRepository = sessionFlagsRepository
                ?? throw new ArgumentNullException(nameof(sessionFlagsRepository));
            RegisteredSkillSetupUseCase = skillSetupUseCase
                ?? throw new ArgumentNullException(nameof(skillSetupUseCase));
            RegisteredThirdPartyToolMigrationUseCase = thirdPartyToolMigrationUseCase
                ?? throw new ArgumentNullException(nameof(thirdPartyToolMigrationUseCase));
        }

        [MenuItem("Window/Unity CLI Loop/Custom Tool Migration", priority = 4)]
        public static void ShowWindow()
        {
            ShowWindowInternal(false);
        }

        internal static void ShowWindowForAutoScan()
        {
            _ = RunAutoScanWithProgressAsync(CancellationToken.None);
        }

        private static async Task RunAutoScanWithProgressAsync(CancellationToken ct)
        {
            int progressId = Progress.Start(
                ThirdPartyToolMigrationWizardText.AutoScanProgressTitle,
                ThirdPartyToolMigrationWizardText.AutoScanProgressDescription);
            try
            {
                IProgress<ThirdPartyToolMigrationProgress> progress =
                    new AutoScanMigrationProgressReporter(progressId);
                await RunAutoScanAsync(
                    innerCt => HasMigrationTargetsForAutoScanAsync(progress, innerCt),
                    SwitchToMainThreadForAutoScanAsync,
                    OpenWindowAfterAutoScan,
                    ConsumeAutoScanSessionState,
                    LogAutoScanException,
                    ct);
            }
            finally
            {
                // Progress.Start/Report/Remove are all native-thread-safe, so no main-thread marshaling is needed here.
                Progress.Remove(progressId);
            }
        }

        internal static async Task<bool> RunAutoScanAsync(
            Func<CancellationToken, Task<bool>> hasMigrationTargetsAsync,
            Func<CancellationToken, Task> switchToMainThreadAsync,
            Action openWindow,
            Action consumeAutoScanSessionState,
            Action<Exception> logException,
            CancellationToken ct)
        {
            Debug.Assert(hasMigrationTargetsAsync != null, "hasMigrationTargetsAsync must not be null");
            Debug.Assert(switchToMainThreadAsync != null, "switchToMainThreadAsync must not be null");
            Debug.Assert(openWindow != null, "openWindow must not be null");
            Debug.Assert(consumeAutoScanSessionState != null, "consumeAutoScanSessionState must not be null");
            Debug.Assert(logException != null, "logException must not be null");

            try
            {
                bool hasMigrationTargets = await hasMigrationTargetsAsync(ct);
                await switchToMainThreadAsync(ct);
                if (!ShouldOpenWindowAfterAutoScan(hasMigrationTargets, ct.IsCancellationRequested))
                {
                    return false;
                }

                openWindow();
                return true;
            }
            catch (Exception ex)
            {
                logException(ex);
                return false;
            }
            finally
            {
                consumeAutoScanSessionState();
            }
        }

        private static async Task<bool> HasMigrationTargetsForAutoScanAsync(
            IProgress<ThirdPartyToolMigrationProgress> progress,
            CancellationToken ct)
        {
            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            ThirdPartyToolMigrationUseCase migrationUseCase =
                GetThirdPartyToolMigrationUseCase();
            ThirdPartyToolMigrationPreview preview = await Task.Run(() =>
                migrationUseCase.PreviewMigrationAsync(projectRoot, progress, ct), ct);
            return preview.HasTargets;
        }

        // Progress.Start/Report/Remove are declared IsThreadSafe=true in Unity's native bindings,
        // so this reporter can relay directly from the background scan thread.
        private sealed class AutoScanMigrationProgressReporter : IProgress<ThirdPartyToolMigrationProgress>
        {
            private readonly int _progressId;

            internal AutoScanMigrationProgressReporter(int progressId)
            {
                _progressId = progressId;
            }

            public void Report(ThirdPartyToolMigrationProgress value)
            {
                // TotalItemCount <= 0 means "unknown total" (see ThirdPartyToolMigrationProjectFileInventory);
                // Progress.Report's totalSteps=0 behavior is undocumented and unverifiable from managed code,
                // so skip the items overload and leave the task showing as a plain busy indicator instead.
                if (value.TotalItemCount <= 0)
                {
                    return;
                }

                Progress.Report(_progressId, value.ProcessedItemCount, value.TotalItemCount);
            }
        }

        private static async Task SwitchToMainThreadForAutoScanAsync(CancellationToken ct)
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            await MainThreadSwitcher.SwitchToMainThread();
        }

        private static void OpenWindowAfterAutoScan()
        {
            ShowWindowInternal(true);
        }

        private static void ConsumeAutoScanSessionState()
        {
            GetSessionFlagsRepository().ConsumeShouldAutoScanThirdPartyToolMigration();
        }

        private static void LogAutoScanException(Exception ex)
        {
            Debug.Assert(ex != null, "ex must not be null");

            Debug.LogException(ex);
        }

        internal static bool ShouldOpenWindowAfterAutoScan(
            bool hasMigrationTargets,
            bool isCancellationRequested)
        {
            return hasMigrationTargets && !isCancellationRequested;
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
            return ThirdPartyToolMigrationWizardWindowResizer.CreateCenteredRect(bounds, size);
        }

        internal static Rect WithContentHeight(Rect currentRect, float contentHeight, Vector2 frameSize)
        {
            return ThirdPartyToolMigrationWizardWindowResizer.WithContentHeight(
                currentRect,
                contentHeight,
                frameSize);
        }

        internal static string GetMigrationStatusText(string[] filePaths, string projectRoot)
        {
            return ThirdPartyToolMigrationWizardText.GetMigrationStatusText(filePaths, projectRoot);
        }

        internal static bool ConfirmMigrationApply(
            int fileCount,
            Func<string, string, string, string, bool> displayDialog)
        {
            Debug.Assert(displayDialog != null, "displayDialog must not be null");
            Debug.Assert(fileCount >= 0, "fileCount must not be negative");

            return displayDialog(
                ThirdPartyToolMigrationWizardText.MigrationConfirmDialogTitle,
                ThirdPartyToolMigrationWizardText.GetMigrationConfirmDialogMessage(fileCount),
                ThirdPartyToolMigrationWizardText.MigrationConfirmDialogOkText,
                ThirdPartyToolMigrationWizardText.MigrationConfirmDialogCancelText);
        }

        internal static string GetMigrationProgressText(
            ThirdPartyToolMigrationProgress progress,
            bool isMigrating)
        {
            return ThirdPartyToolMigrationWizardText.GetMigrationProgressText(progress, isMigrating);
        }

        internal static string GetMigrationButtonText(
            bool isMigrating,
            bool hasMigrationTargets,
            bool hasCheckedMigrationStatus)
        {
            return ThirdPartyToolMigrationWizardText.GetMigrationButtonText(
                isMigrating,
                hasMigrationTargets,
                hasCheckedMigrationStatus);
        }

        internal static string GetMigrationSkillButtonText(
            bool isUpdating,
            SkillInstallState installState)
        {
            return ThirdPartyToolMigrationWizardText.GetMigrationSkillButtonText(isUpdating, installState);
        }

        internal static string GetMigrationSkillPromptText()
        {
            return ThirdPartyToolMigrationWizardText.AiMigrationSkillPromptText;
        }

        internal static string GetMigrationSkillPromptCopyButtonText()
        {
            return ThirdPartyToolMigrationWizardText.GetMigrationSkillPromptCopyButtonText();
        }

        internal static void CopyMigrationSkillPromptToClipboard()
        {
            EditorGUIUtility.systemCopyBuffer = GetMigrationSkillPromptText();
        }

        internal static bool ShouldRemoveMigrationSkill(SkillInstallState installState)
        {
            return installState == SkillInstallState.Installed
                || installState == SkillInstallState.Outdated;
        }

        internal static bool HasFiniteSize(Vector2 size)
        {
            return ThirdPartyToolMigrationWizardWindowResizer.HasFiniteSize(size);
        }

        internal static bool ShouldReportMigrationProgress(
            long lastReportTimestamp,
            long currentTimestamp,
            ThirdPartyToolMigrationProgress progress,
            long stopwatchFrequency,
            int updateIntervalMilliseconds)
        {
            return ThirdPartyToolMigrationWizardStateRules.ShouldReportMigrationProgress(
                lastReportTimestamp,
                currentTimestamp,
                progress,
                stopwatchFrequency,
                updateIntervalMilliseconds);
        }

        internal static bool ShouldApplyMigrationProgress(
            bool isCancellationRequested,
            bool hasActiveOperation)
        {
            return ThirdPartyToolMigrationWizardStateRules.ShouldApplyMigrationProgress(
                isCancellationRequested,
                hasActiveOperation);
        }

        internal static bool ShouldRefreshAfterMigration(ThirdPartyToolMigrationResult result)
        {
            return ThirdPartyToolMigrationWizardStateRules.ShouldRefreshAfterMigration(result);
        }

        internal static bool ShouldFinishMigrationOnMainThread(
            bool isCancellationRequested,
            ThirdPartyToolMigrationResult result)
        {
            return ThirdPartyToolMigrationWizardStateRules.ShouldFinishMigrationOnMainThread(
                isCancellationRequested,
                result);
        }

        internal static bool ShouldRefreshAfterInterruptedMigration(
            bool isMigrationCompletionPending,
            bool isCancellationRequested)
        {
            return ThirdPartyToolMigrationWizardStateRules.ShouldRefreshAfterInterruptedMigration(
                isMigrationCompletionPending,
                isCancellationRequested);
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

        private static ISessionFlagsRepository GetSessionFlagsRepository()
        {
            if (RegisteredSessionFlagsRepository == null)
            {
                throw new InvalidOperationException(
                    "Migration Wizard session flags repository is not initialized.");
            }

            return RegisteredSessionFlagsRepository;
        }

        private static SkillSetupUseCase GetSkillSetupUseCase()
        {
            if (RegisteredSkillSetupUseCase == null)
            {
                throw new InvalidOperationException(
                    "Migration Wizard skill setup use case is not initialized.");
            }

            return RegisteredSkillSetupUseCase;
        }

        private static ThirdPartyToolMigrationUseCase GetThirdPartyToolMigrationUseCase()
        {
            if (RegisteredThirdPartyToolMigrationUseCase == null)
            {
                throw new InvalidOperationException(
                    "Migration Wizard third-party tool migration use case is not initialized.");
            }

            return RegisteredThirdPartyToolMigrationUseCase;
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

        private void CreateGUI()
        {
            InitializeApplicationServices();
            _view = ThirdPartyToolMigrationWizardView.Create(
                rootVisualElement,
                () => _controller.RefreshUI().Forget(),
                () => _controller.HandleMigrateThirdPartyTools().Forget(),
                target => _controller.HandleMigrationSkillTargetChanged(target),
                () => _controller.HandleToggleMigrationSkill().Forget(),
                Close);
            _resizer = new ThirdPartyToolMigrationWizardWindowResizer(this, _view.MainScrollView);
            _resizer.BindSizeUpdates();
            _controller = new ThirdPartyToolMigrationWizardWorkflowController(
                _view,
                _skillSetupUseCase,
                _thirdPartyToolMigrationUseCase,
                ScheduleResizeToContent);

            bool shouldStartInitialRefresh = ConsumeShouldStartInitialRefresh();
            _controller.ShowInitialState(shouldStartInitialRefresh);
            _controller.RefreshMigrationSkillState();
            _controller.ScheduleInitialRefresh(shouldStartInitialRefresh);
            ScheduleResizeToContent();
        }

        private void InitializeApplicationServices()
        {
            _skillSetupUseCase = GetSkillSetupUseCase();
            _thirdPartyToolMigrationUseCase = GetThirdPartyToolMigrationUseCase();
        }

        private void OnDisable()
        {
            _resizer?.Pause();
            _controller?.CancelMigrationOperation();
            _controller?.CancelMigrationSkillOperation();
        }

        private void ScheduleResizeToContent()
        {
            _resizer?.ScheduleResizeToContent();
        }

        internal bool ConsumeShouldStartInitialRefresh()
        {
            if (!_shouldRefreshAfterCreateGui)
            {
                return false;
            }

            _shouldRefreshAfterCreateGui = false;
            return true;
        }

        private void TryStartInitialRefresh()
        {
            if (_controller == null)
            {
                return;
            }

            _controller.TryStartInitialRefresh(ConsumeShouldStartInitialRefresh());
        }
    }
}
