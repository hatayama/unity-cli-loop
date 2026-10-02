using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Presentation;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the Settings CLI setup presenter's section refresh, background version and PATH checks,
    /// and the primary-button paths that end without a dialog, against the real settings view.
    /// </summary>
    public sealed class UnityCliLoopSettingsCliSetupPresenterFlowTests
    {
        private const string MinimumDispatcherVersion = "3.0.0";

        private VisualElement _root;
        private UnityCliLoopSettingsWindowUI _view;
        private StubCliInstallationDetector _cliDetector;
        private RecordingNativeCliInstaller _nativeCliInstaller;
        private StubCliPinReader _pinReader;
        private UnityCliLoopSettingsCliSetupPresenter _presenter;
        private bool _includeSkillScanResult;
        private int _skillsRefreshCount;
        private List<bool> _refreshAllSectionsCalls;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            _view = new UnityCliLoopSettingsWindowUI(_root);
            _cliDetector = new StubCliInstallationDetector();
            _nativeCliInstaller = new RecordingNativeCliInstaller();
            _pinReader = new StubCliPinReader();
            _includeSkillScanResult = true;
            _skillsRefreshCount = 0;
            _refreshAllSectionsCalls = new List<bool>();
            _presenter = new UnityCliLoopSettingsCliSetupPresenter(
                _view,
                new CliSetupApplicationService(_cliDetector, _nativeCliInstaller, _pinReader));
            _presenter.BindCoordination(
                () => new UnityCliLoopSettingsSkillsSnapshot(
                    installSkillsFlat: true,
                    SkillInstallState.Missing,
                    SkillsTarget.Claude,
                    isInstallingSkills: false,
                    new List<SkillSetupTargetInfo>(),
                    _includeSkillScanResult),
                () => _skillsRefreshCount++,
                refreshSkills => _refreshAllSectionsCalls.Add(refreshSkills));
        }

        [TearDown]
        public void TearDown()
        {
            _view.Dispose();
        }

        [Test]
        public void RefreshSection_WhileTheCliCheckIsPending_ShowsChecking()
        {
            // Verifies an unfinished CLI check renders the checking label and button.
            _cliDetector.IsCheckCompletedValue = false;

            _presenter.RefreshSection();

            Assert.That(_root.Q<Label>("cli-status-label").text, Is.EqualTo("CLI: Checking..."));
            Assert.That(_root.Q<Button>("install-cli-button").text, Is.EqualTo("Checking..."));
        }

        [Test]
        public void RefreshSection_WithAPackageOwnedCurrentDispatcher_OffersUninstall()
        {
            // Verifies a current dispatcher in the package-owned install path is offered for uninstall.
            _cliDetector.CliVersion = "3.1.0";
            _cliDetector.IsDispatcher = true;
            _nativeCliInstaller.IsPackageOwnedPath = true;

            _presenter.RefreshSection();

            Assert.That(_root.Q<Label>("cli-status-label").text, Is.EqualTo("CLI: v3.1.0"));
            Assert.That(_root.Q<Button>("install-cli-button").text, Is.EqualTo("Uninstall CLI"));
        }

        [Test]
        public void RefreshSection_WithAnOutdatedCli_OffersTheUpdateToTheMinimum()
        {
            // Verifies a CLI below the dispatcher minimum is offered an update to that minimum when the
            // bootstrap pin cannot name a newer target.
            _cliDetector.CliVersion = "2.0.0";
            _cliDetector.IsDispatcher = true;

            _presenter.RefreshSection();

            Assert.That(
                _root.Q<Button>("install-cli-button").text,
                Is.EqualTo(CliSetupLabelFormatter.GetCliReplacementButtonText("Update", "2.0.0", MinimumDispatcherVersion)));
        }

        [Test]
        public void RefreshSection_WithoutSkillDirectoryChecks_ShowsTheSkillsAsChecking()
        {
            // Verifies a refresh that skips skill directory checks keeps the skills panel in its checking state.
            _cliDetector.CliVersion = "3.1.0";
            _cliDetector.IsDispatcher = true;

            _presenter.RefreshSection(includeSkillDirectoryChecks: false);

            Assert.That(_root.Q<Label>("skill-target-status-summary").text, Is.EqualTo("Checking installed skills..."));
        }

        [Test]
        public void RefreshSection_WithAScannedEmptySkillList_ShowsTheResolvedSkillsPanel()
        {
            // Verifies a finished skill scan with no installable targets leaves the checking state.
            _cliDetector.CliVersion = "3.1.0";
            _cliDetector.IsDispatcher = true;

            _presenter.RefreshSection();

            Assert.That(_root.Q<Label>("skill-target-status-summary").text, Is.Empty);
        }

        [Test]
        public void ResolveCurrentPrimaryButtonAction_ResolvesFromTheCachedExecutablePath()
        {
            // Verifies the primary action asks the installer about the cached executable path and lets a
            // managed install disable the button.
            _cliDetector.CliVersion = "3.1.0";
            _cliDetector.IsDispatcher = true;
            _cliDetector.ExecutablePath = "<CLI_PATH>";
            _nativeCliInstaller.IsPackageOwnedPath = true;

            CliSetupPrimaryAction ownedAction = _presenter.ResolveCurrentPrimaryButtonAction(needsCliPathSetup: false);
            _nativeCliInstaller.IsPackageOwnedPath = false;
            _nativeCliInstaller.ManagedKind = ManagedCliKind.Homebrew;
            CliSetupPrimaryAction managedAction = _presenter.ResolveCurrentPrimaryButtonAction(needsCliPathSetup: false);

            Assert.That(ownedAction, Is.EqualTo(CliSetupPrimaryAction.Uninstall));
            Assert.That(managedAction, Is.EqualTo(CliSetupPrimaryAction.None));
            Assert.That(_nativeCliInstaller.OwnershipQueries, Is.EqualTo(new List<string> { "<CLI_PATH>", "<CLI_PATH>" }));
            Assert.That(_nativeCliInstaller.ManagedKindQueries, Is.EqualTo(new List<string> { "<CLI_PATH>", "<CLI_PATH>" }));
        }

        [Test]
        public async Task RefreshCliVersionInBackground_WhenAlreadyChecked_DoesNothing()
        {
            // Verifies a completed CLI check is not repeated and does not trigger a skills refresh.
            await _presenter.RefreshCliVersionInBackground();

            Assert.That(_cliDetector.RefreshCount, Is.EqualTo(0));
            Assert.That(_skillsRefreshCount, Is.EqualTo(0));
        }

        [Test]
        public async Task RefreshCliVersionInBackground_WhenNotChecked_RefreshesAndRequestsASkillsRefresh()
        {
            // Verifies the first CLI check runs once and then asks for the skills install state.
            _cliDetector.IsCheckCompletedValue = false;

            await _presenter.RefreshCliVersionInBackground();

            Assert.That(_cliDetector.RefreshCount, Is.EqualTo(1));
            Assert.That(_skillsRefreshCount, Is.EqualTo(1));
        }

        [Test]
        public async Task RefreshCliPathSetupInBackground_WithAHiddenPackageOwnedInstall_FlagsPathRepairOutsideWindows()
        {
            // Verifies a package-owned install the shell cannot see switches the button to PATH repair, except
            // on Windows where the PATH check never runs.
            _cliDetector.CliVersion = "3.1.0";
            _cliDetector.IsDispatcher = true;
            _cliDetector.IsVisibleFromShell = false;
            _nativeCliInstaller.IsPackageOwnedPath = true;
            _nativeCliInstaller.HasPackageOwnedInstall = true;
            bool isWindowsEditor = UnityEngine.Application.platform == RuntimePlatform.WindowsEditor;

            await _presenter.RefreshCliPathSetupInBackground();

            Assert.That(_cliDetector.ShellVisibilityChecks, Is.EqualTo(isWindowsEditor ? 0 : 1));
            Assert.That(
                _root.Q<Button>("install-cli-button").text,
                Is.EqualTo(isWindowsEditor ? "Uninstall CLI" : "Fix PATH"));
        }

        [Test]
        public async Task HandleInstallCli_WithAManagedCli_OnlyRefreshesTheState()
        {
            // Verifies a click on a package-manager-owned CLI re-checks the state and then does nothing.
            _cliDetector.CliVersion = "3.1.0";
            _cliDetector.IsDispatcher = true;
            _nativeCliInstaller.ManagedKind = ManagedCliKind.Homebrew;
            // A loadable pin keeps a wrongly reached install on its silent success path instead of the
            // modal failure dialog.
            _pinReader.BootstrapPin = DispatcherBootstrapPinLoadResult.FromSuccess("dispatcher-v3.1.0", "<MANIFEST>");

            await _presenter.HandleInstallCli();

            Assert.That(_cliDetector.ForceRefreshCount, Is.EqualTo(1));
            Assert.That(_nativeCliInstaller.InstallCalls, Is.Empty);
            Assert.That(_refreshAllSectionsCalls, Is.Empty);
            Assert.That(_presenter.IsRefreshingVersion, Is.False);
        }

        [Test]
        public async Task HandleInstallCli_WithoutACli_InstallsFromTheBootstrapPinAndRefreshesSkills()
        {
            // Verifies a first install uses the pinned dispatcher release and refreshes every section,
            // including skills, afterwards.
            _cliDetector.CliVersion = string.Empty;
            _cliDetector.IsCliInstalledValue = false;
            _pinReader.BootstrapPin = DispatcherBootstrapPinLoadResult.FromSuccess("dispatcher-v3.1.0", "<MANIFEST>");

            await _presenter.HandleInstallCli();

            Assert.That(_nativeCliInstaller.InstallCalls, Is.EqualTo(new List<string> { "dispatcher-v3.1.0|<MANIFEST>" }));
            Assert.That(_refreshAllSectionsCalls, Is.EqualTo(new List<bool> { true }));
        }

        [Test]
        public async Task HandleInstallCli_OverAnInstalledCli_RefreshesWithoutTheSkills()
        {
            // Verifies an update over an installed CLI refreshes the sections but not the skills.
            _cliDetector.CliVersion = "2.0.0";
            _cliDetector.IsDispatcher = true;
            _cliDetector.IsCliInstalledValue = true;
            _pinReader.BootstrapPin = DispatcherBootstrapPinLoadResult.FromSuccess("dispatcher-v3.1.0", "<MANIFEST>");

            await _presenter.HandleInstallCli();

            Assert.That(_nativeCliInstaller.InstallCalls.Count, Is.EqualTo(1));
            Assert.That(_refreshAllSectionsCalls, Is.EqualTo(new List<bool> { false }));
        }

        private sealed class StubCliInstallationDetector : ICliInstallationDetector
        {
            internal string CliVersion { get; set; } = string.Empty;
            internal bool IsDispatcher { get; set; }
            internal string ExecutablePath { get; set; } = string.Empty;
            internal bool IsCheckCompletedValue { get; set; } = true;
            internal bool IsCliInstalledValue { get; set; }
            internal bool IsVisibleFromShell { get; set; } = true;
            internal int RefreshCount { get; private set; }
            internal int ForceRefreshCount { get; private set; }
            internal int ShellVisibilityChecks { get; private set; }

            public string GetCachedCliVersion()
            {
                return CliVersion;
            }

            public bool GetCachedCliIsDispatcher()
            {
                return IsDispatcher;
            }

            public string GetCachedCliExecutablePath()
            {
                return ExecutablePath;
            }

            public bool IsCheckCompleted()
            {
                return IsCheckCompletedValue;
            }

            public bool IsCliInstalled()
            {
                return IsCliInstalledValue;
            }

            public Task RefreshCliVersionAsync(CancellationToken ct)
            {
                RefreshCount++;
                return Task.CompletedTask;
            }

            public Task ForceRefreshCliVersionAsync(CancellationToken ct)
            {
                ForceRefreshCount++;
                return Task.CompletedTask;
            }

            public Task<bool> IsCliVisibleFromShellAsync(RuntimePlatform platform, CancellationToken ct)
            {
                ShellVisibilityChecks++;
                return Task.FromResult(IsVisibleFromShell);
            }

            public void InvalidateCache()
            {
            }
        }

        private sealed class RecordingNativeCliInstaller : INativeCliInstaller
        {
            internal bool IsPackageOwnedPath { get; set; }
            internal bool HasPackageOwnedInstall { get; set; }
            internal ManagedCliKind ManagedKind { get; set; } = ManagedCliKind.None;
            internal List<string> OwnershipQueries { get; } = new List<string>();
            internal List<string> ManagedKindQueries { get; } = new List<string>();
            internal List<string> InstallCalls { get; } = new List<string>();

            public bool IsPackageOwnedCurrentUserInstallPath(string cliExecutablePath, RuntimePlatform platform)
            {
                OwnershipQueries.Add(cliExecutablePath);
                return IsPackageOwnedPath;
            }

            public ManagedCliKind ResolveManagedCliKind(string cliExecutablePath)
            {
                ManagedKindQueries.Add(cliExecutablePath);
                return ManagedKind;
            }

            public bool HasPackageOwnedCurrentUserInstall(RuntimePlatform platform)
            {
                return HasPackageOwnedInstall;
            }

            public Task<CliInstallResult> InstallGlobalCliAsync(
                RuntimePlatform platform,
                string dispatcherReleaseTag,
                string dispatcherArchiveManifest,
                IProgress<string> installProgress,
                CancellationToken ct)
            {
                InstallCalls.Add($"{dispatcherReleaseTag}|{dispatcherArchiveManifest}");
                return Task.FromResult(new CliInstallResult(true, string.Empty));
            }

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

        private sealed class StubCliPinReader : ICliPinReader
        {
            internal DispatcherBootstrapPinLoadResult BootstrapPin { get; set; } =
                DispatcherBootstrapPinLoadResult.FromFailure("no bootstrap pin in this test");

            public string LoadMinimumDispatcherVersionOrThrow()
            {
                return MinimumDispatcherVersion;
            }

            public DispatcherBootstrapPinLoadResult LoadDispatcherBootstrapPin()
            {
                return BootstrapPin;
            }

            public CliPinLoadResult LoadPackagePin() => throw new NotSupportedException();
        }
    }
}
