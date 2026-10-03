using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the client disconnect monitor's start and stop contracts without a live connection.
    /// </summary>
    public sealed class UnityCliLoopBridgeClientDisconnectMonitorTests
    {
        /// <summary>
        /// Verifies monitoring a request that is already cancelled ends immediately without probing the connection.
        /// </summary>
        [Test]
        public void MonitorClientDisconnectAsync_WhenRequestAlreadyCancelled_ReturnsWithoutProbingConnection()
        {
            UnityCliLoopBridgeClientDisconnectMonitor monitor = new();
            int probeCount = 0;
            using MemoryStream stream = new MemoryStream();
            using BridgeClientConnection client = new("test-endpoint", stream, () =>
            {
                probeCount++;
                return true;
            });
            using CancellationTokenSource requestCancellationTokenSource = new CancellationTokenSource();
            requestCancellationTokenSource.Cancel();

            Task monitorTask = monitor.MonitorClientDisconnectAsync(client, requestCancellationTokenSource);

            Assert.That(monitorTask.IsCompleted, Is.True);
            Assert.That(monitorTask.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(probeCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies stopping when no monitor was started leaves the request running.
        /// </summary>
        [Test]
        public void StopClientDisconnectMonitorAsync_WhenNoMonitorStarted_DoesNotCancelRequest()
        {
            UnityCliLoopBridgeClientDisconnectMonitor monitor = new();
            using CancellationTokenSource requestCancellationTokenSource = new CancellationTokenSource();

            Task stopTask = monitor.StopClientDisconnectMonitorAsync(null, requestCancellationTokenSource);

            Assert.That(stopTask.IsCompleted, Is.True);
            stopTask.GetAwaiter().GetResult();
            Assert.That(requestCancellationTokenSource.IsCancellationRequested, Is.False);
        }

        /// <summary>
        /// Verifies stopping a started monitor cancels the request source that drives the monitor loop.
        /// </summary>
        [Test]
        public void StopClientDisconnectMonitorAsync_WhenMonitorStarted_CancelsRequestSource()
        {
            UnityCliLoopBridgeClientDisconnectMonitor monitor = new();
            using CancellationTokenSource requestCancellationTokenSource = new CancellationTokenSource();

            Task stopTask = monitor.StopClientDisconnectMonitorAsync(Task.CompletedTask, requestCancellationTokenSource);

            Assert.That(stopTask.IsCompleted, Is.True);
            stopTask.GetAwaiter().GetResult();
            Assert.That(requestCancellationTokenSource.IsCancellationRequested, Is.True);
        }
    }
}
