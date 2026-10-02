using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Presentation;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the setup wizard's version-change evaluation for already-seen package versions:
    /// when it records the last-seen state, when it refreshes the CLI, and when it does nothing.
    /// </summary>
    public sealed class SetupWizardStartupFlowVersionChangeTests
    {
        private const string MinimumDispatcherVersion = "3.0.0";

        private RecordingEditorSettingsPort _editorSettingsPort;
        private StubProjectSettingsPort _projectSettingsPort;
        private StubCliInstallationDetector _cliDetector;
        private int _showWindowCount;
        private SetupWizardStartupFlow _flow;

        [SetUp]
        public void SetUp()
        {
            _editorSettingsPort = new RecordingEditorSettingsPort();
            _projectSettingsPort = new StubProjectSettingsPort();
            _cliDetector = new StubCliInstallationDetector();
            _showWindowCount = 0;
            CliSetupApplicationService cliSetupApplicationService = new CliSetupApplicationService(
                _cliDetector,
                new UnusedNativeCliInstaller(),
                new StubCliPinReader());
            _flow = new SetupWizardStartupFlow(
                _editorSettingsPort,
                _projectSettingsPort,
                new UnusedSessionFlagsRepository(),
                new UnusedAutoScanSeedRepository(),
                cliSetupApplicationService,
                new SkillSetupUseCase(new UnusedSkillSetupPort()),
                new ThirdPartyToolMigrationUseCase(new UnusedMigrationPort()),
                () => _showWindowCount++,
                () => throw new InvalidOperationException("the migration auto-scan must not open"));
        }

        [Test]
        public void TryShowOnVersionChange_WhenTheProjectSuppressesAutoShow_RecordsTheCurrentState()
        {
            // Verifies a project-level suppression records the current versions as seen without
            // checking the CLI, so the wizard stays quiet for this release.
            _editorSettingsPort.Settings = new UnityCliLoopEditorSettingsData
            {
                lastSeenSetupWizardVersion = "3.0.0",
                lastSeenSetupWizardMinimumDispatcherVersion = "2.0.0"
            };
            _projectSettingsPort.SuppressAutoShow = true;

            _flow.TryShowOnVersionChange();

            Assert.That(_editorSettingsPort.UpdateCount, Is.EqualTo(1));
            Assert.That(
                _editorSettingsPort.Settings.lastSeenSetupWizardVersion,
                Is.EqualTo(UnityCliLoopConstants.PackageInfo.version));
            Assert.That(
                _editorSettingsPort.Settings.lastSeenSetupWizardMinimumDispatcherVersion,
                Is.EqualTo(MinimumDispatcherVersion));
            Assert.That(_cliDetector.ForceRefreshCount, Is.EqualTo(0));
        }

        [Test]
        public void TryShowOnVersionChange_WhenTheUserSuppressesAutoShow_RecordsTheCurrentState()
        {
            // Verifies the personal suppression flag alone is enough to record the current state.
            _editorSettingsPort.Settings = new UnityCliLoopEditorSettingsData
            {
                lastSeenSetupWizardVersion = "3.0.0",
                lastSeenSetupWizardMinimumDispatcherVersion = "2.0.0",
                suppressSetupWizardAutoShow = true
            };

            _flow.TryShowOnVersionChange();

            Assert.That(_editorSettingsPort.UpdateCount, Is.EqualTo(1));
            Assert.That(
                _editorSettingsPort.Settings.lastSeenSetupWizardMinimumDispatcherVersion,
                Is.EqualTo(MinimumDispatcherVersion));
            Assert.That(_cliDetector.ForceRefreshCount, Is.EqualTo(0));
        }

        [Test]
        public void TryShowOnVersionChange_WhenNothingChanged_LeavesSettingsAndCliAlone()
        {
            // Verifies an unchanged package and dispatcher minimum neither record state nor refresh the CLI.
            _editorSettingsPort.Settings = new UnityCliLoopEditorSettingsData
            {
                lastSeenSetupWizardVersion = UnityCliLoopConstants.PackageInfo.version,
                lastSeenSetupWizardMinimumDispatcherVersion = MinimumDispatcherVersion
            };

            _flow.TryShowOnVersionChange();

            Assert.That(_editorSettingsPort.UpdateCount, Is.EqualTo(0));
            Assert.That(_cliDetector.ForceRefreshCount, Is.EqualTo(0));
            Assert.That(_showWindowCount, Is.EqualTo(0));
        }

        [Test]
        public void TryShowOnVersionChange_WhenOnlyTheMinimumChangedAndTheCliIsCurrent_RecordsWithoutShowing()
        {
            // Verifies a raised dispatcher minimum re-checks the CLI and, when the installed dispatcher
            // already satisfies it, records the new minimum instead of showing the wizard.
            _editorSettingsPort.Settings = new UnityCliLoopEditorSettingsData
            {
                lastSeenSetupWizardVersion = UnityCliLoopConstants.PackageInfo.version,
                lastSeenSetupWizardMinimumDispatcherVersion = "2.0.0"
            };
            _cliDetector.CliVersion = "3.1.0";
            _cliDetector.IsDispatcher = true;

            _flow.TryShowOnVersionChange();

            Assert.That(_cliDetector.ForceRefreshCount, Is.EqualTo(1));
            Assert.That(_editorSettingsPort.UpdateCount, Is.EqualTo(1));
            Assert.That(
                _editorSettingsPort.Settings.lastSeenSetupWizardMinimumDispatcherVersion,
                Is.EqualTo(MinimumDispatcherVersion));
        }

        [Test]
        public void TryShowOnVersionChange_WhenOnlyTheMinimumChangedAndNoCliIsInstalled_RecordsWithoutShowing()
        {
            // Verifies a missing CLI does not count as needing an update, so the new minimum is recorded.
            _editorSettingsPort.Settings = new UnityCliLoopEditorSettingsData
            {
                lastSeenSetupWizardVersion = UnityCliLoopConstants.PackageInfo.version,
                lastSeenSetupWizardMinimumDispatcherVersion = "2.0.0"
            };
            _cliDetector.CliVersion = string.Empty;

            _flow.TryShowOnVersionChange();

            Assert.That(_cliDetector.ForceRefreshCount, Is.EqualTo(1));
            Assert.That(_editorSettingsPort.UpdateCount, Is.EqualTo(1));
        }

        private sealed class RecordingEditorSettingsPort : IUnityCliLoopEditorSettingsPort
        {
            internal UnityCliLoopEditorSettingsData Settings { get; set; } = new UnityCliLoopEditorSettingsData();
            internal int UpdateCount { get; private set; }

            public UnityCliLoopEditorSettingsData GetSettings()
            {
                return Settings;
            }

            public void UpdateSettings(Func<UnityCliLoopEditorSettingsData, UnityCliLoopEditorSettingsData> transform)
            {
                UpdateCount++;
                Settings = transform(Settings);
            }

            public void RecoverSettingsFileIfNeeded()
            {
                throw new NotSupportedException();
            }

            public void SaveSettings(UnityCliLoopEditorSettingsData settings)
            {
                throw new NotSupportedException();
            }

            public string GetLastSeenSetupWizardVersion()
            {
                throw new NotSupportedException();
            }

            public bool GetSuppressSetupWizardAutoShow()
            {
                throw new NotSupportedException();
            }

            public void SetSuppressSetupWizardAutoShow(bool suppressAutoShow)
            {
                throw new NotSupportedException();
            }

            public void SetShowToolSettings(bool showToolSettings)
            {
                throw new NotSupportedException();
            }

            public void SetInstallSkillsFlat(bool installSkillsFlat)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class StubProjectSettingsPort : IUnityCliLoopProjectSettingsPort
        {
            internal bool SuppressAutoShow { get; set; }

            public bool GetSuppressSetupWizardAutoShow()
            {
                return SuppressAutoShow;
            }

            public void SetSuppressSetupWizardAutoShow(bool suppressAutoShow)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class StubCliInstallationDetector : ICliInstallationDetector
        {
            internal string CliVersion { get; set; } = string.Empty;
            internal bool IsDispatcher { get; set; }
            internal int ForceRefreshCount { get; private set; }

            public string GetCachedCliVersion()
            {
                return CliVersion;
            }

            public bool GetCachedCliIsDispatcher()
            {
                return IsDispatcher;
            }

            public Task ForceRefreshCliVersionAsync(CancellationToken ct)
            {
                ForceRefreshCount++;
                return Task.CompletedTask;
            }

            public bool IsCliInstalled()
            {
                throw new NotSupportedException();
            }

            public string GetCachedCliExecutablePath()
            {
                throw new NotSupportedException();
            }

            public bool IsCheckCompleted()
            {
                throw new NotSupportedException();
            }

            public Task RefreshCliVersionAsync(CancellationToken ct)
            {
                throw new NotSupportedException();
            }

            public Task<bool> IsCliVisibleFromShellAsync(RuntimePlatform platform, CancellationToken ct)
            {
                throw new NotSupportedException();
            }

            public void InvalidateCache()
            {
                throw new NotSupportedException();
            }
        }

        private sealed class StubCliPinReader : ICliPinReader
        {
            public string LoadMinimumDispatcherVersionOrThrow()
            {
                return MinimumDispatcherVersion;
            }

            public CliPinLoadResult LoadPackagePin()
            {
                throw new NotSupportedException();
            }

            public DispatcherBootstrapPinLoadResult LoadDispatcherBootstrapPin()
            {
                throw new NotSupportedException();
            }
        }

        private sealed class UnusedNativeCliInstaller : INativeCliInstaller
        {
            public bool IsPackageOwnedCurrentUserInstallPath(string cliExecutablePath, RuntimePlatform platform)
            {
                throw new NotSupportedException();
            }

            public ManagedCliKind ResolveManagedCliKind(string cliExecutablePath)
            {
                throw new NotSupportedException();
            }

            public bool HasPackageOwnedCurrentUserInstall(RuntimePlatform platform)
            {
                throw new NotSupportedException();
            }

            public Task<CliInstallResult> InstallGlobalCliAsync(
                RuntimePlatform platform,
                string dispatcherReleaseTag,
                string dispatcherArchiveManifest,
                IProgress<string> installProgress,
                CancellationToken ct)
            {
                throw new NotSupportedException();
            }

            public Task<CliInstallResult> UninstallGlobalCliAsync(RuntimePlatform platform, CancellationToken ct)
            {
                throw new NotSupportedException();
            }

            public Task<CliPathSetupPlan> GetGlobalCliPathSetupPlanAsync(RuntimePlatform platform, CancellationToken ct)
            {
                throw new NotSupportedException();
            }

            public CliPathSetupApplyResult ApplyGlobalCliPathSetup(CliPathSetupPlan plan)
            {
                throw new NotSupportedException();
            }

            public NativeCliInstallCommandLoadResult GetGlobalCliInstallCommand(
                RuntimePlatform platform,
                string dispatcherReleaseTag,
                string dispatcherArchiveManifest,
                bool removeLegacyLaunchers)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class UnusedSessionFlagsRepository : ISessionFlagsRepository
        {
            public bool GetIsServerRunning() => throw new NotSupportedException();
            public bool GetIsServerManuallyStopped() => throw new NotSupportedException();
            public bool GetIsAfterCompile() => throw new NotSupportedException();
            public bool GetIsDomainReloadInProgress() => throw new NotSupportedException();
            public bool GetShowReconnectingUI() => throw new NotSupportedException();
            public void SetIsAfterCompile(bool isAfterCompile) => throw new NotSupportedException();
            public void SetIsDomainReloadInProgress(bool isDomainReloadInProgress) => throw new NotSupportedException();
            public void SetIsReconnecting(bool isReconnecting) => throw new NotSupportedException();
            public void SetShowReconnectingUI(bool showReconnectingUI) => throw new NotSupportedException();
            public void SetShowPostCompileReconnectingUI(bool showPostCompileReconnectingUI) => throw new NotSupportedException();
            public void SetShouldAutoScanThirdPartyToolMigration(bool shouldAutoScanThirdPartyToolMigration) => throw new NotSupportedException();
            public bool ConsumeShouldAutoScanThirdPartyToolMigration() => throw new NotSupportedException();
            public void MarkServerStarted() => throw new NotSupportedException();
            public void MarkServerManuallyStopped() => throw new NotSupportedException();
            public void ClearServerSession() => throw new NotSupportedException();
            public void ClearAfterCompileFlag() => throw new NotSupportedException();
            public void ClearReconnectingFlags() => throw new NotSupportedException();
            public void ClearPostCompileReconnectingUI() => throw new NotSupportedException();
            public void ClearDomainReloadFlag() => throw new NotSupportedException();
            public void ClearDomainReloadRecoveryFlags() => throw new NotSupportedException();
        }

        private sealed class UnusedAutoScanSeedRepository : IThirdPartyToolMigrationAutoScanSeedRepository
        {
            public void StoreSeedFilePaths(string[] filePaths) => throw new NotSupportedException();
            public string[] GetSeedFilePaths() => throw new NotSupportedException();
            public void ClearSeedFilePaths() => throw new NotSupportedException();
        }

        private sealed class UnusedSkillSetupPort : ISkillSetupPort
        {
            public void RemoveSkillFiles(string toolName) => throw new NotSupportedException();
            public bool IsSkillInstalled(string toolName) => throw new NotSupportedException();

            public List<SkillSetupTargetInfo> DetectSkillTargetsForLayoutAtProjectRoot(
                string projectRoot,
                bool groupSkillsUnderUnityCliLoop) => throw new NotSupportedException();

            public List<SkillSetupTargetInfo> DetectSkillTargetsForLayoutFastAtProjectRoot(
                string projectRoot,
                bool groupSkillsUnderUnityCliLoop) => throw new NotSupportedException();

            public Task InstallSkillFilesAsync(
                List<SkillSetupTargetInfo> targets,
                bool groupSkillsUnderUnityCliLoop,
                CancellationToken ct) => throw new NotSupportedException();

            public Task InstallSkillFilesForToolAsync(
                string toolName,
                bool groupSkillsUnderUnityCliLoop,
                CancellationToken ct) => throw new NotSupportedException();

            public SkillInstallState GetV3MigrationSkillInstallStateAtProjectRoot(
                string projectRoot,
                SkillSetupTargetInfo target,
                bool groupSkillsUnderUnityCliLoop) => throw new NotSupportedException();

            public Task InstallV3MigrationSkillFilesAsync(
                string projectRoot,
                List<SkillSetupTargetInfo> targets,
                bool groupSkillsUnderUnityCliLoop,
                CancellationToken ct) => throw new NotSupportedException();

            public Task RemoveV3MigrationSkillFilesAsync(
                string projectRoot,
                List<SkillSetupTargetInfo> targets,
                bool groupSkillsUnderUnityCliLoop,
                CancellationToken ct) => throw new NotSupportedException();
        }

        private sealed class UnusedMigrationPort : IThirdPartyToolMigrationPort
        {
            public ThirdPartyToolMigrationPreview PreviewMigration(string projectRoot) => throw new NotSupportedException();

            public Task<ThirdPartyToolMigrationPreview> PreviewMigrationAsync(
                string projectRoot,
                IProgress<ThirdPartyToolMigrationProgress> progress,
                CancellationToken ct) => throw new NotSupportedException();

            public (bool Found, List<string> TargetFilePaths) TryDetectAutoScanTargetsFromCompileErrors(
                string projectRoot) => throw new NotSupportedException();

            public Task<bool> HasMigrationTargetsAsync(string projectRoot, CancellationToken ct) =>
                throw new NotSupportedException();

            public ThirdPartyToolMigrationResult ApplyMigration(string projectRoot) => throw new NotSupportedException();

            public Task<ThirdPartyToolMigrationResult> ApplyMigrationAsync(
                string projectRoot,
                IProgress<ThirdPartyToolMigrationProgress> progress,
                CancellationToken ct) => throw new NotSupportedException();
        }
    }
}
