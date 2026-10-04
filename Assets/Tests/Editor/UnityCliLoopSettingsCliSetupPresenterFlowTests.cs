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
    /// and the primary-button install, PATH repair, and uninstall paths with recorded dialogs, against the real
    /// settings view.
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
        private RecordingPresentationDialogs _dialogs;
        private CliSetupApplicationService _cliSetupApplicationService;

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
            _dialogs = new RecordingPresentationDialogs();
            _cliSetupApplicationService = new CliSetupApplicationService(_cliDetector, _nativeCliInstaller, _pinReader);
            _presenter = new UnityCliLoopSettingsCliSetupPresenter(_view, _cliSetupApplicationService, _dialogs);
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

        /// <summary>
        /// Verifies an unfinished CLI check renders the checking label and button.
        /// </summary>
        [Test]
        public void RefreshSection_WhileTheCliCheckIsPending_ShowsChecking()
        {
            _cliDetector.IsCheckCompletedValue = false;

            _presenter.RefreshSection();

            Assert.That(_root.Q<Label>("cli-status-label").text, Is.EqualTo("CLI: Checking..."));
            Assert.That(_root.Q<Button>("install-cli-button").text, Is.EqualTo("Checking..."));
        }

        /// <summary>
        /// Verifies a current dispatcher in the package-owned install path is offered for uninstall.
        /// </summary>
        [Test]
        public void RefreshSection_WithAPackageOwnedCurrentDispatcher_OffersUninstall()
        {
            _cliDetector.CliVersion = "3.1.0";
            _cliDetector.IsDispatcher = true;
            _nativeCliInstaller.IsPackageOwnedPath = true;

            _presenter.RefreshSection();

            Assert.That(_root.Q<Label>("cli-status-label").text, Is.EqualTo("CLI: v3.1.0"));
            Assert.That(_root.Q<Button>("install-cli-button").text, Is.EqualTo("Uninstall CLI"));
        }

        /// <summary>
        /// Verifies a CLI below the dispatcher minimum is offered an update to that minimum when the bootstrap pin
        /// cannot name a newer target.
        /// </summary>
        [Test]
        public void RefreshSection_WithAnOutdatedCli_OffersTheUpdateToTheMinimum()
        {
            _cliDetector.CliVersion = "2.0.0";
            _cliDetector.IsDispatcher = true;

            _presenter.RefreshSection();

            Assert.That(
                _root.Q<Button>("install-cli-button").text,
                Is.EqualTo(CliSetupLabelFormatter.GetCliReplacementButtonText("Update", "2.0.0", MinimumDispatcherVersion)));
        }

        /// <summary>
        /// Verifies a refresh that skips skill directory checks keeps the skills panel in its checking state.
        /// </summary>
        [Test]
        public void RefreshSection_WithoutSkillDirectoryChecks_ShowsTheSkillsAsChecking()
        {
            _cliDetector.CliVersion = "3.1.0";
            _cliDetector.IsDispatcher = true;

            _presenter.RefreshSection(includeSkillDirectoryChecks: false);

            Assert.That(_root.Q<Label>("skill-target-status-summary").text, Is.EqualTo("Checking installed skills..."));
        }

        /// <summary>
        /// Verifies a finished skill scan with no installable targets leaves the checking state.
        /// </summary>
        [Test]
        public void RefreshSection_WithAScannedEmptySkillList_ShowsTheResolvedSkillsPanel()
        {
            _cliDetector.CliVersion = "3.1.0";
            _cliDetector.IsDispatcher = true;

            _presenter.RefreshSection();

            Assert.That(_root.Q<Label>("skill-target-status-summary").text, Is.Empty);
        }

        /// <summary>
        /// Verifies the primary action asks the installer about the cached executable path and lets a managed
        /// install disable the button.
        /// </summary>
        [Test]
        public void ResolveCurrentPrimaryButtonAction_ResolvesFromTheCachedExecutablePath()
        {
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

        /// <summary>
        /// Verifies a completed CLI check is not repeated and does not trigger a skills refresh.
        /// </summary>
        [Test]
        public async Task RefreshCliVersionInBackground_WhenAlreadyChecked_DoesNothing()
        {
            await _presenter.RefreshCliVersionInBackground();

            Assert.That(_cliDetector.RefreshCount, Is.EqualTo(0));
            Assert.That(_skillsRefreshCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies the first CLI check runs once and then asks for the skills install state.
        /// </summary>
        [Test]
        public async Task RefreshCliVersionInBackground_WhenNotChecked_RefreshesAndRequestsASkillsRefresh()
        {
            _cliDetector.IsCheckCompletedValue = false;

            await _presenter.RefreshCliVersionInBackground();

            Assert.That(_cliDetector.RefreshCount, Is.EqualTo(1));
            Assert.That(_skillsRefreshCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies a package-owned install the shell cannot see switches the button to PATH repair, except on
        /// Windows where the PATH check never runs and the section is not redrawn.
        /// </summary>
        [Test]
        public async Task RefreshCliPathSetupInBackground_WithAHiddenPackageOwnedInstall_FlagsPathRepairOutsideWindows()
        {
            _cliDetector.CliVersion = "3.1.0";
            _cliDetector.IsDispatcher = true;
            _cliDetector.IsVisibleFromShell = false;
            _nativeCliInstaller.IsPackageOwnedPath = true;
            _nativeCliInstaller.HasPackageOwnedInstall = true;
            bool isWindowsEditor = UnityEngine.Application.platform == RuntimePlatform.WindowsEditor;
            string buttonTextBefore = _root.Q<Button>("install-cli-button").text;

            await _presenter.RefreshCliPathSetupInBackground();

            Assert.That(_cliDetector.ShellVisibilityChecks, Is.EqualTo(isWindowsEditor ? 0 : 1));
            Assert.That(
                _root.Q<Button>("install-cli-button").text,
                Is.EqualTo(isWindowsEditor ? buttonTextBefore : "Fix PATH"));
        }

        /// <summary>
        /// Verifies a click on a package-manager-owned CLI re-checks the state and then does nothing.
        /// </summary>
        [Test]
        public async Task HandleInstallCli_WithAManagedCli_OnlyRefreshesTheState()
        {
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

        /// <summary>
        /// Verifies a first install uses the pinned dispatcher release, runs the PATH setup once, and refreshes
        /// every section, including skills, afterwards.
        /// </summary>
        [Test]
        public async Task HandleInstallCli_WithoutACli_InstallsFromTheBootstrapPinAndRefreshesSkills()
        {
            _cliDetector.CliVersion = string.Empty;
            _cliDetector.IsCliInstalledValue = false;
            _pinReader.BootstrapPin = DispatcherBootstrapPinLoadResult.FromSuccess("dispatcher-v3.1.0", "<MANIFEST>");

            await _presenter.HandleInstallCli();

            Assert.That(_nativeCliInstaller.InstallCalls, Is.EqualTo(new List<string> { "dispatcher-v3.1.0|<MANIFEST>" }));
            AssertPathSetupRanOnce();
            Assert.That(_dialogs.MessageTitles, Is.Empty);
            Assert.That(_refreshAllSectionsCalls, Is.EqualTo(new List<bool> { true }));
        }

        /// <summary>
        /// Verifies an update over an installed CLI refreshes the sections but not the skills.
        /// </summary>
        [Test]
        public async Task HandleInstallCli_OverAnInstalledCli_RefreshesWithoutTheSkills()
        {
            _cliDetector.CliVersion = "2.0.0";
            _cliDetector.IsDispatcher = true;
            _cliDetector.IsCliInstalledValue = true;
            _pinReader.BootstrapPin = DispatcherBootstrapPinLoadResult.FromSuccess("dispatcher-v3.1.0", "<MANIFEST>");

            await _presenter.HandleInstallCli();

            Assert.That(_nativeCliInstaller.InstallCalls.Count, Is.EqualTo(1));
            Assert.That(_refreshAllSectionsCalls, Is.EqualTo(new List<bool> { false }));
        }

        /// <summary>
        /// Verifies a failed install shows the installer error with the manual install command, skips the PATH
        /// setup, and still refreshes every section.
        /// </summary>
        [Test]
        public async Task HandleInstallCli_WhenTheInstallFails_ShowsTheErrorWithTheManualCommand()
        {
            _cliDetector.CliVersion = string.Empty;
            _pinReader.BootstrapPin = DispatcherBootstrapPinLoadResult.FromSuccess("dispatcher-v3.1.0", "<MANIFEST>");
            _nativeCliInstaller.InstallResult = new CliInstallResult(false, "<INSTALL_ERROR>");
            _nativeCliInstaller.InstallCommandResult = NativeCliInstallCommandLoadResult.FromSuccess(
                new NativeCliInstallCommand("<FILE>", "<ARGS>", "<MANUAL_COMMAND>"));

            await _presenter.HandleInstallCli();

            Assert.That(_dialogs.MessageTitles, Is.EqualTo(new List<string> { "Installation Failed" }));
            Assert.That(
                _dialogs.Messages,
                Is.EqualTo(new List<string> { "Failed to install uLoop CLI.\n\n<INSTALL_ERROR>\n\n<MANUAL_COMMAND>" }));
            Assert.That(_dialogs.CliPathSetupCount, Is.EqualTo(0));
            Assert.That(_refreshAllSectionsCalls, Is.EqualTo(new List<bool> { true }));
        }

        /// <summary>
        /// Verifies a failed install whose manual command cannot be built shows the command error instead.
        /// </summary>
        [Test]
        public async Task HandleInstallCli_WhenTheManualCommandIsUnavailable_ShowsTheCommandError()
        {
            _cliDetector.CliVersion = string.Empty;
            _pinReader.BootstrapPin = DispatcherBootstrapPinLoadResult.FromSuccess("dispatcher-v3.1.0", "<MANIFEST>");
            _nativeCliInstaller.InstallResult = new CliInstallResult(false, "<INSTALL_ERROR>");
            _nativeCliInstaller.InstallCommandResult = NativeCliInstallCommandLoadResult.FromFailure("<COMMAND_ERROR>");

            await _presenter.HandleInstallCli();

            Assert.That(
                _dialogs.Messages,
                Is.EqualTo(new List<string> { "Failed to install uLoop CLI.\n\n<INSTALL_ERROR>\n\n<COMMAND_ERROR>" }));
        }

        /// <summary>
        /// Verifies a click on PATH repair for a hidden package-owned install runs the PATH setup instead of an
        /// install, checks the shell again, and refreshes the sections without the skills.
        /// </summary>
        [Test]
        public async Task HandleInstallCli_WhenThePathNeedsRepair_RunsThePathSetupWithoutInstalling()
        {
            Assume.That(UnityEngine.Application.platform, Is.Not.EqualTo(RuntimePlatform.WindowsEditor));
            _cliDetector.CliVersion = "3.1.0";
            _cliDetector.IsDispatcher = true;
            _cliDetector.IsVisibleFromShell = false;
            _nativeCliInstaller.HasPackageOwnedInstall = true;
            await _presenter.RefreshCliPathSetupInBackground();

            await _presenter.HandleInstallCli();

            AssertPathSetupRanOnce();
            Assert.That(_cliDetector.ShellVisibilityChecks, Is.EqualTo(3));
            Assert.That(_nativeCliInstaller.InstallCalls, Is.Empty);
            Assert.That(_refreshAllSectionsCalls, Is.EqualTo(new List<bool> { false }));
        }

        /// <summary>
        /// Verifies declining the uninstall confirmation leaves the CLI and the sections untouched.
        /// </summary>
        [Test]
        public async Task HandleInstallCli_WhenTheUninstallIsDeclined_DoesNotUninstall()
        {
            _cliDetector.CliVersion = "3.1.0";
            _cliDetector.IsDispatcher = true;
            _nativeCliInstaller.IsPackageOwnedPath = true;
            _dialogs.CliUninstallConfirmResult = false;

            await _presenter.HandleInstallCli();

            Assert.That(_dialogs.CliUninstallConfirmCount, Is.EqualTo(1));
            Assert.That(_nativeCliInstaller.UninstallCount, Is.EqualTo(0));
            Assert.That(_refreshAllSectionsCalls, Is.Empty);
        }

        /// <summary>
        /// Verifies a confirmed uninstall of a package-owned CLI removes it without a dialog and refreshes every
        /// section, including skills.
        /// </summary>
        [Test]
        public async Task HandleInstallCli_WhenTheUninstallIsConfirmed_UninstallsAndRefreshes()
        {
            _cliDetector.CliVersion = "3.1.0";
            _cliDetector.IsDispatcher = true;
            _nativeCliInstaller.IsPackageOwnedPath = true;

            await _presenter.HandleInstallCli();

            Assert.That(_dialogs.CliUninstallConfirmCount, Is.EqualTo(1));
            Assert.That(_nativeCliInstaller.UninstallCount, Is.EqualTo(1));
            Assert.That(_dialogs.MessageTitles, Is.Empty);
            Assert.That(_refreshAllSectionsCalls, Is.EqualTo(new List<bool> { true }));
        }

        /// <summary>
        /// Verifies a failed uninstall shows the uninstaller error and still refreshes every section.
        /// </summary>
        [Test]
        public async Task HandleInstallCli_WhenTheUninstallFails_ShowsTheError()
        {
            _cliDetector.CliVersion = "3.1.0";
            _cliDetector.IsDispatcher = true;
            _nativeCliInstaller.IsPackageOwnedPath = true;
            _nativeCliInstaller.UninstallResult = new CliInstallResult(false, "<UNINSTALL_ERROR>");

            await _presenter.HandleInstallCli();

            Assert.That(_dialogs.MessageTitles, Is.EqualTo(new List<string> { "Uninstallation Failed" }));
            Assert.That(
                _dialogs.Messages,
                Is.EqualTo(new List<string> { "Failed to uninstall uloop CLI.\n\n<UNINSTALL_ERROR>" }));
            Assert.That(_refreshAllSectionsCalls, Is.EqualTo(new List<bool> { true }));
        }

        private void AssertPathSetupRanOnce()
        {
            Assert.That(_dialogs.CliPathSetupCount, Is.EqualTo(1));
            Assert.That(
                _dialogs.CliPathSetupPlatforms,
                Is.EqualTo(new List<RuntimePlatform> { UnityEngine.Application.platform }));
            Assert.That(_dialogs.CliPathSetupServices[0], Is.SameAs(_cliSetupApplicationService));
            Assert.That(_dialogs.CliPathSetupTokens, Is.EqualTo(new List<CancellationToken> { CancellationToken.None }));
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
            internal CliInstallResult InstallResult { get; set; } = new CliInstallResult(true, string.Empty);
            internal NativeCliInstallCommandLoadResult InstallCommandResult { get; set; } =
                NativeCliInstallCommandLoadResult.FromFailure("no install command in this test");
            internal CliInstallResult UninstallResult { get; set; } = new CliInstallResult(true, string.Empty);
            internal int UninstallCount { get; private set; }

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
                return Task.FromResult(InstallResult);
            }

            public Task<CliInstallResult> UninstallGlobalCliAsync(RuntimePlatform platform, CancellationToken ct)
            {
                UninstallCount++;
                return Task.FromResult(UninstallResult);
            }

            public Task<CliPathSetupPlan> GetGlobalCliPathSetupPlanAsync(RuntimePlatform platform, CancellationToken ct) =>
                throw new NotSupportedException();

            public CliPathSetupApplyResult ApplyGlobalCliPathSetup(CliPathSetupPlan plan) =>
                throw new NotSupportedException();

            public NativeCliInstallCommandLoadResult GetGlobalCliInstallCommand(
                RuntimePlatform platform,
                string dispatcherReleaseTag,
                string dispatcherArchiveManifest,
                bool removeLegacyLaunchers)
            {
                return InstallCommandResult;
            }
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
