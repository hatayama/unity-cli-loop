using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// The host against the real compiled worker in `--serve` mode: one dotnet process answers
    /// consecutive requests, an externally killed worker is replaced, Shutdown stops the process,
    /// and a request the worker rejects leaves the process alive.
    /// </summary>
    public class TransformWorkerHostProcessTests
    {
        private const int ExitWaitMilliseconds = 10_000;

        private TransformWorkerHost _host;

        [SetUp]
        public void SetUp()
        {
            _host = new TransformWorkerHost(
                TransformWorkerLaunchTargetResolution.ResolveAsync,
                TransformWorkerProcessChannel.Start,
                HotReloadConstants.WorkerProcessTimeoutMilliseconds);
        }

        [TearDown]
        public void TearDown()
        {
            _host.Shutdown("test teardown");
        }

        /// <summary>
        /// What: two transforms of the e2e fixture complete through the same worker process, with the
        /// same shim entries the one-shot path produces, and only one process is launched.
        /// </summary>
        [Test]
        public async Task RunAsync_TwiceOnE2EFixture_ReusesOneProcessAndCompletes()
        {
            TransformWorkerInputDto input = TransformWorkerClientTests.BuildE2EFixtureInput();

            TransformWorkerHostResult first = await _host.RunAsync(input, CancellationToken.None);
            int? firstPid = _host.CurrentProcessId;
            TransformWorkerHostResult second = await _host.RunAsync(input, CancellationToken.None);

            Assert.That(first.Kind, Is.EqualTo(TransformWorkerHostResultKind.Completed), first.ErrorMessage);
            Assert.That(first.Output.entries.Length, Is.GreaterThan(0));
            Assert.That(first.Output.files.Length, Is.EqualTo(input.sources.Length));
            Assert.That(second.Kind, Is.EqualTo(TransformWorkerHostResultKind.Completed), second.ErrorMessage);
            Assert.That(second.Output.entries.Length, Is.EqualTo(first.Output.entries.Length));
            Assert.That(_host.LaunchCount, Is.EqualTo(1));
            Assert.That(firstPid, Is.Not.Null);
            Assert.That(_host.CurrentProcessId, Is.EqualTo(firstPid));
        }

        /// <summary>
        /// What: when the worker process is killed from outside between requests, the next request
        /// starts a new process (new pid) and completes.
        /// </summary>
        [Test]
        public async Task RunAsync_AfterExternalKill_RelaunchesWithNewProcess()
        {
            TransformWorkerInputDto input = TransformWorkerClientTests.BuildE2EFixtureInput();
            TransformWorkerHostResult first = await _host.RunAsync(input, CancellationToken.None);
            Assert.That(first.Kind, Is.EqualTo(TransformWorkerHostResultKind.Completed), first.ErrorMessage);
            int firstPid = _host.CurrentProcessId.Value;

            using (Process worker = Process.GetProcessById(firstPid))
            {
                worker.Kill();
                Assert.That(worker.WaitForExit(ExitWaitMilliseconds), Is.True, "The killed worker must exit.");
            }

            TransformWorkerHostResult second = await _host.RunAsync(input, CancellationToken.None);

            Assert.That(second.Kind, Is.EqualTo(TransformWorkerHostResultKind.Completed), second.ErrorMessage);
            Assert.That(_host.LaunchCount, Is.EqualTo(2));
            Assert.That(_host.CurrentProcessId, Is.Not.Null.And.Not.EqualTo(firstPid));
        }

        /// <summary>
        /// What: Shutdown stops the resident process; it exits and the host reports no current process.
        /// </summary>
        [Test]
        public async Task Shutdown_StopsResidentProcess()
        {
            TransformWorkerInputDto input = TransformWorkerClientTests.BuildE2EFixtureInput();
            TransformWorkerHostResult first = await _host.RunAsync(input, CancellationToken.None);
            Assert.That(first.Kind, Is.EqualTo(TransformWorkerHostResultKind.Completed), first.ErrorMessage);
            int pid = _host.CurrentProcessId.Value;

            using (Process worker = Process.GetProcessById(pid))
            {
                _host.Shutdown("test");

                Assert.That(worker.WaitForExit(ExitWaitMilliseconds), Is.True, "The worker must exit after Shutdown.");
            }

            Assert.That(_host.CurrentProcessId, Is.Null);
        }

        /// <summary>
        /// What: a request the worker rejects at run level (two sources sharing a projectRelativePath)
        /// is reported as WorkerFailed with the worker's message, and the same process still serves
        /// the next request.
        /// </summary>
        [Test]
        public async Task RunAsync_RunLevelRejection_ReportsWorkerFailedAndKeepsProcess()
        {
            TransformWorkerInputDto good = TransformWorkerClientTests.BuildE2EFixtureInput();
            TransformWorkerInputDto rejected = TransformWorkerClientTests.BuildE2EFixtureInput();
            TransformWorkerSourceDto duplicate = new TransformWorkerSourceDto
            {
                sourcePath = rejected.sources[0].sourcePath,
                projectRelativePath = rejected.sources[0].projectRelativePath
            };
            rejected.sources = new[] { rejected.sources[0], duplicate };

            TransformWorkerHostResult failed = await _host.RunAsync(rejected, CancellationToken.None);
            TransformWorkerHostResult next = await _host.RunAsync(good, CancellationToken.None);

            Assert.That(failed.Kind, Is.EqualTo(TransformWorkerHostResultKind.WorkerFailed), failed.ErrorMessage);
            Assert.That(failed.ErrorMessage, Does.Contain("projectRelativePath"));
            Assert.That(next.Kind, Is.EqualTo(TransformWorkerHostResultKind.Completed), next.ErrorMessage);
            Assert.That(_host.LaunchCount, Is.EqualTo(1));
        }
    }
}
