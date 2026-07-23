using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;
using UnityEngine.UIElements;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Presentation;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies that an auto-scan window open renders the compile-error-matched seed file paths
    /// directly, without ever running a scan, and that RefreshUI (the manual Check / re-check path)
    /// always performs a full, unscoped project scan regardless of any seed state.
    /// </summary>
    public sealed class ThirdPartyToolMigrationWizardWorkflowControllerTests
    {
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

            await controller.RefreshUI();
            await controller.RefreshUI();

            Assert.That(port.PreviewMigrationAsyncCallCount, Is.EqualTo(2));
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

        private static ThirdPartyToolMigrationWizardWorkflowController CreateController(
            IThirdPartyToolMigrationPort port,
            List<string> autoScanSeedFilePaths)
        {
            return CreateControllerWithRoot(new VisualElement(), port, autoScanSeedFilePaths);
        }

        private static ThirdPartyToolMigrationWizardWorkflowController CreateControllerWithRoot(
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
                () => { });
        }

        private sealed class RecordingThirdPartyToolMigrationPort : IThirdPartyToolMigrationPort
        {
            internal int PreviewMigrationAsyncCallCount { get; private set; }

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
                return Task.FromResult(new ThirdPartyToolMigrationPreview(0, 0, Array.Empty<string>()));
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
