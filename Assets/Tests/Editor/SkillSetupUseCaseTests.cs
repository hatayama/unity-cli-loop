using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies the skill setup use case delegates each workflow step to the skill setup port.
    /// </summary>
    public sealed class SkillSetupUseCaseTests
    {
        private const string ProjectRoot = "<PROJECT_ROOT>";

        /// <summary>
        /// Verifies removing skill files asks the port to remove the files of the named tool.
        /// </summary>
        [Test]
        public void RemoveSkillFiles_WhenCalled_RemovesFilesForNamedTool()
        {
            RecordingSkillSetupPort port = new RecordingSkillSetupPort();
            SkillSetupUseCase useCase = new SkillSetupUseCase(port);

            useCase.RemoveSkillFiles("sample-tool");

            Assert.That(port.Calls, Is.EqualTo(new[] { "RemoveSkillFiles|sample-tool" }));
        }

        /// <summary>
        /// Verifies the installed check returns the port answer for the named tool.
        /// </summary>
        [Test]
        public void IsSkillInstalled_WhenPortReportsInstalled_ReturnsTrueForNamedTool()
        {
            RecordingSkillSetupPort port = new RecordingSkillSetupPort();
            port.InstalledToolName = "sample-tool";
            SkillSetupUseCase useCase = new SkillSetupUseCase(port);

            Assert.That(useCase.IsSkillInstalled("sample-tool"), Is.True);
            Assert.That(useCase.IsSkillInstalled("other-tool"), Is.False);
        }

        /// <summary>
        /// Verifies both target detection modes return the port's target list for the given root and layout.
        /// </summary>
        [Test]
        public void DetectSkillTargets_WhenCalled_ReturnPortTargetsForRootAndLayout()
        {
            RecordingSkillSetupPort port = new RecordingSkillSetupPort();
            SkillSetupUseCase useCase = new SkillSetupUseCase(port);

            List<SkillSetupTargetInfo> fullTargets = useCase.DetectSkillTargetsForLayoutAtProjectRoot(ProjectRoot, true);
            List<SkillSetupTargetInfo> fastTargets = useCase.DetectSkillTargetsForLayoutFastAtProjectRoot(ProjectRoot, false);

            Assert.That(fullTargets, Is.SameAs(port.FullDetectionTargets));
            Assert.That(fastTargets, Is.SameAs(port.FastDetectionTargets));
            Assert.That(port.Calls, Is.EqualTo(new[]
            {
                "DetectFull|<PROJECT_ROOT>|True",
                "DetectFast|<PROJECT_ROOT>|False"
            }));
        }

        /// <summary>
        /// Verifies skill installation returns the port tasks and forwards targets, layout, tool name, and token.
        /// </summary>
        [Test]
        public void InstallSkillFiles_WhenCalled_ReturnPortTasksWithForwardedArguments()
        {
            RecordingSkillSetupPort port = new RecordingSkillSetupPort();
            SkillSetupUseCase useCase = new SkillSetupUseCase(port);
            List<SkillSetupTargetInfo> targets = CreateTargets();
            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

            Task installTask = useCase.InstallSkillFilesAsync(targets, true, cancellationTokenSource.Token);
            Task installForToolTask = useCase.InstallSkillFilesForToolAsync("sample-tool", false, cancellationTokenSource.Token);

            Assert.That(installTask, Is.SameAs(port.InstallTask));
            Assert.That(installForToolTask, Is.SameAs(port.InstallForToolTask));
            Assert.That(port.LastTargets, Is.SameAs(targets));
            Assert.That(port.Tokens, Is.EqualTo(new[] { cancellationTokenSource.Token, cancellationTokenSource.Token }));
            Assert.That(port.Calls, Is.EqualTo(new[]
            {
                "Install|True",
                "InstallForTool|sample-tool|False"
            }));
            cancellationTokenSource.Dispose();
        }

        /// <summary>
        /// Verifies the V3 migration skill state is the port state for the given root, target, and layout.
        /// </summary>
        [Test]
        public void GetV3MigrationSkillInstallStateAtProjectRoot_WhenPortReportsOutdated_ReturnsOutdated()
        {
            RecordingSkillSetupPort port = new RecordingSkillSetupPort();
            port.V3MigrationState = SkillInstallState.Outdated;
            SkillSetupUseCase useCase = new SkillSetupUseCase(port);
            SkillSetupTargetInfo target = CreateTargets()[0];

            SkillInstallState state = useCase.GetV3MigrationSkillInstallStateAtProjectRoot(ProjectRoot, target, true);

            Assert.That(state, Is.EqualTo(SkillInstallState.Outdated));
            Assert.That(port.Calls, Is.EqualTo(new[] { "V3State|<PROJECT_ROOT>|.agent-a|True" }));
        }

        /// <summary>
        /// Verifies V3 migration skill install and removal return the port tasks with the given root, targets, and layout.
        /// </summary>
        [Test]
        public void V3MigrationSkillFiles_WhenInstalledAndRemoved_ReturnPortTasksWithForwardedArguments()
        {
            RecordingSkillSetupPort port = new RecordingSkillSetupPort();
            SkillSetupUseCase useCase = new SkillSetupUseCase(port);
            List<SkillSetupTargetInfo> installTargets = CreateTargets();
            List<SkillSetupTargetInfo> removeTargets = CreateTargets();

            Task installTask = useCase.InstallV3MigrationSkillFilesAsync(ProjectRoot, installTargets, true, CancellationToken.None);
            Task removeTask = useCase.RemoveV3MigrationSkillFilesAsync(ProjectRoot, removeTargets, false, CancellationToken.None);

            Assert.That(installTask, Is.SameAs(port.InstallV3Task));
            Assert.That(removeTask, Is.SameAs(port.RemoveV3Task));
            Assert.That(port.InstallV3Targets, Is.SameAs(installTargets));
            Assert.That(port.RemoveV3Targets, Is.SameAs(removeTargets));
            Assert.That(port.Calls, Is.EqualTo(new[]
            {
                "InstallV3|<PROJECT_ROOT>|True",
                "RemoveV3|<PROJECT_ROOT>|False"
            }));
        }

        private static List<SkillSetupTargetInfo> CreateTargets()
        {
            return new List<SkillSetupTargetInfo>
            {
                new SkillSetupTargetInfo("Agent A", ".agent-a", "--agent-a", true, false)
            };
        }

        /// <summary>
        /// Test support type that records skill setup port calls and returns distinct sentinel values.
        /// </summary>
        private sealed class RecordingSkillSetupPort : ISkillSetupPort
        {
            public List<string> Calls { get; } = new List<string>();
            public List<CancellationToken> Tokens { get; } = new List<CancellationToken>();
            public List<SkillSetupTargetInfo> LastTargets { get; private set; }

            public List<SkillSetupTargetInfo> InstallV3Targets { get; private set; }

            public List<SkillSetupTargetInfo> RemoveV3Targets { get; private set; }
            public string InstalledToolName { get; set; }
            public SkillInstallState V3MigrationState { get; set; }
            public List<SkillSetupTargetInfo> FullDetectionTargets { get; } = new List<SkillSetupTargetInfo>();
            public List<SkillSetupTargetInfo> FastDetectionTargets { get; } = new List<SkillSetupTargetInfo>();
            public Task InstallTask { get; } = Task.FromResult(1);
            public Task InstallForToolTask { get; } = Task.FromResult(2);
            public Task InstallV3Task { get; } = Task.FromResult(3);
            public Task RemoveV3Task { get; } = Task.FromResult(4);

            public void RemoveSkillFiles(string toolName)
            {
                Calls.Add("RemoveSkillFiles|" + toolName);
            }

            public bool IsSkillInstalled(string toolName)
            {
                return toolName == InstalledToolName;
            }

            public List<SkillSetupTargetInfo> DetectSkillTargetsForLayoutAtProjectRoot(
                string projectRoot,
                bool groupSkillsUnderUnityCliLoop)
            {
                Calls.Add("DetectFull|" + projectRoot + "|" + groupSkillsUnderUnityCliLoop);
                return FullDetectionTargets;
            }

            public List<SkillSetupTargetInfo> DetectSkillTargetsForLayoutFastAtProjectRoot(
                string projectRoot,
                bool groupSkillsUnderUnityCliLoop)
            {
                Calls.Add("DetectFast|" + projectRoot + "|" + groupSkillsUnderUnityCliLoop);
                return FastDetectionTargets;
            }

            public Task InstallSkillFilesAsync(
                List<SkillSetupTargetInfo> targets,
                bool groupSkillsUnderUnityCliLoop,
                CancellationToken ct)
            {
                Calls.Add("Install|" + groupSkillsUnderUnityCliLoop);
                LastTargets = targets;
                Tokens.Add(ct);
                return InstallTask;
            }

            public Task InstallSkillFilesForToolAsync(
                string toolName,
                bool groupSkillsUnderUnityCliLoop,
                CancellationToken ct)
            {
                Calls.Add("InstallForTool|" + toolName + "|" + groupSkillsUnderUnityCliLoop);
                Tokens.Add(ct);
                return InstallForToolTask;
            }

            public SkillInstallState GetV3MigrationSkillInstallStateAtProjectRoot(
                string projectRoot,
                SkillSetupTargetInfo target,
                bool groupSkillsUnderUnityCliLoop)
            {
                Calls.Add("V3State|" + projectRoot + "|" + target.DirName + "|" + groupSkillsUnderUnityCliLoop);
                return V3MigrationState;
            }

            public Task InstallV3MigrationSkillFilesAsync(
                string projectRoot,
                List<SkillSetupTargetInfo> targets,
                bool groupSkillsUnderUnityCliLoop,
                CancellationToken ct)
            {
                Calls.Add("InstallV3|" + projectRoot + "|" + groupSkillsUnderUnityCliLoop);
                InstallV3Targets = targets;
                return InstallV3Task;
            }

            public Task RemoveV3MigrationSkillFilesAsync(
                string projectRoot,
                List<SkillSetupTargetInfo> targets,
                bool groupSkillsUnderUnityCliLoop,
                CancellationToken ct)
            {
                Calls.Add("RemoveV3|" + projectRoot + "|" + groupSkillsUnderUnityCliLoop);
                RemoveV3Targets = targets;
                return RemoveV3Task;
            }
        }
    }
}
