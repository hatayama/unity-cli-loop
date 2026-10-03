using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Presentation;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the Settings skills presenter's synchronous install-state refresh, its snapshot, and the
    /// skill side effects of toggling a tool, through recording skill and CLI ports.
    /// </summary>
    public sealed class UnityCliLoopSettingsSkillsPresenterStateTests
    {
        private RecordingSkillSetupPort _skillPort;
        private StubCliInstallationDetector _cliDetector;
        private RecordingEditorSettingsPort _editorSettingsPort;
        private List<bool> _sectionRefreshCalls;
        private bool _isRefreshingVersion;
        private UnityCliLoopSettingsSkillsPresenter _presenter;

        [SetUp]
        public void SetUp()
        {
            _skillPort = new RecordingSkillSetupPort();
            _cliDetector = new StubCliInstallationDetector();
            _editorSettingsPort = new RecordingEditorSettingsPort();
            _sectionRefreshCalls = new List<bool>();
            _isRefreshingVersion = false;
            _presenter = new UnityCliLoopSettingsSkillsPresenter(
                new SkillSetupUseCase(_skillPort),
                new CliSetupApplicationService(_cliDetector, new UnusedNativeCliInstaller(), new UnusedCliPinReader()),
                _editorSettingsPort);
            _presenter.BindCoordination(
                includeSkillDirectoryChecks => _sectionRefreshCalls.Add(includeSkillDirectoryChecks),
                () => _isRefreshingVersion);
        }

        /// <summary>
        /// Verifies a fresh presenter reports the flat Claude target as missing with no scan result yet.
        /// </summary>
        [Test]
        public void GetSnapshot_BeforeAnyScan_DescribesAnUnscannedFlatClaudeTarget()
        {
            UnityCliLoopSettingsSkillsSnapshot snapshot = _presenter.GetSnapshot();

            Assert.That(snapshot.InstallSkillsFlat, Is.True);
            Assert.That(snapshot.SkillsTarget, Is.EqualTo(SkillsTarget.Claude));
            Assert.That(snapshot.SelectedTargetInstallState, Is.EqualTo(SkillInstallState.Missing));
            Assert.That(snapshot.IsInstallingSkills, Is.False);
            Assert.That(snapshot.InstallableSkillTargets, Is.Empty);
            Assert.That(snapshot.HasSkillTargetScanResult, Is.False);
        }

        /// <summary>
        /// Verifies the Settings window stores the forced flat skill layout.
        /// </summary>
        [Test]
        public void ApplyFlatSkillInstallPreference_PersistsTheFlatLayout()
        {
            _presenter.ApplyFlatSkillInstallPreference();

            Assert.That(_editorSettingsPort.InstallSkillsFlatValues, Is.EqualTo(new List<bool> { true }));
        }

        /// <summary>
        /// Verifies that without a CLI the fast refresh skips the scan, reports the target as missing with no
        /// installable targets, and refreshes the CLI section with skill checks.
        /// </summary>
        [Test]
        public void RefreshSelectedTargetInstallStateFast_WithoutACli_ReportsAScannedEmptyState()
        {
            _cliDetector.IsCliInstalledValue = false;
            _presenter.MarkSelectedTargetInstallStateChecking();

            _presenter.RefreshSelectedTargetInstallStateFast();

            UnityCliLoopSettingsSkillsSnapshot snapshot = _presenter.GetSnapshot();
            Assert.That(snapshot.SelectedTargetInstallState, Is.EqualTo(SkillInstallState.Missing));
            Assert.That(snapshot.InstallableSkillTargets, Is.Empty);
            Assert.That(snapshot.HasSkillTargetScanResult, Is.True);
            Assert.That(_skillPort.FastScanGroupFlags, Is.Empty);
            Assert.That(_sectionRefreshCalls, Is.EqualTo(new List<bool> { true }));
        }

        /// <summary>
        /// Verifies the fast refresh scans the flat layout at the project root, takes the selected target's
        /// install state, and keeps only targets that have a skills directory.
        /// </summary>
        [Test]
        public void RefreshSelectedTargetInstallStateFast_WithACli_ReadsTheSelectedTargetFromTheFastScan()
        {
            _cliDetector.IsCliInstalledValue = true;
            _skillPort.FastTargets = new List<SkillSetupTargetInfo>
            {
                CreateTarget(".codex", SkillInstallState.Outdated, hasSkillsDirectory: true),
                CreateTarget(".claude", SkillInstallState.Installed, hasSkillsDirectory: true),
                CreateTarget(".agents", SkillInstallState.Missing, hasSkillsDirectory: false)
            };

            _presenter.RefreshSelectedTargetInstallStateFast();

            UnityCliLoopSettingsSkillsSnapshot snapshot = _presenter.GetSnapshot();
            Assert.That(snapshot.SelectedTargetInstallState, Is.EqualTo(SkillInstallState.Installed));
            Assert.That(
                snapshot.InstallableSkillTargets.Select(target => target.DirName).ToArray(),
                Is.EqualTo(new[] { ".codex", ".claude" }));
            Assert.That(snapshot.HasSkillTargetScanResult, Is.True);
            Assert.That(_skillPort.FastScanGroupFlags, Is.EqualTo(new List<bool> { false }));
            Assert.That(_skillPort.FastScanProjectRoots[0], Is.EqualTo(UnityCliLoopPathResolver.GetProjectRoot()));
            Assert.That(_skillPort.FullScanCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies a scan that does not list the selected target reports it as missing.
        /// </summary>
        [Test]
        public void RefreshSelectedTargetInstallStateFast_WhenTheSelectedTargetIsAbsent_ReportsMissing()
        {
            _cliDetector.IsCliInstalledValue = true;
            _skillPort.FastTargets = new List<SkillSetupTargetInfo>
            {
                CreateTarget(".codex", SkillInstallState.Installed, hasSkillsDirectory: true)
            };
            _presenter.MarkSelectedTargetInstallStateChecking();

            _presenter.RefreshSelectedTargetInstallStateFast();

            Assert.That(_presenter.GetSnapshot().SelectedTargetInstallState, Is.EqualTo(SkillInstallState.Missing));
        }

        /// <summary>
        /// Verifies that a refresh which cannot start without a CLI replaces a checking state with missing and
        /// refreshes the section once.
        /// </summary>
        [Test]
        public void RefreshSelectedTargetInstallStateInBackground_WithoutACli_ResetsACheckingStateToMissing()
        {
            _cliDetector.IsCliInstalledValue = false;
            _presenter.MarkSelectedTargetInstallStateChecking();

            _presenter.RefreshSelectedTargetInstallStateInBackground();

            Assert.That(_presenter.GetSnapshot().SelectedTargetInstallState, Is.EqualTo(SkillInstallState.Missing));
            Assert.That(_sectionRefreshCalls, Is.EqualTo(new List<bool> { true }));
        }

        /// <summary>
        /// Verifies an unchanged missing state does not refresh the section again.
        /// </summary>
        [Test]
        public void RefreshSelectedTargetInstallStateInBackground_WithoutACli_WhenAlreadyMissing_SkipsTheRefresh()
        {
            _cliDetector.IsCliInstalledValue = false;

            _presenter.RefreshSelectedTargetInstallStateInBackground();

            Assert.That(_sectionRefreshCalls, Is.Empty);
        }

        /// <summary>
        /// Verifies choosing another target is reflected in the snapshot even before a CLI exists.
        /// </summary>
        [Test]
        public void HandleSkillsTargetChanged_WithoutACli_SwitchesTheSelectedTarget()
        {
            _cliDetector.IsCliInstalledValue = false;

            _presenter.HandleSkillsTargetChanged(SkillsTarget.Codex);

            Assert.That(_presenter.GetSnapshot().SkillsTarget, Is.EqualTo(SkillsTarget.Codex));
            Assert.That(_presenter.GetSnapshot().SelectedTargetInstallState, Is.EqualTo(SkillInstallState.Missing));
        }

        /// <summary>
        /// Verifies a layout change re-applies the flat layout and refreshes the selected target's install state
        /// from the scan.
        /// </summary>
        [Test]
        public void HandleGroupSkillsChanged_PersistsTheLayoutAndRefreshesTheInstallState()
        {
            _cliDetector.IsCliInstalledValue = true;
            // A CLI refresh in progress keeps the background full scan (Task.Run) from starting during the
            // test; that guard itself is covered by UnityCliLoopSettingsWindowRefreshPolicyTests.
            _isRefreshingVersion = true;
            _skillPort.FastTargets = new List<SkillSetupTargetInfo>
            {
                CreateTarget(".claude", SkillInstallState.Outdated, hasSkillsDirectory: true)
            };

            _presenter.HandleGroupSkillsChanged(true);

            Assert.That(_editorSettingsPort.InstallSkillsFlatValues, Is.EqualTo(new List<bool> { true }));
            Assert.That(_presenter.GetSnapshot().SelectedTargetInstallState, Is.EqualTo(SkillInstallState.Outdated));
            Assert.That(_skillPort.FastScanGroupFlags.Count, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies disabling a tool removes its skill files and installs nothing.
        /// </summary>
        [Test]
        public async Task ApplyToolToggleSideEffects_WhenDisabled_RemovesTheToolSkill()
        {
            await _presenter.ApplyToolToggleSideEffects("sample-tool", false);

            Assert.That(_skillPort.RemovedTools, Is.EqualTo(new List<string> { "sample-tool" }));
            Assert.That(_skillPort.InstalledToolSkills, Is.Empty);
        }

        /// <summary>
        /// Verifies enabling a tool installs its skill in the flat layout without a warning when the skill is
        /// present afterwards.
        /// </summary>
        [Test]
        public async Task ApplyToolToggleSideEffects_WhenEnabled_InstallsTheToolSkillFlat()
        {
            _skillPort.InstalledToolNames.Add("sample-tool");

            await _presenter.ApplyToolToggleSideEffects("sample-tool", true);

            Assert.That(_skillPort.InstalledToolSkills, Is.EqualTo(new List<string> { "sample-tool|group=False" }));
            Assert.That(_skillPort.RemovedTools, Is.Empty);
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>
        /// Verifies an enabled tool whose skill did not appear logs a warning about the skill source layout.
        /// </summary>
        [Test]
        public async Task ApplyToolToggleSideEffects_WhenTheSkillIsStillMissing_WarnsAboutTheSkillSource()
        {
            LogAssert.Expect(LogType.Warning, new Regex("Skill for 'sample-tool' was not installed after enabling"));

            await _presenter.ApplyToolToggleSideEffects("sample-tool", true);

            Assert.That(_skillPort.InstalledToolSkills.Count, Is.EqualTo(1));
        }

        private static SkillSetupTargetInfo CreateTarget(string dirName, SkillInstallState installState, bool hasSkillsDirectory)
        {
            return new SkillSetupTargetInfo(
                dirName,
                dirName,
                "--flag",
                hasSkillsDirectory,
                hasExistingSkills: installState != SkillInstallState.Missing,
                hasDifferentLayoutSkills: false,
                installState);
        }

        private sealed class RecordingSkillSetupPort : ISkillSetupPort
        {
            internal List<SkillSetupTargetInfo> FastTargets { get; set; } = new List<SkillSetupTargetInfo>();
            internal List<string> FastScanProjectRoots { get; } = new List<string>();
            internal List<bool> FastScanGroupFlags { get; } = new List<bool>();
            internal int FullScanCount { get; private set; }
            internal HashSet<string> InstalledToolNames { get; } = new HashSet<string>();
            internal List<string> RemovedTools { get; } = new List<string>();
            internal List<string> InstalledToolSkills { get; } = new List<string>();

            public List<SkillSetupTargetInfo> DetectSkillTargetsForLayoutFastAtProjectRoot(
                string projectRoot,
                bool groupSkillsUnderUnityCliLoop)
            {
                FastScanProjectRoots.Add(projectRoot);
                FastScanGroupFlags.Add(groupSkillsUnderUnityCliLoop);
                return FastTargets;
            }

            public List<SkillSetupTargetInfo> DetectSkillTargetsForLayoutAtProjectRoot(
                string projectRoot,
                bool groupSkillsUnderUnityCliLoop)
            {
                FullScanCount++;
                return new List<SkillSetupTargetInfo>();
            }

            public void RemoveSkillFiles(string toolName)
            {
                RemovedTools.Add(toolName);
            }

            public bool IsSkillInstalled(string toolName)
            {
                return InstalledToolNames.Contains(toolName);
            }

            public Task InstallSkillFilesForToolAsync(
                string toolName,
                bool groupSkillsUnderUnityCliLoop,
                CancellationToken ct)
            {
                InstalledToolSkills.Add($"{toolName}|group={groupSkillsUnderUnityCliLoop}");
                return Task.CompletedTask;
            }

            public Task InstallSkillFilesAsync(
                List<SkillSetupTargetInfo> targets,
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

        private sealed class StubCliInstallationDetector : ICliInstallationDetector
        {
            internal bool IsCliInstalledValue { get; set; }

            public bool IsCliInstalled()
            {
                return IsCliInstalledValue;
            }

            public string GetCachedCliVersion() => throw new NotSupportedException();
            public bool GetCachedCliIsDispatcher() => throw new NotSupportedException();
            public string GetCachedCliExecutablePath() => throw new NotSupportedException();
            public bool IsCheckCompleted() => throw new NotSupportedException();
            public Task RefreshCliVersionAsync(CancellationToken ct) => throw new NotSupportedException();
            public Task ForceRefreshCliVersionAsync(CancellationToken ct) => throw new NotSupportedException();

            public Task<bool> IsCliVisibleFromShellAsync(RuntimePlatform platform, CancellationToken ct) =>
                throw new NotSupportedException();

            public void InvalidateCache() => throw new NotSupportedException();
        }

        private sealed class RecordingEditorSettingsPort : IUnityCliLoopEditorSettingsPort
        {
            internal List<bool> InstallSkillsFlatValues { get; } = new List<bool>();

            public void SetInstallSkillsFlat(bool installSkillsFlat)
            {
                InstallSkillsFlatValues.Add(installSkillsFlat);
            }

            public void RecoverSettingsFileIfNeeded() => throw new NotSupportedException();
            public UnityCliLoopEditorSettingsData GetSettings() => throw new NotSupportedException();
            public void SaveSettings(UnityCliLoopEditorSettingsData settings) => throw new NotSupportedException();

            public void UpdateSettings(Func<UnityCliLoopEditorSettingsData, UnityCliLoopEditorSettingsData> transform) =>
                throw new NotSupportedException();

            public string GetLastSeenSetupWizardVersion() => throw new NotSupportedException();
            public bool GetSuppressSetupWizardAutoShow() => throw new NotSupportedException();
            public void SetSuppressSetupWizardAutoShow(bool suppressAutoShow) => throw new NotSupportedException();
            public void SetShowToolSettings(bool showToolSettings) => throw new NotSupportedException();
        }

        private sealed class UnusedNativeCliInstaller : INativeCliInstaller
        {
            public bool IsPackageOwnedCurrentUserInstallPath(string cliExecutablePath, RuntimePlatform platform) =>
                throw new NotSupportedException();

            public ManagedCliKind ResolveManagedCliKind(string cliExecutablePath) => throw new NotSupportedException();
            public bool HasPackageOwnedCurrentUserInstall(RuntimePlatform platform) => throw new NotSupportedException();

            public Task<CliInstallResult> InstallGlobalCliAsync(
                RuntimePlatform platform,
                string dispatcherReleaseTag,
                string dispatcherArchiveManifest,
                IProgress<string> installProgress,
                CancellationToken ct) => throw new NotSupportedException();

            public Task<CliInstallResult> UninstallGlobalCliAsync(RuntimePlatform platform, CancellationToken ct) =>
                throw new NotSupportedException();

            public Task<CliPathSetupPlan> GetGlobalCliPathSetupPlanAsync(RuntimePlatform platform, CancellationToken ct) =>
                throw new NotSupportedException();

            public CliPathSetupApplyResult ApplyGlobalCliPathSetup(CliPathSetupPlan plan) =>
                throw new NotSupportedException();

            public NativeCliInstallCommandLoadResult GetGlobalCliInstallCommand(
                RuntimePlatform platform,
                string dispatcherReleaseTag,
                string dispatcherArchiveManifest,
                bool removeLegacyLaunchers) => throw new NotSupportedException();
        }

        private sealed class UnusedCliPinReader : ICliPinReader
        {
            public string LoadMinimumDispatcherVersionOrThrow() => throw new NotSupportedException();
            public CliPinLoadResult LoadPackagePin() => throw new NotSupportedException();
            public DispatcherBootstrapPinLoadResult LoadDispatcherBootstrapPin() => throw new NotSupportedException();
        }
    }
}
