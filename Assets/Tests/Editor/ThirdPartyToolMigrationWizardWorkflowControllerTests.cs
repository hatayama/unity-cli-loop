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
    /// Verifies that reaching a migration scan result state consumes the auto-scan SessionState
    /// flag, instead of the flag being consumed at window-open time (which loses track of a scan
    /// interrupted by a domain reload).
    /// </summary>
    public sealed class ThirdPartyToolMigrationWizardWorkflowControllerTests
    {
        [Test]
        public void ShowMigrationTargetsState_WhenAutoScanFlagIsSet_ConsumesIt()
        {
            // Verifies that reaching the "targets found" result state consumes the auto-scan flag.
            InMemorySessionFlagsRepository sessionFlagsRepository = new();
            sessionFlagsRepository.SetShouldAutoScanThirdPartyToolMigration(true);
            ThirdPartyToolMigrationWizardWorkflowController controller =
                CreateController(sessionFlagsRepository);

            controller.ShowMigrationTargetsState(new[] { "/Project/Assets/Tool.cs" });

            Assert.That(sessionFlagsRepository.GetShouldAutoScanThirdPartyToolMigration(), Is.False);
        }

        [Test]
        public void ShowNoMigrationTargetsState_WhenAutoScanFlagIsSet_ConsumesIt()
        {
            // Verifies that reaching the "no targets" result state consumes the auto-scan flag.
            InMemorySessionFlagsRepository sessionFlagsRepository = new();
            sessionFlagsRepository.SetShouldAutoScanThirdPartyToolMigration(true);
            ThirdPartyToolMigrationWizardWorkflowController controller =
                CreateController(sessionFlagsRepository);

            controller.ShowNoMigrationTargetsState();

            Assert.That(sessionFlagsRepository.GetShouldAutoScanThirdPartyToolMigration(), Is.False);
        }

        [Test]
        public void ShowCheckingState_WhenAutoScanFlagIsSet_DoesNotConsumeIt()
        {
            // Verifies that an in-progress scan (not yet a result state) leaves the flag set, so an
            // interrupted scan can restart on the next CreateGUI.
            InMemorySessionFlagsRepository sessionFlagsRepository = new();
            sessionFlagsRepository.SetShouldAutoScanThirdPartyToolMigration(true);
            ThirdPartyToolMigrationWizardWorkflowController controller =
                CreateController(sessionFlagsRepository);

            controller.ShowCheckingState(new ThirdPartyToolMigrationProgress(1, 10));

            Assert.That(sessionFlagsRepository.GetShouldAutoScanThirdPartyToolMigration(), Is.True);
        }

        private static ThirdPartyToolMigrationWizardWorkflowController CreateController(
            ISessionFlagsRepository sessionFlagsRepository)
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
            ThirdPartyToolMigrationUseCase migrationUseCase =
                new(new NoOpThirdPartyToolMigrationPort());

            return new ThirdPartyToolMigrationWizardWorkflowController(
                view,
                skillSetupUseCase,
                migrationUseCase,
                sessionFlagsRepository,
                () => { });
        }

        private sealed class InMemorySessionFlagsRepository : ISessionFlagsRepository
        {
            private bool _shouldAutoScanThirdPartyToolMigration;

            public bool GetIsServerRunning() => false;
            public bool GetIsServerManuallyStopped() => false;
            public bool GetIsAfterCompile() => false;
            public bool GetIsDomainReloadInProgress() => false;
            public bool GetShowReconnectingUI() => false;
            public void SetIsAfterCompile(bool isAfterCompile) { }
            public void SetIsDomainReloadInProgress(bool isDomainReloadInProgress) { }
            public void SetIsReconnecting(bool isReconnecting) { }
            public void SetShowReconnectingUI(bool showReconnectingUI) { }
            public void SetShowPostCompileReconnectingUI(bool showPostCompileReconnectingUI) { }

            public void SetShouldAutoScanThirdPartyToolMigration(bool shouldAutoScanThirdPartyToolMigration)
            {
                _shouldAutoScanThirdPartyToolMigration = shouldAutoScanThirdPartyToolMigration;
            }

            public bool GetShouldAutoScanThirdPartyToolMigration()
            {
                return _shouldAutoScanThirdPartyToolMigration;
            }

            public bool ConsumeShouldAutoScanThirdPartyToolMigration()
            {
                if (!_shouldAutoScanThirdPartyToolMigration)
                {
                    return false;
                }

                _shouldAutoScanThirdPartyToolMigration = false;
                return true;
            }

            public void MarkServerStarted() { }
            public void MarkServerManuallyStopped() { }
            public void ClearServerSession() { }
            public void ClearAfterCompileFlag() { }
            public void ClearReconnectingFlags() { }
            public void ClearPostCompileReconnectingUI() { }
            public void ClearDomainReloadFlag() { }
            public void ClearDomainReloadRecoveryFlags() { }
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

        private sealed class NoOpThirdPartyToolMigrationPort : IThirdPartyToolMigrationPort
        {
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
    }
}
