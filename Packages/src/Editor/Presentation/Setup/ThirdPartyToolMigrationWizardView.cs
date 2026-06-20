using System;

using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// Owns UI Toolkit elements and visual states for the third-party tool migration wizard.
    /// </summary>
    internal sealed class ThirdPartyToolMigrationWizardView
    {
        private const string USS_RELATIVE_PATH = "Editor/Presentation/Setup/SetupWizardWindow.uss";

        private readonly Label _migrationStatusLabel;
        private readonly ProgressBar _migrationProgressBar;
        private readonly VisualElement _migrationButtonRow;
        private readonly Button _migrateButton;
        private readonly Button _refreshButton;
        private readonly Button _closeButton;

        private ThirdPartyToolMigrationWizardView(
            ScrollView mainScrollView,
            Label migrationStatusLabel,
            ProgressBar migrationProgressBar,
            VisualElement migrationButtonRow,
            Button migrateButton,
            Button refreshButton,
            Button closeButton)
        {
            Debug.Assert(mainScrollView != null, "mainScrollView must not be null");
            Debug.Assert(migrationStatusLabel != null, "migrationStatusLabel must not be null");
            Debug.Assert(migrationProgressBar != null, "migrationProgressBar must not be null");
            Debug.Assert(migrationButtonRow != null, "migrationButtonRow must not be null");
            Debug.Assert(migrateButton != null, "migrateButton must not be null");
            Debug.Assert(refreshButton != null, "refreshButton must not be null");
            Debug.Assert(closeButton != null, "closeButton must not be null");

            MainScrollView = mainScrollView;
            _migrationStatusLabel = migrationStatusLabel;
            _migrationProgressBar = migrationProgressBar;
            _migrationButtonRow = migrationButtonRow;
            _migrateButton = migrateButton;
            _refreshButton = refreshButton;
            _closeButton = closeButton;
        }

        internal ScrollView MainScrollView { get; }

        internal static ThirdPartyToolMigrationWizardView Create(
            VisualElement root,
            Action refresh,
            Action migrate,
            Action close)
        {
            Debug.Assert(root != null, "root must not be null");
            Debug.Assert(refresh != null, "refresh must not be null");
            Debug.Assert(migrate != null, "migrate must not be null");
            Debug.Assert(close != null, "close must not be null");

            string ussPath = $"{UnityCliLoopConstants.PackageAssetPath}/{USS_RELATIVE_PATH}";
            StyleSheet styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(ussPath);
            Debug.Assert(styleSheet != null, $"USS not found at {ussPath}");
            root.styleSheets.Add(styleSheet);

            ScrollView mainScrollView = CreateMainScrollView(root);
            VisualElement content = CreateMigrationSection(mainScrollView);
            Label migrationStatusLabel = CreateStatusLabel(content);
            ProgressBar migrationProgressBar = CreateProgressBar(content);
            VisualElement migrationButtonRow = CreateMigrationButtonRow(content);
            Button migrateButton = CreateMigrateButton(migrationButtonRow);
            (Button refreshButton, Button closeButton) = CreateFooter(mainScrollView);

            ThirdPartyToolMigrationWizardView view = new ThirdPartyToolMigrationWizardView(
                mainScrollView,
                migrationStatusLabel,
                migrationProgressBar,
                migrationButtonRow,
                migrateButton,
                refreshButton,
                closeButton);
            view.BindEvents(refresh, migrate, close);
            return view;
        }

        internal void ShowNotCheckedState(bool isMigrating)
        {
            _migrationStatusLabel.text = ThirdPartyToolMigrationWizardText.MigrationNotCheckedText;
            ViewDataBinder.SetVisible(_migrationProgressBar, false);
            ViewDataBinder.SetVisible(_migrationButtonRow, false);
            _migrateButton.SetEnabled(false);
            _migrateButton.text = ThirdPartyToolMigrationWizardText.GetMigrationButtonText(
                isMigrating,
                false,
                false);
            ViewDataBinder.SetVisible(_refreshButton, true);
            ViewDataBinder.SetVisible(_closeButton, false);
            _refreshButton.SetEnabled(true);
        }

        internal void ShowMigrationTargetsState(int fileCount, bool isMigrating)
        {
            _migrationStatusLabel.text = ThirdPartyToolMigrationWizardText.GetMigrationStatusText(fileCount);
            ViewDataBinder.SetVisible(_migrationProgressBar, false);
            ViewDataBinder.SetVisible(_migrationButtonRow, true);
            _migrateButton.SetEnabled(!isMigrating);
            _migrateButton.text = ThirdPartyToolMigrationWizardText.GetMigrationButtonText(
                isMigrating,
                true,
                true);
            ViewDataBinder.SetVisible(_refreshButton, true);
            ViewDataBinder.SetVisible(_closeButton, false);
            _refreshButton.SetEnabled(false);
        }

        internal void ShowNoMigrationTargetsState(bool isMigrating)
        {
            _migrationStatusLabel.text = ThirdPartyToolMigrationWizardText.NoMigrationTargetsText;
            ViewDataBinder.SetVisible(_migrationProgressBar, false);
            ViewDataBinder.SetVisible(_migrationButtonRow, true);
            _migrateButton.SetEnabled(false);
            _migrateButton.text = ThirdPartyToolMigrationWizardText.GetMigrationButtonText(
                isMigrating,
                false,
                true);
            ViewDataBinder.SetVisible(_refreshButton, false);
            ViewDataBinder.SetVisible(_closeButton, true);
            _closeButton.SetEnabled(true);
        }

        internal void ShowCheckingState(ThirdPartyToolMigrationProgress progress, bool isMigrating)
        {
            _migrationStatusLabel.text = ThirdPartyToolMigrationWizardText.GetMigrationProgressText(
                progress,
                isMigrating);
            ViewDataBinder.SetVisible(_migrationProgressBar, true);
            ViewDataBinder.SetVisible(_migrationButtonRow, true);
            UpdateMigrationProgressBar(progress);
            _migrateButton.SetEnabled(false);
            _migrateButton.text = ThirdPartyToolMigrationWizardText.GetMigrationButtonText(
                isMigrating,
                true,
                true);
            ViewDataBinder.SetVisible(_refreshButton, true);
            ViewDataBinder.SetVisible(_closeButton, false);
            _refreshButton.SetEnabled(false);
        }

        private static ScrollView CreateMainScrollView(VisualElement root)
        {
            ScrollView mainScrollView = new ScrollView();
            mainScrollView.AddToClassList("setup-main-container");
            root.Add(mainScrollView);
            return mainScrollView;
        }

        private static VisualElement CreateMigrationSection(ScrollView mainScrollView)
        {
            VisualElement migrationSection = new VisualElement();
            migrationSection.AddToClassList("setup-step");
            migrationSection.AddToClassList("setup-step--migration-alert");
            mainScrollView.Add(migrationSection);

            Label titleLabel = new Label("Custom Tool Migration");
            titleLabel.AddToClassList("setup-step__title");
            migrationSection.Add(titleLabel);

            VisualElement content = new VisualElement();
            content.AddToClassList("setup-step__content");
            migrationSection.Add(content);
            return content;
        }

        private static Label CreateStatusLabel(VisualElement content)
        {
            Label migrationStatusLabel = new Label();
            migrationStatusLabel.AddToClassList("setup-step__status-label");
            migrationStatusLabel.AddToClassList("setup-step__status-label--standalone");
            content.Add(migrationStatusLabel);
            return migrationStatusLabel;
        }

        private static ProgressBar CreateProgressBar(VisualElement content)
        {
            ProgressBar migrationProgressBar = new ProgressBar();
            migrationProgressBar.AddToClassList("setup-progress-bar");
            content.Add(migrationProgressBar);
            return migrationProgressBar;
        }

        private static VisualElement CreateMigrationButtonRow(VisualElement content)
        {
            VisualElement migrationButtonRow = new VisualElement();
            migrationButtonRow.AddToClassList("setup-step__button-row");
            content.Add(migrationButtonRow);
            return migrationButtonRow;
        }

        private static Button CreateMigrateButton(VisualElement migrationButtonRow)
        {
            Debug.Assert(migrationButtonRow != null, "migrationButtonRow must not be null");

            Button migrateButton = new Button();
            migrateButton.text = ThirdPartyToolMigrationWizardText.GetMigrationButtonText(false, false, false);
            migrateButton.AddToClassList("setup-button");
            migrateButton.AddToClassList("setup-button--migration-action");
            migrationButtonRow.Add(migrateButton);
            return migrateButton;
        }

        private static (Button refreshButton, Button closeButton) CreateFooter(ScrollView mainScrollView)
        {
            VisualElement footer = new VisualElement();
            footer.AddToClassList("setup-footer");
            mainScrollView.Add(footer);

            VisualElement footerButtonRow = new VisualElement();
            footerButtonRow.AddToClassList("setup-footer__button-row");
            footer.Add(footerButtonRow);

            Button refreshButton = new Button();
            refreshButton.text = "Check";
            refreshButton.AddToClassList("setup-button");
            refreshButton.AddToClassList("setup-button--primary");
            footerButtonRow.Add(refreshButton);

            Button closeButton = new Button();
            closeButton.text = "Close";
            closeButton.AddToClassList("setup-button");
            footerButtonRow.Add(closeButton);

            Label reopenHintLabel = new Label(
                "You can close this wizard and reopen it later from\n" +
                "Window > Unity CLI Loop > Custom Tool Migration.");
            reopenHintLabel.AddToClassList("setup-footer__hint-label");
            footer.Add(reopenHintLabel);
            return (refreshButton, closeButton);
        }

        private void BindEvents(Action refresh, Action migrate, Action close)
        {
            _refreshButton.clicked += refresh;
            _migrateButton.clicked += migrate;
            _closeButton.clicked += close;
        }

        private void UpdateMigrationProgressBar(ThirdPartyToolMigrationProgress progress)
        {
            int totalItemCount = Mathf.Max(progress.TotalItemCount, 1);
            int processedItemCount = Mathf.Clamp(progress.ProcessedItemCount, 0, totalItemCount);
            _migrationProgressBar.lowValue = 0;
            _migrationProgressBar.highValue = totalItemCount;
            _migrationProgressBar.value = processedItemCount;
        }
    }
}
