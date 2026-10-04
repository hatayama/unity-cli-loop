using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using System.Text.RegularExpressions;

using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Presentation;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies that an auto-scan window open renders the compile-error-matched seed file paths
    /// directly, without ever running a scan, and that RefreshUI (the manual Check / re-check path)
    /// always performs a full, unscoped project scan regardless of any seed state. Also covers the scan
    /// results, the migrate confirmation, and the apply outcomes with recorded dialogs and inline background work.
    /// </summary>
    public sealed class ThirdPartyToolMigrationWizardWorkflowControllerTests
    {
        private RecordingPresentationDialogs _dialogs;
        private InlineBackgroundWorkRunner _backgroundWorkRunner;

        [SetUp]
        public void SetUp()
        {
            _dialogs = new RecordingPresentationDialogs();
            _backgroundWorkRunner = new InlineBackgroundWorkRunner();
        }

        [Test]
        public void ShowInitialState_WhenShouldShowAutoScanDetectedState_DoesNotTriggerAnyPreviewCall()
        {
            // Verifies that showing the auto-scan detected state on window open never calls into the
            // migration port (no preview scan runs before Migrate is clicked).
            RecordingThirdPartyToolMigrationPort port = new();
            ThirdPartyToolMigrationWizardWorkflowController controller =
                CreateController(port, new List<string> { "/Project/Assets/Tool.cs" });

            controller.ShowInitialState(shouldShowAutoScanDetectedState: true);

            Assert.That(port.PreviewMigrationAsyncCallCount, Is.EqualTo(0));
        }

        [Test]
        public async Task RefreshUI_AlwaysPerformsAFullUnscopedProjectScan()
        {
            // Verifies that the manual Check / re-check path always calls the full-project preview,
            // regardless of any auto-scan seed file paths supplied at construction time.
            RecordingThirdPartyToolMigrationPort port = new();
            ThirdPartyToolMigrationWizardWorkflowController controller =
                CreateController(port, new List<string> { "/Project/Assets/Tool.cs" });

            await PresentationTestAwaits.AwaitWithoutCancellationAsync(controller.RefreshUI());
            await PresentationTestAwaits.AwaitWithoutCancellationAsync(controller.RefreshUI());

            Assert.That(port.PreviewMigrationAsyncCallCount, Is.EqualTo(2));
            Assert.That(_backgroundWorkRunner.RunCount, Is.EqualTo(2));
        }

        [Test]
        public void TryShowAutoScanDetectedState_RendersFreshlyPassedSeedsInsteadOfConstructorSeeds()
        {
            // Verifies that re-showing an already-open window uses the seeds passed to this call
            // (a fresh re-detection), not the stale seed list captured at construction time.
            VisualElement root = new();
            RecordingThirdPartyToolMigrationPort port = new();
            ThirdPartyToolMigrationWizardWorkflowController controller = CreateControllerWithRoot(
                root,
                port,
                new List<string> { "/Project/Assets/Old.cs" });

            controller.TryShowAutoScanDetectedState(
                shouldShowAutoScanDetectedState: true,
                new List<string> { "/Project/Assets/New1.cs", "/Project/Assets/New2.cs" });

            TextField statusTextField = root
                .Query<TextField>(className: "setup-step__status-label--standalone")
                .First();

            Assert.That(
                statusTextField.value,
                Is.EqualTo(ThirdPartyToolMigrationWizardText.GetAutoScanDetectedStatusText(2)));
        }

        /// <summary>
        /// Verifies a scan that finds files runs on the background runner and lists them for migration.
        /// </summary>
        [Test]
        public async Task RefreshUI_WhenTheScanFindsFiles_ShowsThemAsMigrationTargets()
        {
            VisualElement root = new();
            RecordingThirdPartyToolMigrationPort port = new()
            {
                PreviewFilePaths = new[] { "/Project/Assets/A.cs", "/Project/Assets/B.cs" }
            };
            ThirdPartyToolMigrationWizardWorkflowController controller =
                CreateControllerWithRoot(root, port, new List<string>());

            await PresentationTestAwaits.AwaitWithoutCancellationAsync(controller.RefreshUI());

            Assert.That(_backgroundWorkRunner.RunCount, Is.EqualTo(1));
            Assert.That(GetStatusText(root), Is.EqualTo(ThirdPartyToolMigrationWizardText.GetMigrationStatusText(2)));
        }

        /// <summary>
        /// Verifies a scan that finds nothing reports that no migration is needed.
        /// </summary>
        [Test]
        public async Task RefreshUI_WhenTheScanFindsNothing_ShowsNoMigrationTargets()
        {
            VisualElement root = new();
            RecordingThirdPartyToolMigrationPort port = new();
            ThirdPartyToolMigrationWizardWorkflowController controller =
                CreateControllerWithRoot(root, port, new List<string>());

            await PresentationTestAwaits.AwaitWithoutCancellationAsync(controller.RefreshUI());

            Assert.That(GetStatusText(root), Is.EqualTo(ThirdPartyToolMigrationWizardText.NoMigrationTargetsText));
        }

        /// <summary>
        /// Verifies a failing scan is logged and returns the window to the not-checked state instead of leaving it
        /// scanning.
        /// </summary>
        [Test]
        public async Task RefreshUI_WhenTheScanThrows_LogsAndShowsNotChecked()
        {
            VisualElement root = new();
            RecordingThirdPartyToolMigrationPort port = new()
            {
                PreviewFailure = new InvalidOperationException("<PREVIEW_THROWN>")
            };
            ThirdPartyToolMigrationWizardWorkflowController controller =
                CreateControllerWithRoot(root, port, new List<string>());
            LogAssert.Expect(LogType.Exception, new Regex("<PREVIEW_THROWN>"));

            await PresentationTestAwaits.AwaitWithoutCancellationAsync(controller.RefreshUI());

            Assert.That(GetStatusText(root), Is.EqualTo(ThirdPartyToolMigrationWizardText.MigrationNotCheckedText));
        }

        /// <summary>
        /// Verifies a scan canceled by its owner mid-flight, as when the window closes, leaves the window to that
        /// owner: nothing is logged and the checking state stays.
        /// </summary>
        [Test]
        public async Task RefreshUI_WhenTheOwnerCancelsTheScan_LeavesTheCheckingState()
        {
            VisualElement root = new();
            RecordingThirdPartyToolMigrationPort port = new()
            {
                CancelPreviewWithToken = true
            };
            ThirdPartyToolMigrationWizardWorkflowController controller =
                CreateControllerWithRoot(root, port, new List<string>());
            port.OnPreview = controller.CancelMigrationOperation;

            await PresentationTestAwaits.AwaitWithoutCancellationAsync(controller.RefreshUI());

            Assert.That(
                GetStatusText(root),
                Is.EqualTo(ThirdPartyToolMigrationWizardText.GetMigrationProgressText(
                    new ThirdPartyToolMigrationProgress(0, 0),
                    false)));
        }

        /// <summary>
        /// Verifies declining the migrate confirmation shows the exact dialog text and applies nothing.
        /// </summary>
        [Test]
        public async Task HandleMigrateThirdPartyTools_WhenDeclined_AppliesNothing()
        {
            RecordingThirdPartyToolMigrationPort port = new();
            ThirdPartyToolMigrationWizardWorkflowController controller =
                CreateController(port, new List<string>());
            _dialogs.ConfirmResult = false;

            await PresentationTestAwaits.AwaitWithoutCancellationAsync(controller.HandleMigrateThirdPartyTools());

            Assert.That(
                _dialogs.ConfirmTitles,
                Is.EqualTo(new List<string> { ThirdPartyToolMigrationWizardText.MigrationConfirmDialogTitle }));
            Assert.That(
                _dialogs.ConfirmMessages,
                Is.EqualTo(new List<string> { ThirdPartyToolMigrationWizardText.GetMigrationConfirmDialogMessage(0) }));
            Assert.That(
                _dialogs.ConfirmOkLabels,
                Is.EqualTo(new List<string> { ThirdPartyToolMigrationWizardText.MigrationConfirmDialogOkText }));
            Assert.That(
                _dialogs.ConfirmCancelLabels,
                Is.EqualTo(new List<string> { ThirdPartyToolMigrationWizardText.MigrationConfirmDialogCancelText }));
            Assert.That(port.ApplyMigrationAsyncCallCount, Is.EqualTo(0));
            Assert.That(_backgroundWorkRunner.RunCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies the confirmation names the exact file count once a full scan has verified it.
        /// </summary>
        [Test]
        public async Task HandleMigrateThirdPartyTools_AfterAFullScan_ConfirmsTheScannedFileCount()
        {
            RecordingThirdPartyToolMigrationPort port = new()
            {
                PreviewFilePaths = new[] { "/Project/Assets/A.cs", "/Project/Assets/B.cs" }
            };
            ThirdPartyToolMigrationWizardWorkflowController controller =
                CreateController(port, new List<string>());
            _dialogs.ConfirmResult = false;
            await PresentationTestAwaits.AwaitWithoutCancellationAsync(controller.RefreshUI());

            await PresentationTestAwaits.AwaitWithoutCancellationAsync(controller.HandleMigrateThirdPartyTools());

            Assert.That(
                _dialogs.ConfirmMessages,
                Is.EqualTo(new List<string> { ThirdPartyToolMigrationWizardText.GetMigrationConfirmDialogMessage(2) }));
        }

        /// <summary>
        /// Verifies the confirmation does not claim an exact count that only came from compile-error seeds.
        /// </summary>
        [Test]
        public async Task HandleMigrateThirdPartyTools_AfterAnAutoScanSeed_ConfirmsWithoutAFileCount()
        {
            RecordingThirdPartyToolMigrationPort port = new();
            ThirdPartyToolMigrationWizardWorkflowController controller = CreateController(
                port,
                new List<string> { "/Project/Assets/A.cs", "/Project/Assets/B.cs", "/Project/Assets/C.cs" });
            _dialogs.ConfirmResult = false;
            controller.ShowInitialState(shouldShowAutoScanDetectedState: true);

            await PresentationTestAwaits.AwaitWithoutCancellationAsync(controller.HandleMigrateThirdPartyTools());

            Assert.That(
                _dialogs.ConfirmMessages,
                Is.EqualTo(new List<string> { ThirdPartyToolMigrationWizardText.GetMigrationConfirmDialogMessage(0) }));
        }

        /// <summary>
        /// Verifies a confirmed migration that changes nothing applies once on the background runner and shows the
        /// complete state without rescanning.
        /// </summary>
        [Test]
        public async Task HandleMigrateThirdPartyTools_WhenConfirmedAndNothingChanges_ShowsComplete()
        {
            VisualElement root = new();
            RecordingThirdPartyToolMigrationPort port = new();
            ThirdPartyToolMigrationWizardWorkflowController controller =
                CreateControllerWithRoot(root, port, new List<string>());

            await PresentationTestAwaits.AwaitWithoutCancellationAsync(controller.HandleMigrateThirdPartyTools());

            Assert.That(port.ApplyMigrationAsyncCallCount, Is.EqualTo(1));
            Assert.That(port.PreviewMigrationAsyncCallCount, Is.EqualTo(0));
            Assert.That(_backgroundWorkRunner.RunCount, Is.EqualTo(1));
            Assert.That(GetStatusText(root), Is.EqualTo(ThirdPartyToolMigrationWizardText.MigrationCompleteText));
        }

        /// <summary>
        /// Verifies a failing apply is logged and followed by a rescan, so the window shows the files as they are
        /// after the rollback.
        /// </summary>
        [Test]
        public async Task HandleMigrateThirdPartyTools_WhenTheApplyThrows_LogsAndRescans()
        {
            VisualElement root = new();
            RecordingThirdPartyToolMigrationPort port = new()
            {
                ApplyFailure = new InvalidOperationException("<APPLY_THROWN>"),
                PreviewFilePaths = new[] { "/Project/Assets/A.cs" }
            };
            ThirdPartyToolMigrationWizardWorkflowController controller =
                CreateControllerWithRoot(root, port, new List<string>());
            LogAssert.Expect(LogType.Exception, new Regex("<APPLY_THROWN>"));

            await PresentationTestAwaits.AwaitWithoutCancellationAsync(controller.HandleMigrateThirdPartyTools());

            Assert.That(port.ApplyMigrationAsyncCallCount, Is.EqualTo(1));
            Assert.That(port.PreviewMigrationAsyncCallCount, Is.EqualTo(1));
            Assert.That(_backgroundWorkRunner.RunCount, Is.EqualTo(2));
            Assert.That(GetStatusText(root), Is.EqualTo(ThirdPartyToolMigrationWizardText.GetMigrationStatusText(1)));
        }

        private static string GetStatusText(VisualElement root)
        {
            return root.Query<TextField>(className: "setup-step__status-label--standalone").First().value;
        }

        private ThirdPartyToolMigrationWizardWorkflowController CreateController(
            IThirdPartyToolMigrationPort port,
            List<string> autoScanSeedFilePaths)
        {
            return CreateControllerWithRoot(new VisualElement(), port, autoScanSeedFilePaths);
        }

        private ThirdPartyToolMigrationWizardWorkflowController CreateControllerWithRoot(
            VisualElement root,
            IThirdPartyToolMigrationPort port,
            List<string> autoScanSeedFilePaths)
        {
            ThirdPartyToolMigrationWizardView view = ThirdPartyToolMigrationWizardView.Create(
                root,
                () => { },
                () => { },
                _ => { },
                () => { },
                () => { });
            SkillSetupUseCase skillSetupUseCase = new(new NoOpSkillSetupPort());
            ThirdPartyToolMigrationUseCase migrationUseCase = new(port);

            return new ThirdPartyToolMigrationWizardWorkflowController(
                view,
                skillSetupUseCase,
                migrationUseCase,
                autoScanSeedFilePaths,
                () => { },
                _dialogs,
                _backgroundWorkRunner);
        }

        private sealed class RecordingThirdPartyToolMigrationPort : IThirdPartyToolMigrationPort
        {
            internal int PreviewMigrationAsyncCallCount { get; private set; }
            internal string[] PreviewFilePaths { get; set; } = Array.Empty<string>();
            internal Exception PreviewFailure { get; set; }
            internal Action OnPreview { get; set; }
            internal bool CancelPreviewWithToken { get; set; }
            internal int ApplyMigrationAsyncCallCount { get; private set; }
            internal Exception ApplyFailure { get; set; }

            public ThirdPartyToolMigrationPreview PreviewMigration(string projectRoot)
            {
                return new ThirdPartyToolMigrationPreview(0, 0, Array.Empty<string>());
            }

            public Task<ThirdPartyToolMigrationPreview> PreviewMigrationAsync(
                string projectRoot,
                IProgress<ThirdPartyToolMigrationProgress> progress,
                CancellationToken ct)
            {
                PreviewMigrationAsyncCallCount++;
                OnPreview?.Invoke();
                if (CancelPreviewWithToken)
                {
                    // Like the real port, cancellation surfaces only through the token the owner canceled.
                    Assert.That(ct.IsCancellationRequested, Is.True);
                    return Task.FromCanceled<ThirdPartyToolMigrationPreview>(ct);
                }

                if (PreviewFailure != null)
                {
                    return Task.FromException<ThirdPartyToolMigrationPreview>(PreviewFailure);
                }

                return Task.FromResult(
                    new ThirdPartyToolMigrationPreview(PreviewFilePaths.Length, PreviewFilePaths.Length, PreviewFilePaths));
            }

            public (bool Found, List<string> TargetFilePaths) TryDetectAutoScanTargetsFromCompileErrors(
                string projectRoot)
            {
                return (false, new List<string>());
            }

            public Task<bool> HasMigrationTargetsAsync(string projectRoot, CancellationToken ct)
            {
                return Task.FromResult(false);
            }

            public ThirdPartyToolMigrationResult ApplyMigration(string projectRoot)
            {
                return new ThirdPartyToolMigrationResult(0, 0, Array.Empty<string>());
            }

            public Task<ThirdPartyToolMigrationResult> ApplyMigrationAsync(
                string projectRoot,
                IProgress<ThirdPartyToolMigrationProgress> progress,
                CancellationToken ct)
            {
                ApplyMigrationAsyncCallCount++;
                if (ApplyFailure != null)
                {
                    return Task.FromException<ThirdPartyToolMigrationResult>(ApplyFailure);
                }

                // An unchanged result keeps the controller away from AssetDatabase.Refresh.
                return Task.FromResult(new ThirdPartyToolMigrationResult(0, 0, Array.Empty<string>()));
            }
        }

        private sealed class NoOpSkillSetupPort : ISkillSetupPort
        {
            public void RemoveSkillFiles(string toolName)
            {
            }

            public bool IsSkillInstalled(string toolName)
            {
                return false;
            }

            public List<SkillSetupTargetInfo> DetectSkillTargetsForLayoutAtProjectRoot(
                string projectRoot,
                bool groupSkillsUnderUnityCliLoop)
            {
                return new List<SkillSetupTargetInfo>();
            }

            public List<SkillSetupTargetInfo> DetectSkillTargetsForLayoutFastAtProjectRoot(
                string projectRoot,
                bool groupSkillsUnderUnityCliLoop)
            {
                return new List<SkillSetupTargetInfo>();
            }

            public Task InstallSkillFilesAsync(
                List<SkillSetupTargetInfo> targets,
                bool groupSkillsUnderUnityCliLoop,
                CancellationToken ct)
            {
                return Task.CompletedTask;
            }

            public Task InstallSkillFilesForToolAsync(
                string toolName,
                bool groupSkillsUnderUnityCliLoop,
                CancellationToken ct)
            {
                return Task.CompletedTask;
            }

            public SkillInstallState GetV3MigrationSkillInstallStateAtProjectRoot(
                string projectRoot,
                SkillSetupTargetInfo target,
                bool groupSkillsUnderUnityCliLoop)
            {
                return SkillInstallState.Missing;
            }

            public Task InstallV3MigrationSkillFilesAsync(
                string projectRoot,
                List<SkillSetupTargetInfo> targets,
                bool groupSkillsUnderUnityCliLoop,
                CancellationToken ct)
            {
                return Task.CompletedTask;
            }

            public Task RemoveV3MigrationSkillFilesAsync(
                string projectRoot,
                List<SkillSetupTargetInfo> targets,
                bool groupSkillsUnderUnityCliLoop,
                CancellationToken ct)
            {
                return Task.CompletedTask;
            }
        }
    }
}
