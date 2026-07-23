using System;
using System.Collections.Generic;

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
        private static IThirdPartyToolMigrationAutoScanSeedRepository RegisteredAutoScanSeedRepository;
        private static SkillSetupUseCase RegisteredSkillSetupUseCase;
        private static ThirdPartyToolMigrationUseCase RegisteredThirdPartyToolMigrationUseCase;

        [SerializeField]
        private bool _shouldRefreshAfterCreateGui;

        private List<string> _autoScanSeedFilePaths = new List<string>();
        private SkillSetupUseCase _skillSetupUseCase;
        private ThirdPartyToolMigrationUseCase _thirdPartyToolMigrationUseCase;
        private ThirdPartyToolMigrationWizardView _view;
        private ThirdPartyToolMigrationWizardWorkflowController _controller;
        private ThirdPartyToolMigrationWizardWindowResizer _resizer;

        internal static void InitializeEditorServices(
            ISessionFlagsRepository sessionFlagsRepository,
            IThirdPartyToolMigrationAutoScanSeedRepository autoScanSeedRepository,
            SkillSetupUseCase skillSetupUseCase,
            ThirdPartyToolMigrationUseCase thirdPartyToolMigrationUseCase)
        {
            Debug.Assert(sessionFlagsRepository != null, "sessionFlagsRepository must not be null");
            Debug.Assert(autoScanSeedRepository != null, "autoScanSeedRepository must not be null");
            Debug.Assert(skillSetupUseCase != null, "skillSetupUseCase must not be null");
            Debug.Assert(
                thirdPartyToolMigrationUseCase != null,
                "thirdPartyToolMigrationUseCase must not be null");

            RegisteredSessionFlagsRepository = sessionFlagsRepository
                ?? throw new ArgumentNullException(nameof(sessionFlagsRepository));
            RegisteredAutoScanSeedRepository = autoScanSeedRepository
                ?? throw new ArgumentNullException(nameof(autoScanSeedRepository));
            RegisteredSkillSetupUseCase = skillSetupUseCase
                ?? throw new ArgumentNullException(nameof(skillSetupUseCase));
            RegisteredThirdPartyToolMigrationUseCase = thirdPartyToolMigrationUseCase
                ?? throw new ArgumentNullException(nameof(thirdPartyToolMigrationUseCase));
        }

        [MenuItem("Window/Unity CLI Loop/Custom Tool Migration", priority = 4)]
        public static void ShowWindow()
        {
            ShowWindowInternal(false, new List<string>());
        }

        internal static void ShowWindowForAutoScan()
        {
            List<string> seedFilePaths = ConsumeAutoScanSessionState();
            ShowWindowInternal(true, seedFilePaths);
        }

        private static List<string> ConsumeAutoScanSessionState()
        {
            GetSessionFlagsRepository().ConsumeShouldAutoScanThirdPartyToolMigration();
            string[] seedFilePaths = GetAutoScanSeedRepository().GetSeedFilePaths();
            GetAutoScanSeedRepository().ClearSeedFilePaths();
            return new List<string>(seedFilePaths);
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

        internal static Rect WithContentHeight(
            Rect currentRect,
            float contentHeight,
            Vector2 frameSize,
            float maxHeight)
        {
            return ThirdPartyToolMigrationWizardWindowResizer.WithContentHeight(
                currentRect,
                contentHeight,
                frameSize,
                maxHeight);
        }

        internal static string GetMigrationStatusText(int fileCount)
        {
            return ThirdPartyToolMigrationWizardText.GetMigrationStatusText(fileCount);
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

        internal static int GetMigrationConfirmDialogFileCount(
            bool hasVerifiedPendingFileCount,
            int pendingFileCount)
        {
            return ThirdPartyToolMigrationWizardStateRules.GetMigrationConfirmDialogFileCount(
                hasVerifiedPendingFileCount,
                pendingFileCount);
        }

        internal static (int TotalItemCount, int ProcessedItemCount) GetMigrationProgressBarRange(
            int totalItemCount,
            int processedItemCount)
        {
            return ThirdPartyToolMigrationWizardStateRules.GetMigrationProgressBarRange(
                totalItemCount,
                processedItemCount);
        }

        private static void ShowWindowInternal(bool shouldRefreshAfterCreateGui, List<string> seedFilePaths)
        {
            if (HasOpenInstances<ThirdPartyToolMigrationWizardWindow>())
            {
                FocusExistingWindow(shouldRefreshAfterCreateGui, seedFilePaths);
                return;
            }

            Rect windowPosition = CreateCenteredRect(
                EditorGUIUtility.GetMainWindowPosition(),
                InitialWindowSize);
            ThirdPartyToolMigrationWizardWindow window =
                CreateInstance<ThirdPartyToolMigrationWizardWindow>();
            window._autoScanSeedFilePaths = seedFilePaths;
            PrepareForOpen(window, WindowTitle, windowPosition, shouldRefreshAfterCreateGui);
            window.ShowUtility();
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

        private static IThirdPartyToolMigrationAutoScanSeedRepository GetAutoScanSeedRepository()
        {
            if (RegisteredAutoScanSeedRepository == null)
            {
                throw new InvalidOperationException(
                    "Migration Wizard auto-scan seed repository is not initialized.");
            }

            return RegisteredAutoScanSeedRepository;
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

        private static void FocusExistingWindow(bool shouldRefreshAfterCreateGui, List<string> seedFilePaths)
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
            window.TryShowAutoScanDetectedState(seedFilePaths);
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
            _controller = new ThirdPartyToolMigrationWizardWorkflowController(
                _view,
                _skillSetupUseCase,
                _thirdPartyToolMigrationUseCase,
                _autoScanSeedFilePaths,
                ScheduleResizeToContent);

            bool shouldStartInitialRefresh = ConsumeShouldStartInitialRefresh();
            _controller.ShowInitialState(shouldStartInitialRefresh);
            _controller.RefreshMigrationSkillState();
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

        private void TryShowAutoScanDetectedState(List<string> seedFilePaths)
        {
            if (_controller == null)
            {
                return;
            }

            _controller.TryShowAutoScanDetectedState(ConsumeShouldStartInitialRefresh(), seedFilePaths);
        }
    }
}
