using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies shutdown disposal of the bridge server's cancellation source.
    /// </summary>
    public class UnityCliLoopBridgeServerShutdownTests
    {
        [Test]
        public async Task DisposeCancellationSourceAfterServerTaskAsync_WhenAllTasksComplete_DisposesSource()
        {
            // Tests that the cancellation source is disposed once every shutdown-wait task has finished.
            CancellationTokenSource cancellationTokenSource = new();

            await UnityCliLoopBridgeServer.DisposeCancellationSourceAfterServerTaskAsync(
                Task.CompletedTask,
                new[] { Task.CompletedTask },
                cancellationTokenSource,
                TimeSpan.FromSeconds(1));

            Assert.Throws<ObjectDisposedException>(() => cancellationTokenSource.Cancel());
        }

        [Test]
        public async Task DisposeCancellationSourceAfterServerTaskAsync_WhenTaskOutlivesTimeout_SkipsDisposal()
        {
            // Tests that a straggling client task keeps the cancellation source alive so the task
            // can still observe its token without hitting ObjectDisposedException.
            CancellationTokenSource cancellationTokenSource = new();
            TaskCompletionSource<bool> stragglingTask = new();

            await UnityCliLoopBridgeServer.DisposeCancellationSourceAfterServerTaskAsync(
                Task.CompletedTask,
                new[] { stragglingTask.Task },
                cancellationTokenSource,
                TimeSpan.FromMilliseconds(50));

            Assert.DoesNotThrow(() => cancellationTokenSource.Cancel());

            // Complete the straggler so the test leaves no pending work behind.
            stragglingTask.SetResult(true);
            cancellationTokenSource.Dispose();
        }
    }
}
