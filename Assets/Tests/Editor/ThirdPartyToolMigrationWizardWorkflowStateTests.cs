using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Presentation;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the migration wizard workflow controller's synchronous state transitions, the
    /// migration skill install/remove toggle, and the superseding of in-flight operation tokens.
    /// </summary>
    public sealed class ThirdPartyToolMigrationWizardWorkflowStateTests
    {
        private VisualElement _root;
        private RecordingMigrationSkillPort _skillPort;
        private int _resizeCount;
        private ThirdPartyToolMigrationWizardWorkflowController _controller;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            _skillPort = new RecordingMigrationSkillPort();
            _resizeCount = 0;
            ThirdPartyToolMigrationWizardView view = ThirdPartyToolMigrationWizardView.Create(
                _root,
                () => { },
                () => { },
                _ => { },
                () => { },
                () => { });
            _controller = new ThirdPartyToolMigrationWizardWorkflowController(
                view,
                new SkillSetupUseCase(_skillPort),
                new ThirdPartyToolMigrationUseCase(new UnusedMigrationPort()),
                new List<string> { "Assets/Seed.cs" },
                () => _resizeCount++);
        }

        [TearDown]
        public void TearDown()
        {
            _controller.CancelMigrationOperation();
            _controller.CancelMigrationSkillOperation();
        }

        /// <summary>
        /// Verifies a plain window open shows the not-checked status instead of the seed list.
        /// </summary>
        [Test]
        public void ShowInitialState_WithoutAutoScan_ShowsTheNotCheckedStatus()
        {
            _controller.ShowInitialState(shouldShowAutoScanDetectedState: false);

            Assert.That(GetStatusText(), Is.EqualTo(ThirdPartyToolMigrationWizardText.MigrationNotCheckedText));
            Assert.That(_resizeCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies an auto-scan window open renders the seed files captured at construction.
        /// </summary>
        [Test]
        public void ShowInitialState_WithAutoScan_ShowsTheConstructorSeedCount()
        {
            _controller.ShowInitialState(shouldShowAutoScanDetectedState: true);

            Assert.That(
                GetStatusText(),
                Is.EqualTo(ThirdPartyToolMigrationWizardText.GetAutoScanDetectedStatusText(1)));
            Assert.That(_resizeCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies a re-detection that should not show the detected state keeps the window as it was.
        /// </summary>
        [Test]
        public void TryShowAutoScanDetectedState_WhenNotRequested_LeavesTheCurrentStatus()
        {
            _controller.ShowNoMigrationTargetsState();

            _controller.TryShowAutoScanDetectedState(
                shouldShowAutoScanDetectedState: false,
                new List<string> { "Assets/A.cs", "Assets/B.cs" });

            Assert.That(GetStatusText(), Is.EqualTo(ThirdPartyToolMigrationWizardText.NoMigrationTargetsText));
            Assert.That(_resizeCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies a scan result renders its file count and schedules a resize.
        /// </summary>
        [Test]
        public void ShowMigrationTargetsState_ShowsTheScannedFileCount()
        {
            _controller.ShowMigrationTargetsState(new[] { "Assets/A.cs", "Assets/B.cs", "Assets/C.cs" });

            Assert.That(GetStatusText(), Is.EqualTo(ThirdPartyToolMigrationWizardText.GetMigrationStatusText(3)));
            Assert.That(_resizeCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies a finished migration shows the completion wording and schedules a resize.
        /// </summary>
        [Test]
        public void ShowMigrationCompleteState_ShowsTheCompletionStatus()
        {
            _controller.ShowMigrationCompleteState();

            Assert.That(GetStatusText(), Is.EqualTo(ThirdPartyToolMigrationWizardText.MigrationCompleteText));
            Assert.That(_resizeCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies progress updates render the scanning text but do not reflow the window.
        /// </summary>
        [Test]
        public void ShowCheckingState_ShowsTheScanProgressWithoutResizing()
        {
            ThirdPartyToolMigrationProgress progress = new ThirdPartyToolMigrationProgress(2, 5);

            _controller.ShowCheckingState(progress);

            Assert.That(
                GetStatusText(),
                Is.EqualTo(ThirdPartyToolMigrationWizardText.GetMigrationProgressText(progress, false)));
            Assert.That(_resizeCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies switching the skill target re-reads that target's install state, flat, at the project root,
        /// and renders the remove action for an installed skill.
        /// </summary>
        [Test]
        public void HandleMigrationSkillTargetChanged_QueriesTheNewTargetAtTheProjectRoot()
        {
            _skillPort.InstallState = SkillInstallState.Installed;

            _controller.HandleMigrationSkillTargetChanged(SkillsTarget.Codex);

            Assert.That(_skillPort.StateQueries.Count, Is.EqualTo(1));
            Assert.That(_skillPort.StateQueries[0].ProjectRoot, Is.EqualTo(UnityCliLoopPathResolver.GetProjectRoot()));
            Assert.That(_skillPort.StateQueries[0].Target.DirName, Is.EqualTo(".codex"));
            Assert.That(_skillPort.StateQueries[0].Target.InstallFlag, Is.EqualTo("--codex"));
            Assert.That(_skillPort.StateQueries[0].GroupSkillsUnderUnityCliLoop, Is.False);
            Assert.That(
                GetMigrationSkillButtonText(),
                Is.EqualTo(ThirdPartyToolMigrationWizardText.GetMigrationSkillButtonText(false, SkillInstallState.Installed)));
        }

        [TestCase(SkillsTarget.Claude, "Claude Code", ".claude", "--claude")]
        [TestCase(SkillsTarget.Codex, "Codex CLI", ".codex", "--codex")]
        [TestCase(SkillsTarget.Agents, "Common", ".agents", "--agents")]
        public void CreateMigrationSkillTargetInfo_DescribesAMissingFlatTarget(
            SkillsTarget target,
            string expectedDisplayName,
            string expectedDirectoryName,
            string expectedInstallFlag)
        {
            // Verifies the migration skill target names the selected agent directory and starts as missing.
            SkillSetupTargetInfo info =
                ThirdPartyToolMigrationWizardWorkflowController.CreateMigrationSkillTargetInfo(target);

            Assert.That(info.DisplayName, Is.EqualTo(expectedDisplayName));
            Assert.That(info.DirName, Is.EqualTo(expectedDirectoryName));
            Assert.That(info.InstallFlag, Is.EqualTo(expectedInstallFlag));
            Assert.That(info.HasSkillsDirectory, Is.True);
            Assert.That(info.InstallState, Is.EqualTo(SkillInstallState.Missing));
        }

        /// <summary>
        /// Verifies the toggle installs a missing migration skill for the selected target and then re-reads the
        /// install state.
        /// </summary>
        [Test]
        public async Task HandleToggleMigrationSkill_WhenMissing_InstallsTheSelectedTarget()
        {
            _skillPort.InstallState = SkillInstallState.Missing;

            await _controller.HandleToggleMigrationSkill();

            Assert.That(_skillPort.InstallCalls.Count, Is.EqualTo(1));
            Assert.That(_skillPort.RemoveCalls.Count, Is.EqualTo(0));
            Assert.That(_skillPort.InstallCalls[0].ProjectRoot, Is.EqualTo(UnityCliLoopPathResolver.GetProjectRoot()));
            Assert.That(_skillPort.InstallCalls[0].Targets.Count, Is.EqualTo(1));
            Assert.That(_skillPort.InstallCalls[0].Targets[0].DirName, Is.EqualTo(".claude"));
            Assert.That(_skillPort.InstallCalls[0].GroupSkillsUnderUnityCliLoop, Is.False);
            Assert.That(_skillPort.StateQueries.Count, Is.EqualTo(2));
        }

        [TestCase(SkillInstallState.Installed)]
        [TestCase(SkillInstallState.Outdated)]
        public async Task HandleToggleMigrationSkill_WhenPresent_RemovesTheSelectedTarget(SkillInstallState installState)
        {
            // Verifies the toggle removes an installed or outdated migration skill instead of reinstalling it.
            _skillPort.InstallState = installState;
            _controller.HandleMigrationSkillTargetChanged(SkillsTarget.Agents);

            await _controller.HandleToggleMigrationSkill();

            Assert.That(_skillPort.RemoveCalls.Count, Is.EqualTo(1));
            Assert.That(_skillPort.InstallCalls.Count, Is.EqualTo(0));
            Assert.That(_skillPort.RemoveCalls[0].Targets[0].DirName, Is.EqualTo(".agents"));
            Assert.That(_skillPort.RemoveCalls[0].GroupSkillsUnderUnityCliLoop, Is.False);
        }

        /// <summary>
        /// Verifies an install failure is logged and the button leaves its updating state by re-reading the
        /// on-disk install state.
        /// </summary>
        [Test]
        public async Task HandleToggleMigrationSkill_WhenTheInstallFails_LogsAndShowsTheRefreshedState()
        {
            _skillPort.InstallState = SkillInstallState.Missing;
            _skillPort.InstallFailure = new InvalidOperationException("install failed for test");
            LogAssert.Expect(LogType.Exception, new Regex("install failed for test"));

            await _controller.HandleToggleMigrationSkill();

            Assert.That(_skillPort.StateQueries.Count, Is.EqualTo(2));
            Assert.That(
                GetMigrationSkillButtonText(),
                Is.EqualTo(ThirdPartyToolMigrationWizardText.GetMigrationSkillButtonText(false, SkillInstallState.Missing)));
        }

        /// <summary>
        /// Verifies a toggle whose operation was canceled while installing does not re-read or touch the install
        /// state afterwards.
        /// </summary>
        [Test]
        public async Task HandleToggleMigrationSkill_WhenCanceled_SkipsTheRefresh()
        {
            _skillPort.InstallState = SkillInstallState.Missing;
            _skillPort.OnInstall = () => _controller.CancelMigrationSkillOperation();

            await _controller.HandleToggleMigrationSkill();

            Assert.That(_skillPort.StateQueries.Count, Is.EqualTo(1));
            Assert.That(
                GetMigrationSkillButtonText(),
                Is.EqualTo(ThirdPartyToolMigrationWizardText.GetMigrationSkillButtonText(true, SkillInstallState.Missing)));
        }

        /// <summary>
        /// Verifies a toggle superseded by a newer skill operation while installing does not re-read the install
        /// state for the stale operation.
        /// </summary>
        [Test]
        public async Task HandleToggleMigrationSkill_WhenSupersededDuringInstall_LeavesTheRefreshToTheNewerToggle()
        {
            _skillPort.InstallState = SkillInstallState.Missing;
            _skillPort.OnInstall = () => _controller.BeginMigrationSkillOperation();

            await _controller.HandleToggleMigrationSkill();

            Assert.That(_skillPort.InstallCalls.Count, Is.EqualTo(1));
            Assert.That(_skillPort.StateQueries.Count, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies a new migration operation cancels the previous one, which then can no longer complete the
        /// newer operation.
        /// </summary>
        [Test]
        public void BeginMigrationOperation_CancelsAndDeactivatesThePreviousToken()
        {
            CancellationToken first = _controller.BeginMigrationOperation();
            CancellationToken second = _controller.BeginMigrationOperation();

            _controller.CompleteMigrationOperation(first);

            Assert.That(first.IsCancellationRequested, Is.True);
            Assert.That(_controller.IsMigrationOperationActive(first), Is.False);
            Assert.That(_controller.IsMigrationOperationActive(second), Is.True);
        }

        /// <summary>
        /// Verifies completing the active migration operation clears it without signaling cancellation.
        /// </summary>
        [Test]
        public void CompleteMigrationOperation_ForTheActiveToken_EndsItWithoutCanceling()
        {
            CancellationToken token = _controller.BeginMigrationOperation();

            _controller.CompleteMigrationOperation(token);

            Assert.That(_controller.IsMigrationOperationActive(token), Is.False);
            Assert.That(token.IsCancellationRequested, Is.False);
        }

        /// <summary>
        /// Verifies a new skill operation cancels the previous one, which then can no longer complete the newer
        /// operation.
        /// </summary>
        [Test]
        public void BeginMigrationSkillOperation_CancelsAndDeactivatesThePreviousToken()
        {
            CancellationToken first = _controller.BeginMigrationSkillOperation();
            CancellationToken second = _controller.BeginMigrationSkillOperation();

            _controller.CompleteMigrationSkillOperation(first);

            Assert.That(first.IsCancellationRequested, Is.True);
            Assert.That(_controller.IsMigrationSkillOperationActive(first), Is.False);
            Assert.That(_controller.IsMigrationSkillOperationActive(second), Is.True);
        }

        /// <summary>
        /// Verifies the controller's progress reporter renders progress for the active scan and drops it once that
        /// scan has ended, even though its token was never canceled.
        /// </summary>
        [Test]
        public void CreateProgressReporter_AppliesProgressOnlyWhileTheOperationIsActive()
        {
            CancellationToken stale = _controller.BeginMigrationOperation();
            IProgress<ThirdPartyToolMigrationProgress> staleReporter = _controller.CreateProgressReporter(stale);
            _controller.CompleteMigrationOperation(stale);
            CancellationToken active = _controller.BeginMigrationOperation();
            IProgress<ThirdPartyToolMigrationProgress> activeReporter = _controller.CreateProgressReporter(active);
            _controller.ShowNotCheckedState();

            staleReporter.Report(new ThirdPartyToolMigrationProgress(1, 4));
            string afterStaleReport = GetStatusText();
            activeReporter.Report(new ThirdPartyToolMigrationProgress(3, 4));

            Assert.That(afterStaleReport, Is.EqualTo(ThirdPartyToolMigrationWizardText.MigrationNotCheckedText));
            Assert.That(
                GetStatusText(),
                Is.EqualTo(ThirdPartyToolMigrationWizardText.GetMigrationProgressText(
                    new ThirdPartyToolMigrationProgress(3, 4),
                    false)));
        }

        private string GetStatusText()
        {
            return _root.Query<TextField>(className: "setup-step__status-label--standalone").First().value;
        }

        private string GetMigrationSkillButtonText()
        {
            // The skill button is the primary button in the same action section as the target dropdown.
            VisualElement skillActionSection = _root.Q<EnumField>().parent.parent;
            return skillActionSection.Q<Button>(className: "setup-button--primary").text;
        }

        private readonly struct StateQuery
        {
            internal readonly string ProjectRoot;
            internal readonly SkillSetupTargetInfo Target;
            internal readonly bool GroupSkillsUnderUnityCliLoop;

            internal StateQuery(string projectRoot, SkillSetupTargetInfo target, bool groupSkillsUnderUnityCliLoop)
            {
                ProjectRoot = projectRoot;
                Target = target;
                GroupSkillsUnderUnityCliLoop = groupSkillsUnderUnityCliLoop;
            }
        }

        private readonly struct MigrationSkillFilesCall
        {
            internal readonly string ProjectRoot;
            internal readonly List<SkillSetupTargetInfo> Targets;
            internal readonly bool GroupSkillsUnderUnityCliLoop;

            internal MigrationSkillFilesCall(
                string projectRoot,
                List<SkillSetupTargetInfo> targets,
                bool groupSkillsUnderUnityCliLoop)
            {
                ProjectRoot = projectRoot;
                Targets = targets;
                GroupSkillsUnderUnityCliLoop = groupSkillsUnderUnityCliLoop;
            }
        }

        private sealed class RecordingMigrationSkillPort : ISkillSetupPort
        {
            internal SkillInstallState InstallState { get; set; } = SkillInstallState.Missing;
            internal Exception InstallFailure { get; set; }
            internal Action OnInstall { get; set; }
            internal List<StateQuery> StateQueries { get; } = new List<StateQuery>();
            internal List<MigrationSkillFilesCall> InstallCalls { get; } = new List<MigrationSkillFilesCall>();
            internal List<MigrationSkillFilesCall> RemoveCalls { get; } = new List<MigrationSkillFilesCall>();

            public SkillInstallState GetV3MigrationSkillInstallStateAtProjectRoot(
                string projectRoot,
                SkillSetupTargetInfo target,
                bool groupSkillsUnderUnityCliLoop)
            {
                StateQueries.Add(new StateQuery(projectRoot, target, groupSkillsUnderUnityCliLoop));
                return InstallState;
            }

            public Task InstallV3MigrationSkillFilesAsync(
                string projectRoot,
                List<SkillSetupTargetInfo> targets,
                bool groupSkillsUnderUnityCliLoop,
                CancellationToken ct)
            {
                InstallCalls.Add(new MigrationSkillFilesCall(projectRoot, targets, groupSkillsUnderUnityCliLoop));
                OnInstall?.Invoke();
                if (ct.IsCancellationRequested)
                {
                    // Mirrors the production installer, whose only cancellation is ct.ThrowIfCancellationRequested.
                    return Task.FromCanceled(ct);
                }

                return InstallFailure == null ? Task.CompletedTask : Task.FromException(InstallFailure);
            }

            public Task RemoveV3MigrationSkillFilesAsync(
                string projectRoot,
                List<SkillSetupTargetInfo> targets,
                bool groupSkillsUnderUnityCliLoop,
                CancellationToken ct)
            {
                RemoveCalls.Add(new MigrationSkillFilesCall(projectRoot, targets, groupSkillsUnderUnityCliLoop));
                return Task.CompletedTask;
            }

            public void RemoveSkillFiles(string toolName)
            {
                throw new NotSupportedException();
            }

            public bool IsSkillInstalled(string toolName)
            {
                throw new NotSupportedException();
            }

            public List<SkillSetupTargetInfo> DetectSkillTargetsForLayoutAtProjectRoot(
                string projectRoot,
                bool groupSkillsUnderUnityCliLoop)
            {
                throw new NotSupportedException();
            }

            public List<SkillSetupTargetInfo> DetectSkillTargetsForLayoutFastAtProjectRoot(
                string projectRoot,
                bool groupSkillsUnderUnityCliLoop)
            {
                throw new NotSupportedException();
            }

            public Task InstallSkillFilesAsync(
                List<SkillSetupTargetInfo> targets,
                bool groupSkillsUnderUnityCliLoop,
                CancellationToken ct)
            {
                throw new NotSupportedException();
            }

            public Task InstallSkillFilesForToolAsync(
                string toolName,
                bool groupSkillsUnderUnityCliLoop,
                CancellationToken ct)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class UnusedMigrationPort : IThirdPartyToolMigrationPort
        {
            public ThirdPartyToolMigrationPreview PreviewMigration(string projectRoot)
            {
                throw new NotSupportedException();
            }

            public Task<ThirdPartyToolMigrationPreview> PreviewMigrationAsync(
                string projectRoot,
                IProgress<ThirdPartyToolMigrationProgress> progress,
                CancellationToken ct)
            {
                throw new NotSupportedException();
            }

            public (bool Found, List<string> TargetFilePaths) TryDetectAutoScanTargetsFromCompileErrors(
                string projectRoot)
            {
                throw new NotSupportedException();
            }

            public Task<bool> HasMigrationTargetsAsync(string projectRoot, CancellationToken ct)
            {
                throw new NotSupportedException();
            }

            public ThirdPartyToolMigrationResult ApplyMigration(string projectRoot)
            {
                throw new NotSupportedException();
            }

            public Task<ThirdPartyToolMigrationResult> ApplyMigrationAsync(
                string projectRoot,
                IProgress<ThirdPartyToolMigrationProgress> progress,
                CancellationToken ct)
            {
                throw new NotSupportedException();
            }
        }
    }
}
