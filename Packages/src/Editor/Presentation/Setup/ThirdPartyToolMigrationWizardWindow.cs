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
        private static UnityCliLoopEditorSessionStateService RegisteredSessionStateService;

        [SerializeField]
        private bool _shouldRefreshAfterCreateGui;

        private bool _isMigrating;
        private CancellationTokenSource _migrationOperationCts;
        private ThirdPartyToolMigrationUseCase _thirdPartyToolMigrationUseCase;
        private ThirdPartyToolMigrationWizardView _view;
        private ThirdPartyToolMigrationWizardWindowResizer _resizer;

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
            _ = RunAutoScanAsync(
                HasMigrationTargetsForAutoScanAsync,
                SwitchToMainThreadForAutoScanAsync,
                OpenWindowAfterAutoScan,
                ConsumeAutoScanSessionState,
                LogAutoScanException,
                CancellationToken.None);
        }

        internal static async Task<bool> RunAutoScanAsync(
            System.Func<CancellationToken, Task<bool>> hasMigrationTargetsAsync,
            System.Func<CancellationToken, Task> switchToMainThreadAsync,
            System.Action openWindow,
            System.Action consumeAutoScanSessionState,
            System.Action<System.Exception> logException,
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
            catch (System.Exception ex)
            {
                logException(ex);
                return false;
            }
            finally
            {
                consumeAutoScanSessionState();
            }
        }

        private static async Task<bool> HasMigrationTargetsForAutoScanAsync(CancellationToken ct)
        {
            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            ThirdPartyToolMigrationUseCase migrationUseCase =
                ThirdPartyToolMigrationUseCaseRegistry.GetRegisteredUseCase();
            return await Task.Run(async () =>
                await migrationUseCase.HasMigrationTargetsAsync(projectRoot, ct), ct);
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
            GetSessionStateService().ConsumeShouldAutoScanThirdPartyToolMigration();
        }

        private static void LogAutoScanException(System.Exception ex)
        {
            Debug.Assert(ex != null, "ex must not be null");

            Debug.LogException(ex);
        }

        internal static bool ShouldStartInitialRefresh(
            bool shouldRefreshAfterCreateGui,
            bool shouldAutoScanThirdPartyToolMigration)
        {
            return ThirdPartyToolMigrationWizardStateRules.ShouldStartInitialRefresh(
                shouldRefreshAfterCreateGui,
                shouldAutoScanThirdPartyToolMigration);
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

        internal static string GetMigrationStatusText(int fileCount)
        {
            return ThirdPartyToolMigrationWizardText.GetMigrationStatusText(fileCount);
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

        private void CreateGUI()
        {
            InitializeApplicationServices();
            _view = ThirdPartyToolMigrationWizardView.Create(
                rootVisualElement,
                RefreshUI,
                HandleMigrateThirdPartyTools,
                Close);
            _resizer = new ThirdPartyToolMigrationWizardWindowResizer(this, _view.MainScrollView);
            _resizer.BindSizeUpdates();

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
            _resizer?.Pause();
            CancelMigrationOperation();
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
            System.IProgress<ThirdPartyToolMigrationProgress> progress = CreateProgressReporter(ct);
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
                System.IProgress<ThirdPartyToolMigrationProgress> progress = CreateProgressReporter(ct);
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

        private System.IProgress<ThirdPartyToolMigrationProgress> CreateProgressReporter(CancellationToken ct)
        {
            return new ThirdPartyToolMigrationProgressReporter(
                ct,
                IsMigrationOperationActive,
                ShowCheckingState);
        }

        private void ShowNotCheckedState()
        {
            _view.ShowNotCheckedState(_isMigrating);
            ScheduleResizeToContent();
        }

        private void ShowMigrationTargetsState(int fileCount)
        {
            _view.ShowMigrationTargetsState(fileCount, _isMigrating);
            ScheduleResizeToContent();
        }

        private void ShowNoMigrationTargetsState()
        {
            _view.ShowNoMigrationTargetsState(_isMigrating);
            ScheduleResizeToContent();
        }

        private void ShowCheckingState(ThirdPartyToolMigrationProgress progress)
        {
            _view.ShowCheckingState(progress, _isMigrating);
            ScheduleResizeToContent();
        }

        private void ScheduleResizeToContent()
        {
            _resizer?.ScheduleResizeToContent();
        }

        private CancellationToken BeginMigrationOperation()
        {
            CancelMigrationOperation();
            CancellationTokenSource cts = new CancellationTokenSource();
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
    }
}
