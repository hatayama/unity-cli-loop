using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.UnitTests
{
    /// <summary>
    /// Pure unit coverage for async Roslyn worker line reading and compile-gate shutdown.
    /// </summary>
    [TestFixture]
    public class SharedRoslynCompilerWorkerLineReaderTests
    {
        [Test]
        public async Task ReadLineAsync_WhenLineAvailable_ShouldReturnLine()
        {
            // Verifies a normal protocol line is returned without hitting the timeout path.
            using StringReader reader = new("hello\n");

            string line = await SharedRoslynCompilerWorkerLineReader.ReadLineAsync(
                reader,
                CancellationToken.None,
                timeoutMilliseconds: 1000);

            Assert.That(line, Is.EqualTo("hello"));
        }

        [Test]
        public async Task ReadLineAsync_WhenNoLineWithinTimeout_ShouldReturnNull()
        {
            // Verifies timeout returns null while the abandoned ReadLine is only observed.
            using AnonymousPipeServerStream server = new(PipeDirection.Out);
            using AnonymousPipeClientStream client = new(
                PipeDirection.In,
                server.ClientSafePipeHandle);
            using StreamReader reader = new(client);

            string line = await SharedRoslynCompilerWorkerLineReader.ReadLineAsync(
                reader,
                CancellationToken.None,
                timeoutMilliseconds: 40);

            Assert.That(line, Is.Null);
            server.Dispose();
        }

        [Test]
        public void ReadLineAsync_WhenCanceled_ShouldThrowOperationCanceledException()
        {
            // Verifies cooperative cancel surfaces as OperationCanceledException before ReadLine starts.
            using CancellationTokenSource cancellationTokenSource = new();
            cancellationTokenSource.Cancel();
            using StringReader reader = new(string.Empty);

            Assert.ThrowsAsync<OperationCanceledException>(
                async () => await SharedRoslynCompilerWorkerLineReader.ReadLineAsync(
                    reader,
                    cancellationTokenSource.Token,
                    timeoutMilliseconds: 1000));
        }

        [Test]
        public async Task ReadLineAsync_WhenCanceledDuringWait_ShouldThrowAndAllowPipeClose()
        {
            // Verifies mid-wait cancel returns without holding the caller on the abandoned ReadLine.
            using AnonymousPipeServerStream server = new(PipeDirection.Out);
            using AnonymousPipeClientStream client = new(
                PipeDirection.In,
                server.ClientSafePipeHandle);
            using StreamReader reader = new(client);
            using CancellationTokenSource cancellationTokenSource = new();
            Task<string> readTask = SharedRoslynCompilerWorkerLineReader.ReadLineAsync(
                reader,
                cancellationTokenSource.Token,
                timeoutMilliseconds: 5000);

            await Task.Delay(20);
            cancellationTokenSource.Cancel();

            Assert.ThrowsAsync<OperationCanceledException>(async () => await readTask);
            server.Dispose();
        }

        [Test]
        public async Task ReadDiagnosticLinesAsync_WhenEndMarkerArrives_ShouldReturnCollectedLines()
        {
            // Verifies diagnostic aggregation stops at the protocol end marker.
            using StringReader reader = new("diag-a\ndiag-b\n__ULOOP_END__\n");

            List<string> lines = await SharedRoslynCompilerWorkerLineReader.ReadDiagnosticLinesAsync(
                reader,
                "__ULOOP_END__",
                CancellationToken.None,
                timeoutMilliseconds: 1000);

            Assert.That(lines, Is.EqualTo(new[] { "diag-a", "diag-b" }));
        }
    }

    [TestFixture]
    public class SharedRoslynCompilerWorkerSessionCoordinationTests
    {
        [Test]
        public async Task RunShutdownWithoutCompileGate_WhileCompileGateHeld_ShouldCompleteWithoutWaitingForGate()
        {
            // Verifies shutdown bypasses the async compile gate so a stuck read cannot block it.
            SharedRoslynCompilerWorkerSessionCoordination coordination = new();
            TaskCompletionSource<bool> gateEntered =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<bool> allowCompileToFinish =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            int shutdownStateLockEntries = 0;

            Task<bool> compileTask = coordination.RunSerializedCompileAsync(
                async _ =>
                {
                    gateEntered.TrySetResult(true);
                    await allowCompileToFinish.Task;
                    return true;
                },
                CancellationToken.None);

            await gateEntered.Task;
            Stopwatch stopwatch = Stopwatch.StartNew();
            coordination.RunShutdownWithoutCompileGate(() =>
            {
                coordination.AssertStateLockHeld();
                shutdownStateLockEntries++;
            });
            stopwatch.Stop();

            Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(500));
            Assert.That(shutdownStateLockEntries, Is.EqualTo(1));

            allowCompileToFinish.TrySetResult(true);
            bool compileResult = await compileTask;
            Assert.That(compileResult, Is.True);
        }

        [Test]
        public async Task RunSerializedCompileAsync_AfterShutdown_ShouldStillRunSerializedBody()
        {
            // Verifies a later compile conversation is not stuck after shutdown cleared process state.
            SharedRoslynCompilerWorkerSessionCoordination coordination = new();
            bool processCleared = false;

            coordination.RunShutdownWithoutCompileGate(() =>
            {
                processCleared = true;
            });

            bool ran = await coordination.RunSerializedCompileAsync(
                _ => Task.FromResult(true),
                CancellationToken.None);

            Assert.That(processCleared, Is.True);
            Assert.That(ran, Is.True);
            Assert.That(
                coordination.ExecuteWithStateLock(() => processCleared),
                Is.True);
        }

        [Test]
        public async Task RunSerializedCompileAsync_ShouldSerializeConversations()
        {
            // Verifies write→read conversations stay single-flight under the async gate.
            SharedRoslynCompilerWorkerSessionCoordination coordination = new();
            int concurrent = 0;
            int maxConcurrent = 0;
            object concurrentLock = new();

            Task<int> first = coordination.RunSerializedCompileAsync(
                async _ =>
                {
                    lock (concurrentLock)
                    {
                        concurrent++;
                        maxConcurrent = Math.Max(maxConcurrent, concurrent);
                    }

                    await Task.Delay(40);
                    lock (concurrentLock)
                    {
                        concurrent--;
                    }

                    return 1;
                },
                CancellationToken.None);

            Task<int> second = coordination.RunSerializedCompileAsync(
                async _ =>
                {
                    lock (concurrentLock)
                    {
                        concurrent++;
                        maxConcurrent = Math.Max(maxConcurrent, concurrent);
                    }

                    await Task.Delay(10);
                    lock (concurrentLock)
                    {
                        concurrent--;
                    }

                    return 2;
                },
                CancellationToken.None);

            int[] results = await Task.WhenAll(first, second);
            Assert.That(results, Is.EquivalentTo(new[] { 1, 2 }));
            Assert.That(maxConcurrent, Is.EqualTo(1));
        }
    }
}
