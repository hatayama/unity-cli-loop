using System.Collections;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;
using UnityEngine.TestTools;

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

        // Why wait: the installed warm-up may still be sending its transform worker requests,
        // which no cancel stops, to the shared host this test starts and stops a worker on.
        [UnitySetUp]
        public IEnumerator WaitForTheInstalledWarmUpToStop()
        {
            Task stopped = PublicizedCopyTestCache.StopInstalledWarmUpAsync();
            while (!stopped.IsCompleted)
            {
                yield return null;
            }
        }

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
