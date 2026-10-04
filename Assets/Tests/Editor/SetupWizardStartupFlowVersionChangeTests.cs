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
    /// when it records the last-seen state, when it refreshes the CLI, and when it does nothing. Also covers the
    /// migration auto-scan poll actions and the fallback full scan with recording ports.
    /// </summary>
    public sealed class SetupWizardStartupFlowVersionChangeTests
    {
        private const string MinimumDispatcherVersion = "3.0.0";

        private RecordingEditorSettingsPort _editorSettingsPort;
        private StubProjectSettingsPort _projectSettingsPort;
        private StubCliInstallationDetector _cliDetector;
        private int _showWindowCount;
        private int _showAutoScanCount;
        private RecordingSessionFlagsRepository _sessionFlagsRepository;
        private RecordingAutoScanSeedRepository _autoScanSeedRepository;
        private RecordingSkillSetupPort _skillSetupPort;
        private RecordingMigrationPort _migrationPort;
        private InlineBackgroundWorkRunner _backgroundWorkRunner;
        private SetupWizardStartupFlow _flow;

        [SetUp]
        public void SetUp()
        {
            _editorSettingsPort = new RecordingEditorSettingsPort();
            _projectSettingsPort = new StubProjectSettingsPort();
            _cliDetector = new StubCliInstallationDetector();
            _showWindowCount = 0;
            _showAutoScanCount = 0;
            _sessionFlagsRepository = new RecordingSessionFlagsRepository();
            _autoScanSeedRepository = new RecordingAutoScanSeedRepository();
            _skillSetupPort = new RecordingSkillSetupPort();
            _migrationPort = new RecordingMigrationPort();
            _backgroundWorkRunner = new InlineBackgroundWorkRunner();
            CliSetupApplicationService cliSetupApplicationService = new CliSetupApplicationService(
                _cliDetector,
                new UnusedNativeCliInstaller(),
                new StubCliPinReader());
            _flow = new SetupWizardStartupFlow(
                _editorSettingsPort,
                _projectSettingsPort,
                _sessionFlagsRepository,
                _autoScanSeedRepository,
                cliSetupApplicationService,
                new SkillSetupUseCase(_skillSetupPort),
                new ThirdPartyToolMigrationUseCase(_migrationPort),
                () => _showWindowCount++,
                () => _showAutoScanCount++,
                _backgroundWorkRunner);
        }

        /// <summary>
        /// Verifies a project-level suppression records the current versions as seen without checking the CLI, so
        /// the wizard stays quiet for this release.
        /// </summary>
        [Test]
        public void TryShowOnVersionChange_WhenTheProjectSuppressesAutoShow_RecordsTheCurrentState()
        {
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

        /// <summary>
        /// Verifies the personal suppression flag alone is enough to record the current state.
        /// </summary>
        [Test]
        public void TryShowOnVersionChange_WhenTheUserSuppressesAutoShow_RecordsTheCurrentState()
        {
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

        /// <summary>
        /// Verifies an unchanged package and dispatcher minimum neither record state nor refresh the CLI.
        /// </summary>
        [Test]
        public void TryShowOnVersionChange_WhenNothingChanged_LeavesSettingsAndCliAlone()
        {
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

        /// <summary>
        /// Verifies a raised dispatcher minimum re-checks the CLI and, when the installed dispatcher already
        /// satisfies it, records the new minimum instead of showing the wizard.
        /// </summary>
        [Test]
        public void TryShowOnVersionChange_WhenOnlyTheMinimumChangedAndTheCliIsCurrent_RecordsWithoutShowing()
        {
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

        /// <summary>
        /// Verifies a missing CLI does not count as needing an update, so the new minimum is recorded.
        /// </summary>
        [Test]
        public void TryShowOnVersionChange_WhenOnlyTheMinimumChangedAndNoCliIsInstalled_RecordsWithoutShowing()
        {
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

        /// <summary>
        /// Verifies a new package version with a current CLI scans the installed skills on the background runner,
        /// at the project root and in the wizard's forced layout, and records the state when nothing is outdated.
        /// </summary>
        [Test]
        public void TryShowOnVersionChange_WhenThePackageChangedAndTheSkillsAreCurrent_ScansSkillsAndRecords()
        {
            const string PreviousVersion = "3.0.0-previous.1";
            Assume.That(UnityCliLoopConstants.PackageInfo.version, Is.Not.EqualTo(PreviousVersion));
            _editorSettingsPort.Settings = new UnityCliLoopEditorSettingsData
            {
                lastSeenSetupWizardVersion = PreviousVersion,
                lastSeenSetupWizardMinimumDispatcherVersion = MinimumDispatcherVersion
            };
            _cliDetector.CliVersion = "3.1.0";
            _cliDetector.IsDispatcher = true;

            _flow.TryShowOnVersionChange();

            Assert.That(_backgroundWorkRunner.RunCount, Is.EqualTo(1));
            Assert.That(_skillSetupPort.DetectProjectRoots, Is.EqualTo(new List<string> { UnityCliLoopPathResolver.GetProjectRoot() }));
            Assert.That(_skillSetupPort.DetectGroupFlags, Is.EqualTo(new List<bool> { !SetupWizardWindow.ForceFlatSkillInstall }));
            Assert.That(_editorSettingsPort.UpdateCount, Is.EqualTo(1));
            Assert.That(
                _editorSettingsPort.Settings.lastSeenSetupWizardVersion,
                Is.EqualTo(UnityCliLoopConstants.PackageInfo.version));
            Assert.That(_showWindowCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies a compile-error detection that finds legacy files stores them as seeds, flags the auto-scan for
        /// this session, and opens the migration window once.
        /// </summary>
        [Test]
        public void ApplyMigrationAutoScanPollAction_WhenDetectionFindsFiles_StoresSeedsAndOpensTheMigrationWindow()
        {
            _migrationPort.DetectionFound = true;
            _migrationPort.DetectedFilePaths = new List<string> { "/Project/Assets/A.cs", "/Project/Assets/B.cs" };

            _flow.ApplyMigrationAutoScanPollAction(MigrationAutoScanPollAction.RunDetection);

            Assert.That(
                _migrationPort.DetectionProjectRoots,
                Is.EqualTo(new List<string> { UnityCliLoopPathResolver.GetProjectRoot() }));
            Assert.That(_autoScanSeedRepository.StoredSeedFilePaths.Count, Is.EqualTo(1));
            Assert.That(
                _autoScanSeedRepository.StoredSeedFilePaths[0],
                Is.EqualTo(new[] { "/Project/Assets/A.cs", "/Project/Assets/B.cs" }));
            Assert.That(_sessionFlagsRepository.ShouldAutoScanValues, Is.EqualTo(new List<bool> { true }));
            Assert.That(_showAutoScanCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies a detection that finds nothing yet stores nothing and keeps the migration window closed.
        /// </summary>
        [Test]
        public void ApplyMigrationAutoScanPollAction_WhenDetectionFindsNothing_KeepsTheWindowClosed()
        {
            _flow.ApplyMigrationAutoScanPollAction(MigrationAutoScanPollAction.RunDetection);

            Assert.That(_migrationPort.DetectionProjectRoots.Count, Is.EqualTo(1));
            Assert.That(_autoScanSeedRepository.StoredSeedFilePaths, Is.Empty);
            Assert.That(_sessionFlagsRepository.ShouldAutoScanValues, Is.Empty);
            Assert.That(_showAutoScanCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies a throwing detection reaches the caller without opening the migration window.
        /// </summary>
        [Test]
        public void ApplyMigrationAutoScanPollAction_WhenDetectionThrows_RethrowsWithoutOpeningTheWindow()
        {
            _migrationPort.DetectionFailure = new InvalidOperationException("<DETECTION_THROWN>");

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => _flow.ApplyMigrationAutoScanPollAction(MigrationAutoScanPollAction.RunDetection));

            Assert.That(exception.Message, Is.EqualTo("<DETECTION_THROWN>"));
            Assert.That(_showAutoScanCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies waiting and terminating poll actions touch neither the migration port nor the window.
        /// </summary>
        [TestCase(MigrationAutoScanPollAction.ContinueWaiting)]
        [TestCase(MigrationAutoScanPollAction.Terminate)]
        public void ApplyMigrationAutoScanPollAction_WhenWaitingOrTerminating_DoesNotScan(MigrationAutoScanPollAction action)
        {
            _flow.ApplyMigrationAutoScanPollAction(action);

            Assert.That(_migrationPort.DetectionProjectRoots, Is.Empty);
            Assert.That(_migrationPort.HasTargetsProjectRoots, Is.Empty);
            Assert.That(_showAutoScanCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies the timeout fallback runs a full scan at the project root and opens the migration window when it
        /// finds targets.
        /// </summary>
        [Test]
        public void ApplyMigrationAutoScanPollAction_WhenFallingBackAndTargetsExist_OpensTheMigrationWindow()
        {
            _migrationPort.HasTargets = true;

            _flow.ApplyMigrationAutoScanPollAction(MigrationAutoScanPollAction.FallBackToFullScan);

            Assert.That(
                _migrationPort.HasTargetsProjectRoots,
                Is.EqualTo(new List<string> { UnityCliLoopPathResolver.GetProjectRoot() }));
            Assert.That(_migrationPort.DetectionProjectRoots, Is.Empty);
            Assert.That(_sessionFlagsRepository.ShouldAutoScanValues, Is.EqualTo(new List<bool> { true }));
            Assert.That(_showAutoScanCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies a fallback full scan without targets keeps the migration window closed.
        /// </summary>
        [Test]
        public async Task RunThirdPartyToolMigrationFallbackFullScanAsync_WithoutTargets_KeepsTheWindowClosed()
        {
            await PresentationTestAwaits.AwaitWithoutCancellationAsync(
                _flow.RunThirdPartyToolMigrationFallbackFullScanAsync("<PROJECT_ROOT>"));

            Assert.That(_migrationPort.HasTargetsProjectRoots, Is.EqualTo(new List<string> { "<PROJECT_ROOT>" }));
            Assert.That(
                _migrationPort.HasTargetsTokens,
                Is.EqualTo(new List<CancellationToken> { CancellationToken.None }));
            Assert.That(_sessionFlagsRepository.ShouldAutoScanValues, Is.Empty);
            Assert.That(_showAutoScanCount, Is.EqualTo(0));
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

        private sealed class RecordingSessionFlagsRepository : ISessionFlagsRepository
        {
            internal List<bool> ShouldAutoScanValues { get; } = new List<bool>();

            public void SetShouldAutoScanThirdPartyToolMigration(bool shouldAutoScanThirdPartyToolMigration)
            {
                ShouldAutoScanValues.Add(shouldAutoScanThirdPartyToolMigration);
            }

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

        private sealed class RecordingAutoScanSeedRepository : IThirdPartyToolMigrationAutoScanSeedRepository
        {
            internal List<string[]> StoredSeedFilePaths { get; } = new List<string[]>();

            public void StoreSeedFilePaths(string[] filePaths)
            {
                StoredSeedFilePaths.Add(filePaths);
            }

            public string[] GetSeedFilePaths() => throw new NotSupportedException();
            public void ClearSeedFilePaths() => throw new NotSupportedException();
        }

        private sealed class RecordingSkillSetupPort : ISkillSetupPort
        {
            internal List<string> DetectProjectRoots { get; } = new List<string>();
            internal List<bool> DetectGroupFlags { get; } = new List<bool>();

            public void RemoveSkillFiles(string toolName) => throw new NotSupportedException();
            public bool IsSkillInstalled(string toolName) => throw new NotSupportedException();

            public List<SkillSetupTargetInfo> DetectSkillTargetsForLayoutAtProjectRoot(
                string projectRoot,
                bool groupSkillsUnderUnityCliLoop)
            {
                DetectProjectRoots.Add(projectRoot);
                DetectGroupFlags.Add(groupSkillsUnderUnityCliLoop);
                return new List<SkillSetupTargetInfo>
                {
                    new SkillSetupTargetInfo(
                        "Claude Code",
                        ".claude",
                        "--claude",
                        hasSkillsDirectory: true,
                        hasExistingSkills: true,
                        hasDifferentLayoutSkills: false,
                        SkillInstallState.Installed)
                };
            }

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

        private sealed class RecordingMigrationPort : IThirdPartyToolMigrationPort
        {
            internal bool DetectionFound { get; set; }
            internal List<string> DetectedFilePaths { get; set; } = new List<string>();
            internal Exception DetectionFailure { get; set; }
            internal List<string> DetectionProjectRoots { get; } = new List<string>();
            internal bool HasTargets { get; set; }
            internal List<string> HasTargetsProjectRoots { get; } = new List<string>();
            internal List<CancellationToken> HasTargetsTokens { get; } = new List<CancellationToken>();

            public (bool Found, List<string> TargetFilePaths) TryDetectAutoScanTargetsFromCompileErrors(
                string projectRoot)
            {
                DetectionProjectRoots.Add(projectRoot);
                if (DetectionFailure != null)
                {
                    throw DetectionFailure;
                }

                return (DetectionFound, DetectedFilePaths);
            }

            public Task<bool> HasMigrationTargetsAsync(string projectRoot, CancellationToken ct)
            {
                HasTargetsProjectRoots.Add(projectRoot);
                HasTargetsTokens.Add(ct);
                return Task.FromResult(HasTargets);
            }

            public ThirdPartyToolMigrationPreview PreviewMigration(string projectRoot) => throw new NotSupportedException();

            public Task<ThirdPartyToolMigrationPreview> PreviewMigrationAsync(
                string projectRoot,
                IProgress<ThirdPartyToolMigrationProgress> progress,
                CancellationToken ct) => throw new NotSupportedException();


            public ThirdPartyToolMigrationResult ApplyMigration(string projectRoot) => throw new NotSupportedException();

            public Task<ThirdPartyToolMigrationResult> ApplyMigrationAsync(
                string projectRoot,
                IProgress<ThirdPartyToolMigrationProgress> progress,
                CancellationToken ct) => throw new NotSupportedException();
        }
    }
}
