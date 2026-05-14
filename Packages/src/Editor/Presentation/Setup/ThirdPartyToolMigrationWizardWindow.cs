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
        private const string MigrationCheckingText = "Scanning project for V3 custom tool migration...";
        private const string NoMigrationTargetsText = "No V3 custom tool migration is needed.";
        private static readonly Vector2 MinimumWindowSize = new(360f, 220f);

        private VisualElement _migrationSection;
        private Label _migrationStatusLabel;
        private ProgressBar _migrationProgressBar;
        private Button _migrateButton;
        private Button _refreshButton;
        private Button _closeButton;

        private bool _isMigrating;
        [SerializeField]
        private bool _shouldRefreshAfterCreateGui;
        private CancellationTokenSource _migrationPreviewCts;
        private ThirdPartyToolMigrationUseCase _thirdPartyToolMigrationUseCase;

        internal static void InitializeForEditorStartup()
        {
            if (AssetDatabase.IsAssetImportWorkerProcess()) return;
            if (UnityEngine.Application.isBatchMode) return;

            EditorApplication.delayCall += TryShowOnMigrationTargets;
        }

        [MenuItem("Window/Unity CLI Loop/Custom Tool Migration", priority = 4)]
        public static void ShowWindow()
        {
            ShowWindowInternal(true);
        }

        internal static bool ShouldAutoShowForMigrationTargets(bool hasMigrationTargets)
        {
            return hasMigrationTargets;
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

        internal static string GetMigrationProgressText(ThirdPartyToolMigrationProgress progress)
        {
            if (progress.TotalItemCount <= 0)
            {
                return MigrationCheckingText;
            }

            return $"{MigrationCheckingText}\n" +
                $"{progress.ProcessedItemCount}/{progress.TotalItemCount} checks complete.";
        }

        internal static string GetMigrationButtonText(bool isMigrating)
        {
            return isMigrating ? "Migrating..." : "Migrate";
        }

        private static void TryShowOnMigrationTargets()
        {
            TryShowOnMigrationTargetsAsync(CancellationToken.None);
        }

        private static async void TryShowOnMigrationTargetsAsync(CancellationToken ct)
        {
            bool hasMigrationTargets = await HasMigrationTargetsAsync(ct);
            if (ct.IsCancellationRequested)
            {
                return;
            }

            if (!ShouldAutoShowForMigrationTargets(hasMigrationTargets))
            {
                return;
            }

            EditorApplication.delayCall += ShowWindow;
        }

        private static async Task<bool> HasMigrationTargetsAsync(CancellationToken ct)
        {
            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            return await ThirdPartyToolMigrationUseCaseRegistry
                .GetRegisteredUseCase()
                .HasMigrationTargetsAsync(projectRoot, ct);
        }

        private static void ShowWindowInternal(bool shouldRefreshAfterCreateGui)
        {
            if (HasOpenInstances<ThirdPartyToolMigrationWizardWindow>())
            {
                FocusWindowIfItsOpen<ThirdPartyToolMigrationWizardWindow>();
                return;
            }

            Rect windowPosition = CreateCenteredRect(
                EditorGUIUtility.GetMainWindowPosition(),
                MinimumWindowSize);
            ThirdPartyToolMigrationWizardWindow window =
                CreateInstance<ThirdPartyToolMigrationWizardWindow>();
            PrepareForOpen(window, WindowTitle, windowPosition, shouldRefreshAfterCreateGui);
            window.ShowUtility();
        }

        private void CreateGUI()
        {
            InitializeApplicationServices();
            BuildLayout();
            BindEvents();
            ShowInitialState();
            ScheduleInitialRefresh();
        }

        private void InitializeApplicationServices()
        {
            _thirdPartyToolMigrationUseCase = ThirdPartyToolMigrationUseCaseRegistry.GetRegisteredUseCase();
        }

        private void OnDisable()
        {
            CancelMigrationPreview();
        }

        private void BuildLayout()
        {
            string ussPath = $"{UnityCliLoopConstants.PackageAssetPath}/{USS_RELATIVE_PATH}";
            StyleSheet styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(ussPath);
            Debug.Assert(styleSheet != null, $"USS not found at {ussPath}");
            rootVisualElement.styleSheets.Add(styleSheet);

            ScrollView mainContainer = new();
            mainContainer.AddToClassList("setup-main-container");
            rootVisualElement.Add(mainContainer);

            _migrationSection = new VisualElement();
            _migrationSection.AddToClassList("setup-step");
            _migrationSection.AddToClassList("setup-step--migration-alert");
            mainContainer.Add(_migrationSection);

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
            _migrateButton.text = GetMigrationButtonText(false);
            _migrateButton.AddToClassList("setup-button");
            migrationButtonRow.Add(_migrateButton);

            VisualElement footer = new();
            footer.AddToClassList("setup-footer");
            mainContainer.Add(footer);

            VisualElement footerButtonRow = new();
            footerButtonRow.AddToClassList("setup-footer__button-row");
            footer.Add(footerButtonRow);

            _refreshButton = new Button();
            _refreshButton.text = "Refresh";
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

        private void ShowInitialState()
        {
            if (_shouldRefreshAfterCreateGui)
            {
                ShowCheckingState(new ThirdPartyToolMigrationProgress(0, 0));
                return;
            }

            ShowNoMigrationTargetsState();
        }

        private void ScheduleInitialRefresh()
        {
            rootVisualElement.schedule.Execute(RefreshUI).StartingIn(0);
        }

        private async void RefreshUI()
        {
            CancellationToken ct = BeginMigrationPreview();
            ShowCheckingState(new ThirdPartyToolMigrationProgress(0, 0));
            await Task.Yield();

            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            System.IProgress<ThirdPartyToolMigrationProgress> progress =
                new ThirdPartyToolMigrationPreviewProgress(this, ct);
            ThirdPartyToolMigrationPreview preview =
                await _thirdPartyToolMigrationUseCase.PreviewMigrationAsync(projectRoot, progress, ct);
            if (ct.IsCancellationRequested)
            {
                return;
            }

            if (!preview.HasTargets)
            {
                ShowNoMigrationTargetsState();
                return;
            }

            ShowMigrationTargetsState(preview.FileCount);
        }

        private void HandleMigrateThirdPartyTools()
        {
            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            _isMigrating = true;
            ShowCheckingState(new ThirdPartyToolMigrationProgress(0, 0));

            try
            {
                ThirdPartyToolMigrationResult result =
                    _thirdPartyToolMigrationUseCase.ApplyMigration(projectRoot);
                Debug.Assert(result.Changed, "migration result should contain changed files");
                AssetDatabase.Refresh();
            }
            finally
            {
                _isMigrating = false;
                RefreshUI();
            }
        }

        private void ShowMigrationTargetsState(int fileCount)
        {
            _migrationStatusLabel.text = GetMigrationStatusText(fileCount);
            ViewDataBinder.SetVisible(_migrationProgressBar, false);
            _migrateButton.SetEnabled(!_isMigrating);
            _migrateButton.text = GetMigrationButtonText(_isMigrating);
        }

        private void ShowNoMigrationTargetsState()
        {
            _migrationStatusLabel.text = NoMigrationTargetsText;
            ViewDataBinder.SetVisible(_migrationProgressBar, false);
            _migrateButton.SetEnabled(false);
            _migrateButton.text = GetMigrationButtonText(_isMigrating);
        }

        private void ShowCheckingState(ThirdPartyToolMigrationProgress progress)
        {
            _migrationStatusLabel.text = GetMigrationProgressText(progress);
            ViewDataBinder.SetVisible(_migrationProgressBar, true);
            UpdateMigrationProgressBar(progress);
            _migrateButton.SetEnabled(false);
            _migrateButton.text = GetMigrationButtonText(_isMigrating);
        }

        private void UpdateMigrationProgressBar(ThirdPartyToolMigrationProgress progress)
        {
            int totalItemCount = Mathf.Max(progress.TotalItemCount, 1);
            int processedItemCount = Mathf.Clamp(progress.ProcessedItemCount, 0, totalItemCount);
            _migrationProgressBar.lowValue = 0;
            _migrationProgressBar.highValue = totalItemCount;
            _migrationProgressBar.value = processedItemCount;
        }

        private CancellationToken BeginMigrationPreview()
        {
            CancelMigrationPreview();
            CancellationTokenSource cts = new();
            _migrationPreviewCts = cts;
            return cts.Token;
        }

        private void CancelMigrationPreview()
        {
            if (_migrationPreviewCts == null)
            {
                return;
            }

            _migrationPreviewCts.Cancel();
            _migrationPreviewCts.Dispose();
            _migrationPreviewCts = null;
        }

        private sealed class ThirdPartyToolMigrationPreviewProgress
            : System.IProgress<ThirdPartyToolMigrationProgress>
        {
            private readonly ThirdPartyToolMigrationWizardWindow _window;
            private readonly CancellationToken _ct;

            public ThirdPartyToolMigrationPreviewProgress(
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

                _window.ShowCheckingState(value);
            }
        }
    }
}
