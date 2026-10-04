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
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the setup wizard's CLI-step refresh, skills-step synchronous paths, and the initial
    /// checking state, using named stand-in elements and recording ports instead of the real window.
    /// </summary>
    public sealed class SetupWizardWorkflowControllersTests
    {
        private const string MinimumDispatcherVersion = "3.0.0";

        private VisualElement _root;
        private RecordingEditorSettingsPort _editorSettingsPort;
        private StubCliInstallationDetector _cliDetector;
        private StubNativeCliInstaller _nativeCliInstaller;
        private RecordingSkillSetupPort _skillPort;
        private CliSetupApplicationService _cliSetupApplicationService;
        private int _resizeCount;
        private List<bool> _refreshUiCalls;
        private RecordingPresentationDialogs _dialogs;
        private InlineBackgroundWorkRunner _backgroundWorkRunner;

        [SetUp]
        public void SetUp()
        {
            _root = CreateRootElement();
            _editorSettingsPort = new RecordingEditorSettingsPort();
            _cliDetector = new StubCliInstallationDetector();
            _nativeCliInstaller = new StubNativeCliInstaller();
            _skillPort = new RecordingSkillSetupPort();
            _cliSetupApplicationService = new CliSetupApplicationService(
                _cliDetector,
                _nativeCliInstaller,
                new StubCliPinReader());
            _resizeCount = 0;
            _refreshUiCalls = new List<bool>();
            _dialogs = new RecordingPresentationDialogs();
            _backgroundWorkRunner = new InlineBackgroundWorkRunner();
        }

        /// <summary>
        /// Verifies a refresh re-reads the CLI, skips the PATH check when the package does not own the install,
        /// and renders the installed state.
        /// </summary>
        [Test]
        public async Task CliRefreshAndUpdate_WithACurrentDispatcher_ReportsInstalledWithoutAShellCheck()
        {
            _cliDetector.CliVersion = "3.1.0";
            _cliDetector.IsDispatcher = true;
            SetupWizardCliWorkflowController controller = CreateCliWorkflow();

            bool installed = await controller.RefreshAndUpdateAsync(CancellationToken.None);

            Assert.That(installed, Is.True);
            Assert.That(_cliDetector.ForceRefreshCount, Is.EqualTo(1));
            Assert.That(_cliDetector.ShellVisibilityChecks, Is.EqualTo(0));
            Assert.That(_root.Q<Button>("install-cli-button").text, Is.EqualTo("Installed"));
        }

        /// <summary>
        /// Verifies an empty CLI version reports the CLI as missing and offers the install action.
        /// </summary>
        [Test]
        public async Task CliRefreshAndUpdate_WithoutACli_ReportsNotInstalled()
        {
            _cliDetector.CliVersion = string.Empty;
            SetupWizardCliWorkflowController controller = CreateCliWorkflow();

            bool installed = await controller.RefreshAndUpdateAsync(CancellationToken.None);

            Assert.That(installed, Is.False);
            Assert.That(_root.Q<Button>("install-cli-button").text, Is.EqualTo("Install CLI"));
        }

        /// <summary>
        /// Verifies a package-owned install that the shell cannot see asks for a PATH repair, except on Windows
        /// where the PATH check never runs.
        /// </summary>
        [Test]
        public async Task CliRefreshAndUpdate_WithAPackageOwnedInstallHiddenFromTheShell_OffersPathRepairOutsideWindows()
        {
            _cliDetector.CliVersion = "3.1.0";
            _cliDetector.IsDispatcher = true;
            _cliDetector.IsVisibleFromShell = false;
            _nativeCliInstaller.HasPackageOwnedInstall = true;
            SetupWizardCliWorkflowController controller = CreateCliWorkflow();
            bool isWindowsEditor = UnityEngine.Application.platform == RuntimePlatform.WindowsEditor;

            await controller.RefreshAndUpdateAsync(CancellationToken.None);

            Assert.That(_cliDetector.ShellVisibilityChecks, Is.EqualTo(isWindowsEditor ? 0 : 1));
            Assert.That(
                _root.Q<Button>("install-cli-button").text,
                Is.EqualTo(isWindowsEditor ? "Installed" : "Fix PATH"));
        }

        /// <summary>
        /// Verifies the skills step stores the forced flat layout preference and shows grouping off until the
        /// skill state is known.
        /// </summary>
        [Test]
        public void SkillsInitializeGroupSkillsToggle_PersistsTheFlatLayoutAndDisablesTheToggle()
        {
            SetupWizardSkillsWorkflowController controller = CreateSkillsWorkflow();

            controller.InitializeGroupSkillsToggle();

            Assert.That(_editorSettingsPort.InstallSkillsFlatValues, Is.EqualTo(new List<bool> { true }));
            Toggle groupToggle = _root.Q<Toggle>("group-skills-toggle");
            Assert.That(groupToggle.value, Is.False);
            Assert.That(groupToggle.enabledSelf, Is.False);
        }

        /// <summary>
        /// Verifies a refresh without a CLI renders the fast flat-layout scan, keeps the selected target install
        /// disabled, never starts the full background scan, and resizes once.
        /// </summary>
        [Test]
        public void SkillsRefreshSection_WithoutACli_ShowsTheFastScanWithoutTheFullScan()
        {
            _cliDetector.CliVersion = string.Empty;
            _skillPort.FastTargets = new List<SkillSetupTargetInfo>
            {
                CreateTarget("Claude Code", ".claude", SkillInstallState.Missing)
            };
            SetupWizardSkillsWorkflowController controller = CreateSkillsWorkflow();
            controller.InitializeGroupSkillsToggle();

            controller.RefreshSkillsSection();

            Assert.That(_skillPort.FastScanGroupFlags, Is.EqualTo(new List<bool> { false }));
            Assert.That(_skillPort.FastScanProjectRoots[0], Is.EqualTo(UnityCliLoopPathResolver.GetProjectRoot()));
            Assert.That(_skillPort.FullScanCount, Is.EqualTo(0));
            Assert.That(_resizeCount, Is.EqualTo(1));
            Assert.That(_root.Q<VisualElement>("skill-target-status-list").childCount, Is.EqualTo(1));
            Assert.That(_root.Q<Button>("install-selected-skills-button").enabledSelf, Is.False);
        }

        /// <summary>
        /// Verifies the fast skills state applied by the wizard refresh renders the scan but leaves the resize to
        /// its caller.
        /// </summary>
        [Test]
        public void SkillsApplyFastSkillsState_WithoutACli_RendersWithoutResizing()
        {
            _skillPort.FastTargets = new List<SkillSetupTargetInfo>
            {
                CreateTarget("Claude Code", ".claude", SkillInstallState.Installed),
                CreateTarget("Codex CLI", ".codex", SkillInstallState.Installed)
            };
            SetupWizardSkillsWorkflowController controller = CreateSkillsWorkflow();

            controller.ApplyFastSkillsState(cliInstalled: false);

            Assert.That(_skillPort.FastScanGroupFlags.Count, Is.EqualTo(1));
            Assert.That(_skillPort.FullScanCount, Is.EqualTo(0));
            Assert.That(_resizeCount, Is.EqualTo(0));
            Assert.That(_root.Q<VisualElement>("skill-target-status-list").childCount, Is.EqualTo(2));
        }

        /// <summary>
        /// Verifies a refresh with a CLI renders the fast scan first and then the full scan run through the
        /// background runner, resizing after each.
        /// </summary>
        [Test]
        public void SkillsRefreshSection_WithACli_AppliesTheFullScanAfterTheFastScan()
        {
            _cliDetector.CliVersion = "3.1.0";
            _skillPort.FastTargets = new List<SkillSetupTargetInfo>
            {
                CreateTarget("Claude Code", ".claude", SkillInstallState.Missing)
            };
            _skillPort.FullTargets = new List<SkillSetupTargetInfo>
            {
                CreateTarget("Claude Code", ".claude", SkillInstallState.Installed),
                CreateTarget("Codex CLI", ".codex", SkillInstallState.Installed)
            };
            SetupWizardSkillsWorkflowController controller = CreateSkillsWorkflow();

            controller.RefreshSkillsSection();

            Assert.That(_skillPort.FullScanCount, Is.EqualTo(1));
            Assert.That(_backgroundWorkRunner.RunCount, Is.EqualTo(1));
            Assert.That(_resizeCount, Is.EqualTo(2));
            Assert.That(_root.Q<VisualElement>("skill-target-status-list").childCount, Is.EqualTo(2));
        }

        /// <summary>
        /// Verifies a full scan cancelled while running leaves the fast scan on screen.
        /// </summary>
        [Test]
        public void SkillsRefreshSection_WhenCancelledDuringTheFullScan_KeepsTheFastScan()
        {
            _cliDetector.CliVersion = "3.1.0";
            _skillPort.FastTargets = new List<SkillSetupTargetInfo>
            {
                CreateTarget("Claude Code", ".claude", SkillInstallState.Missing)
            };
            _skillPort.FullTargets = new List<SkillSetupTargetInfo>
            {
                CreateTarget("Claude Code", ".claude", SkillInstallState.Installed),
                CreateTarget("Codex CLI", ".codex", SkillInstallState.Installed)
            };
            SetupWizardSkillsWorkflowController controller = CreateSkillsWorkflow();
            _skillPort.OnFullScan = () => controller.CancelSkillInstallStateRefresh();

            controller.RefreshSkillsSection();

            Assert.That(_skillPort.FullScanCount, Is.EqualTo(1));
            Assert.That(_resizeCount, Is.EqualTo(1));
            Assert.That(_root.Q<VisualElement>("skill-target-status-list").childCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies a bulk install installs every target with a skills directory with the caller's token, disables the layout toggle while
        /// installing, shows the installed dialog, and refreshes the section afterwards.
        /// </summary>
        [Test]
        public async Task SkillsHandleInstallSkillsAsync_BulkInstall_InstallsEveryInstallableTarget()
        {
            _cliDetector.CliVersion = "3.1.0";
            _skillPort.FullTargets = new List<SkillSetupTargetInfo>
            {
                CreateTarget("Claude Code", ".claude", SkillInstallState.Missing),
                CreateTarget("Codex CLI", ".codex", SkillInstallState.Installed),
                CreateTarget("Agents", ".agents", SkillInstallState.Missing, hasSkillsDirectory: false)
            };
            SetupWizardSkillsWorkflowController controller = CreateSkillsWorkflow();
            controller.InitializeGroupSkillsToggle();
            bool toggleEnabledDuringInstall = true;
            _skillPort.OnInstall = () =>
                toggleEnabledDuringInstall = _root.Q<Toggle>("group-skills-toggle").enabledSelf;

            using CancellationTokenSource cancellation = new CancellationTokenSource();

            await controller.HandleInstallSkillsAsync(isBulkInstall: true, cancellation.Token);

            Assert.That(_skillPort.InstalledTargetDirs, Is.EqualTo(new List<string> { ".claude", ".codex" }));
            Assert.That(_skillPort.InstallTokens, Is.EqualTo(new[] { cancellation.Token }));
            Assert.That(_skillPort.InstallGroupFlags, Is.EqualTo(new List<bool> { false }));
            Assert.That(toggleEnabledDuringInstall, Is.False);
            Assert.That(_dialogs.SkillsInstalledCount, Is.EqualTo(1));
            Assert.That(_skillPort.FastScanGroupFlags.Count, Is.EqualTo(1));
            Assert.That(_root.Q<Toggle>("group-skills-toggle").enabledSelf, Is.True);
        }

        /// <summary>
        /// Verifies a bulk install with an outdated target installs without the installed dialog.
        /// </summary>
        [Test]
        public async Task SkillsHandleInstallSkillsAsync_WhenATargetIsOutdated_SkipsTheInstalledDialog()
        {
            _cliDetector.CliVersion = "3.1.0";
            _skillPort.FullTargets = new List<SkillSetupTargetInfo>
            {
                CreateTarget("Claude Code", ".claude", SkillInstallState.Outdated)
            };
            SetupWizardSkillsWorkflowController controller = CreateSkillsWorkflow();

            await controller.HandleInstallSkillsAsync(isBulkInstall: true, CancellationToken.None);

            Assert.That(_skillPort.InstalledTargetDirs, Is.EqualTo(new List<string> { ".claude" }));
            Assert.That(_dialogs.SkillsInstalledCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies a single-target install installs only the selected target.
        /// </summary>
        [Test]
        public async Task SkillsHandleInstallSkillsAsync_SingleInstall_InstallsOnlyTheSelectedTarget()
        {
            _cliDetector.CliVersion = "3.1.0";
            _skillPort.FullTargets = new List<SkillSetupTargetInfo>
            {
                CreateTarget("Claude Code", ".claude", SkillInstallState.Missing),
                CreateTarget("Codex CLI", ".codex", SkillInstallState.Missing)
            };
            SetupWizardSkillsWorkflowController controller = CreateSkillsWorkflow();

            await controller.HandleInstallSkillsAsync(isBulkInstall: false, CancellationToken.None);

            Assert.That(_skillPort.InstalledTargetDirs, Is.EqualTo(new List<string> { ".claude" }));
        }

        /// <summary>
        /// Verifies a single-target install of an already installed target installs nothing and shows no dialog.
        /// </summary>
        [Test]
        public async Task SkillsHandleInstallSkillsAsync_SingleInstall_WhenTheSelectedTargetIsInstalled_InstallsNothing()
        {
            _cliDetector.CliVersion = "3.1.0";
            _skillPort.FullTargets = new List<SkillSetupTargetInfo>
            {
                CreateTarget("Claude Code", ".claude", SkillInstallState.Installed)
            };
            SetupWizardSkillsWorkflowController controller = CreateSkillsWorkflow();

            await controller.HandleInstallSkillsAsync(isBulkInstall: false, CancellationToken.None);

            Assert.That(_skillPort.InstalledTargetDirs, Is.Empty);
            Assert.That(_dialogs.SkillsInstalledCount, Is.EqualTo(0));
            Assert.That(_skillPort.FastScanGroupFlags.Count, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies an install cancelled while detecting targets installs nothing but still refreshes the section.
        /// </summary>
        [Test]
        public async Task SkillsHandleInstallSkillsAsync_WhenCancelledDuringDetection_InstallsNothing()
        {
            _cliDetector.CliVersion = "3.1.0";
            _skillPort.FullTargets = new List<SkillSetupTargetInfo>
            {
                CreateTarget("Claude Code", ".claude", SkillInstallState.Missing)
            };
            SetupWizardSkillsWorkflowController controller = CreateSkillsWorkflow();
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            _skillPort.OnFullScan = () => cancellation.Cancel();

            await controller.HandleInstallSkillsAsync(isBulkInstall: true, cancellation.Token);

            Assert.That(_skillPort.InstalledTargetDirs, Is.Empty);
            Assert.That(_skillPort.FastScanGroupFlags.Count, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies a second install request during an install returns without starting another install.
        /// </summary>
        [Test]
        public async Task SkillsHandleInstallSkillsAsync_WhileInstalling_DoesNotStartASecondInstall()
        {
            _cliDetector.CliVersion = "3.1.0";
            _skillPort.FullTargets = new List<SkillSetupTargetInfo>
            {
                CreateTarget("Claude Code", ".claude", SkillInstallState.Missing)
            };
            SetupWizardSkillsWorkflowController controller = CreateSkillsWorkflow();
            Task nestedInstall = null;
            bool nestedRequested = false;
            // Re-enters only once so a missing latch fails the test instead of recursing without end.
            _skillPort.OnInstall = () =>
            {
                if (nestedRequested)
                {
                    return;
                }

                nestedRequested = true;
                nestedInstall = controller.HandleInstallSkillsAsync(isBulkInstall: true, CancellationToken.None);
            };

            await controller.HandleInstallSkillsAsync(isBulkInstall: true, CancellationToken.None);

            Assert.That(nestedInstall.IsCompleted, Is.True);
            Assert.That(_skillPort.InstalledTargetDirs, Is.EqualTo(new List<string> { ".claude" }));
        }

        /// <summary>
        /// Verifies changing the target resizes the step and makes later single installs use the new target.
        /// </summary>
        [Test]
        public async Task SkillsHandleTargetChanged_SelectsTheTargetForSingleInstalls()
        {
            _cliDetector.CliVersion = "3.1.0";
            _skillPort.FullTargets = new List<SkillSetupTargetInfo>
            {
                CreateTarget("Claude Code", ".claude", SkillInstallState.Missing),
                CreateTarget("Codex CLI", ".codex", SkillInstallState.Missing)
            };
            SetupWizardSkillsWorkflowController controller = CreateSkillsWorkflow();

            controller.HandleTargetChanged(SkillsTarget.Codex);
            await controller.HandleInstallSkillsAsync(isBulkInstall: false, CancellationToken.None);

            Assert.That(_resizeCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(_skillPort.InstalledTargetDirs, Is.EqualTo(new List<string> { ".codex" }));
        }

        /// <summary>
        /// Verifies a layout change persists the flat layout and refreshes the skills section.
        /// </summary>
        [Test]
        public void SkillsHandleGroupSkillsChanged_PersistsTheFlatLayoutAndRefreshes()
        {
            SetupWizardSkillsWorkflowController controller = CreateSkillsWorkflow();

            controller.HandleGroupSkillsChanged(true);

            Assert.That(_editorSettingsPort.InstallSkillsFlatValues, Is.EqualTo(new List<bool> { true }));
            Assert.That(_skillPort.FastScanGroupFlags, Is.EqualTo(new List<bool> { false }));
            Assert.That(_resizeCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies the wizard opens with both steps checking and the auto-show toggle reflecting the stored
        /// personal suppression flag.
        /// </summary>
        [Test]
        public void WorkflowApplyInitialCheckingState_ShowsCheckingAndTheStoredSuppressFlag()
        {
            _editorSettingsPort.SuppressAutoShow = true;
            Toggle suppressToggle = new Toggle();
            VisualElement nodejsWarning = new VisualElement();
            VisualElement nodejsOk = new VisualElement();
            SetupWizardWorkflowController controller = CreateWorkflow(suppressToggle, nodejsWarning, nodejsOk);

            controller.ApplyInitialCheckingState();

            Assert.That(suppressToggle.value, Is.True);
            Assert.That(nodejsWarning.style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(nodejsOk.style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(_root.Q<Label>("cli-status-label").text, Is.EqualTo("Checking..."));
            Assert.That(_root.Q<Label>("skill-target-status-summary").text, Is.EqualTo("Checking installed skills..."));
        }

        /// <summary>
        /// Verifies the wizard-level initialization persists the flat layout through the skills step.
        /// </summary>
        [Test]
        public void WorkflowInitializeGroupSkillsToggle_DelegatesToTheSkillsStep()
        {
            SetupWizardWorkflowController controller = CreateWorkflow(new Toggle(), new VisualElement(), new VisualElement());

            controller.InitializeGroupSkillsToggle();

            Assert.That(_editorSettingsPort.InstallSkillsFlatValues, Is.EqualTo(new List<bool> { true }));
        }

        private SetupWizardCliWorkflowController CreateCliWorkflow()
        {
            return new SetupWizardCliWorkflowController(
                _root.Q<VisualElement>("cli-status-icon"),
                _root.Q<Label>("cli-status-label"),
                _root.Q<Label>("cli-homebrew-upgrade-message"),
                _root.Q<Button>("install-cli-button"),
                _root.Q<VisualElement>("cli-install-progress"),
                _root.Q<Label>("cli-install-progress-label"),
                _cliSetupApplicationService,
                refreshSkills => _refreshUiCalls.Add(refreshSkills));
        }

        private SetupWizardSkillsWorkflowController CreateSkillsWorkflow()
        {
            return new SetupWizardSkillsWorkflowController(
                CreateSkillsPanelView(),
                new SkillSetupUseCase(_skillPort),
                _editorSettingsPort,
                _cliSetupApplicationService,
                () => _resizeCount++,
                _dialogs,
                _backgroundWorkRunner);
        }

        private SetupWizardWorkflowController CreateWorkflow(
            Toggle suppressToggle,
            VisualElement nodejsWarning,
            VisualElement nodejsOk)
        {
            return new SetupWizardWorkflowController(
                _root,
                nodejsWarning,
                nodejsOk,
                _root.Q<VisualElement>("cli-status-icon"),
                _root.Q<Label>("cli-status-label"),
                _root.Q<Label>("cli-homebrew-upgrade-message"),
                _root.Q<Button>("install-cli-button"),
                _root.Q<VisualElement>("cli-install-progress"),
                _root.Q<Label>("cli-install-progress-label"),
                CreateSkillsPanelView(),
                suppressToggle,
                new SkillSetupUseCase(_skillPort),
                _editorSettingsPort,
                _cliSetupApplicationService,
                () => _resizeCount++);
        }

        private SkillsSetupPanelView CreateSkillsPanelView()
        {
            return new SkillsSetupPanelView(
                _root.Q<VisualElement>("skills-setup-panel"),
                _root.Q<Button>("refresh-skills-state-button"));
        }

        private static SkillSetupTargetInfo CreateTarget(
            string displayName,
            string dirName,
            SkillInstallState installState,
            bool hasSkillsDirectory = true)
        {
            return new SkillSetupTargetInfo(
                displayName,
                dirName,
                "--flag",
                hasSkillsDirectory,
                hasExistingSkills: installState != SkillInstallState.Missing,
                hasDifferentLayoutSkills: false,
                installState);
        }

        private static VisualElement CreateRootElement()
        {
            VisualElement root = new VisualElement();
            root.Add(new VisualElement { name = "cli-status-icon" });
            root.Add(new Label { name = "cli-status-label" });
            root.Add(new Label { name = "cli-homebrew-upgrade-message" });
            root.Add(new Button { name = "install-cli-button" });
            VisualElement installProgress = new VisualElement { name = "cli-install-progress" };
            installProgress.Add(new Label { name = "cli-install-progress-label" });
            root.Add(installProgress);

            root.Add(new Button { name = "refresh-skills-state-button" });
            VisualElement skillsSetupPanel = new VisualElement { name = "skills-setup-panel" };
            skillsSetupPanel.Add(new VisualElement { name = "skill-target-status-list" });
            skillsSetupPanel.Add(new VisualElement { name = "skill-target-status-divider" });
            skillsSetupPanel.Add(new Label { name = "skill-target-status-summary" });
            skillsSetupPanel.Add(new Label { name = "skills-no-targets-message" });
            skillsSetupPanel.Add(new Button { name = "install-all-skills-button" });
            Foldout specificTargetFoldout = new Foldout { name = "install-specific-target-foldout" };
            VisualElement groupSkillsRow = new VisualElement { name = "group-skills-row" };
            groupSkillsRow.Add(new Toggle { name = "group-skills-toggle" });
            specificTargetFoldout.Add(groupSkillsRow);
            specificTargetFoldout.Add(new EnumField { name = "skills-target-field" });
            specificTargetFoldout.Add(new Button { name = "install-selected-skills-button" });
            skillsSetupPanel.Add(specificTargetFoldout);
            root.Add(skillsSetupPanel);
            return root;
        }

        private sealed class RecordingEditorSettingsPort : IUnityCliLoopEditorSettingsPort
        {
            internal bool SuppressAutoShow { get; set; }
            internal List<bool> InstallSkillsFlatValues { get; } = new List<bool>();

            public bool GetSuppressSetupWizardAutoShow()
            {
                return SuppressAutoShow;
            }

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
            public void SetSuppressSetupWizardAutoShow(bool suppressAutoShow) => throw new NotSupportedException();
            public void SetShowToolSettings(bool showToolSettings) => throw new NotSupportedException();
        }

        private sealed class StubCliInstallationDetector : ICliInstallationDetector
        {
            internal string CliVersion { get; set; } = string.Empty;
            internal bool IsDispatcher { get; set; }
            internal bool IsVisibleFromShell { get; set; } = true;
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
                return string.Empty;
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

            public bool IsCliInstalled() => throw new NotSupportedException();
            public bool IsCheckCompleted() => throw new NotSupportedException();
            public Task RefreshCliVersionAsync(CancellationToken ct) => throw new NotSupportedException();
            public void InvalidateCache() => throw new NotSupportedException();
        }

        private sealed class StubNativeCliInstaller : INativeCliInstaller
        {
            internal bool HasPackageOwnedInstall { get; set; }

            public bool HasPackageOwnedCurrentUserInstall(RuntimePlatform platform)
            {
                return HasPackageOwnedInstall;
            }

            public ManagedCliKind ResolveManagedCliKind(string cliExecutablePath)
            {
                return ManagedCliKind.None;
            }

            public bool IsPackageOwnedCurrentUserInstallPath(string cliExecutablePath, RuntimePlatform platform) =>
                throw new NotSupportedException();

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

        private sealed class StubCliPinReader : ICliPinReader
        {
            public string LoadMinimumDispatcherVersionOrThrow()
            {
                return MinimumDispatcherVersion;
            }

            public DispatcherBootstrapPinLoadResult LoadDispatcherBootstrapPin()
            {
                return DispatcherBootstrapPinLoadResult.FromFailure("no bootstrap pin in this test");
            }

            public CliPinLoadResult LoadPackagePin() => throw new NotSupportedException();
        }

        private sealed class RecordingSkillSetupPort : ISkillSetupPort
        {
            internal List<SkillSetupTargetInfo> FastTargets { get; set; } = new List<SkillSetupTargetInfo>();
            internal List<string> FastScanProjectRoots { get; } = new List<string>();
            internal List<bool> FastScanGroupFlags { get; } = new List<bool>();
            internal int FullScanCount { get; private set; }
            internal List<SkillSetupTargetInfo> FullTargets { get; set; } = new List<SkillSetupTargetInfo>();
            internal Action OnFullScan { get; set; }
            internal List<string> InstalledTargetDirs { get; } = new List<string>();
            internal List<bool> InstallGroupFlags { get; } = new List<bool>();
            internal Action OnInstall { get; set; }
            internal List<CancellationToken> InstallTokens { get; } = new List<CancellationToken>();

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
                OnFullScan?.Invoke();
                return FullTargets;
            }

            public void RemoveSkillFiles(string toolName) => throw new NotSupportedException();
            public bool IsSkillInstalled(string toolName) => throw new NotSupportedException();

            public Task InstallSkillFilesAsync(
                List<SkillSetupTargetInfo> targets,
                bool groupSkillsUnderUnityCliLoop,
                CancellationToken ct)
            {
                foreach (SkillSetupTargetInfo target in targets)
                {
                    InstalledTargetDirs.Add(target.DirName);
                }

                InstallGroupFlags.Add(groupSkillsUnderUnityCliLoop);
                InstallTokens.Add(ct);
                OnInstall?.Invoke();
                return Task.CompletedTask;
            }

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
    }
}
