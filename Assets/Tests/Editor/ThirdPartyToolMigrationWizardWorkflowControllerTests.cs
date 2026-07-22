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
    /// Verifies that only the first RefreshUI call after an auto-scan window-open uses the
    /// compile-error-matched seed file paths for a scoped scan; a later manual re-check falls back
    /// to a full-project scan (empty seed list) since the seeds are cleared after first use.
    /// </summary>
    public sealed class ThirdPartyToolMigrationWizardWorkflowControllerTests
    {
        [Test]
        public async Task RefreshUI_WhenCalledFirstTime_PassesAutoScanSeedFilePathsToPreview()
        {
            // Verifies that the first RefreshUI call after an auto-scan open scopes the scan to the
            // seed file paths supplied by the constructor.
            RecordingThirdPartyToolMigrationPort port = new();
            ThirdPartyToolMigrationWizardWorkflowController controller =
                CreateController(port, new List<string> { "/Project/Assets/Tool.cs" });

            await controller.RefreshUI();

            Assert.That(port.SeedFilePathsPerCall, Has.Count.EqualTo(1));
            Assert.That(port.SeedFilePathsPerCall[0], Is.EqualTo(new[] { "/Project/Assets/Tool.cs" }));
        }

        [Test]
        public async Task RefreshUI_WhenCalledASecondTime_PassesEmptySeedFilePaths()
        {
            // Verifies that a manual re-check after the initial auto-scoped scan falls back to a
            // full-project scan, since the seed file paths are cleared after their first use.
            RecordingThirdPartyToolMigrationPort port = new();
            ThirdPartyToolMigrationWizardWorkflowController controller =
                CreateController(port, new List<string> { "/Project/Assets/Tool.cs" });

            await controller.RefreshUI();
            await controller.RefreshUI();

            Assert.That(port.SeedFilePathsPerCall, Has.Count.EqualTo(2));
            Assert.That(port.SeedFilePathsPerCall[1], Is.Empty);
        }

        private static ThirdPartyToolMigrationWizardWorkflowController CreateController(
            IThirdPartyToolMigrationPort port,
            List<string> autoScanSeedFilePaths)
        {
            VisualElement root = new();
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
            internal readonly List<List<string>> SeedFilePathsPerCall = new();

            public ThirdPartyToolMigrationPreview PreviewMigration(string projectRoot)
            {
                return new ThirdPartyToolMigrationPreview(0, 0, Array.Empty<string>());
            }

            public Task<ThirdPartyToolMigrationPreview> PreviewMigrationAsync(
                string projectRoot,
                IProgress<ThirdPartyToolMigrationProgress> progress,
                CancellationToken ct)
            {
                return Task.FromResult(new ThirdPartyToolMigrationPreview(0, 0, Array.Empty<string>()));
            }

            public Task<ThirdPartyToolMigrationPreview> PreviewMigrationForSeedFilesAsync(
                string projectRoot,
                List<string> seedFilePaths,
                IProgress<ThirdPartyToolMigrationProgress> progress,
                CancellationToken ct)
            {
                SeedFilePathsPerCall.Add(seedFilePaths);
                return Task.FromResult(new ThirdPartyToolMigrationPreview(0, 0, Array.Empty<string>()));
            }

            public (bool Found, List<string> TargetFilePaths) TryDetectAutoScanTargetsFromCompileErrors(
                string projectRoot)
            {
                return (false, null);
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
