using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Domain-reload and quit callbacks stop the shared resident worker process.
    /// </summary>
    public class TransformWorkerHostLifecycleTests
    {
        private const int ProcessExitWaitMilliseconds = 10_000;
        private const int ExitPollIntervalMilliseconds = 50;

        /// <summary>
        /// After a real resident run, the reload callback kills the worker process and clears
        /// the host's current process id.
        /// </summary>
        [Test]
        public async Task ShutdownForReload_StopsSharedWorker()
        {
            TransformWorkerInputDto input = TransformWorkerClientTests.BuildE2EFixtureInput();

            TransformWorkerHostResult result = await TransformWorkerHost.Shared.RunAsync(input, CancellationToken.None);

            Assert.That(result.Kind, Is.EqualTo(TransformWorkerHostResultKind.Completed), result.ErrorMessage);
            int? processId = TransformWorkerHost.Shared.CurrentProcessId;
            Assert.That(processId, Is.Not.Null);

            using (Process worker = Process.GetProcessById(processId.Value))
            {
                TransformWorkerHostLifecycle.ShutdownForReload();

                Assert.That(await WaitForExitAsync(worker, ProcessExitWaitMilliseconds), Is.True);
            }

            Assert.That(TransformWorkerHost.Shared.CurrentProcessId, Is.Null);
        }

        // Why poll instead of Process.WaitForExit: an EditMode test that blocks the Editor's main
        // thread on a child process stalls the whole run.
        private static async Task<bool> WaitForExitAsync(Process process, int timeoutMilliseconds)
        {
            Stopwatch waited = Stopwatch.StartNew();
            while (waited.ElapsedMilliseconds < timeoutMilliseconds)
            {
                if (process.HasExited)
                {
                    return true;
                }

                await Task.Delay(ExitPollIntervalMilliseconds);
            }

            return process.HasExited;
        }
    }
}
