using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies heartbeat negotiation and frame emission for the bridge server.
    /// </summary>
    public class JsonRpcHeartbeatTests
    {
        [Test]
        public void CreateDispatchAcceptedResponse_WhenHeartbeatNegotiated_AdvertisesInterval()
        {
            // Tests that the dispatch ack tells a heartbeat-capable CLI which interval to expect.
            string response = JsonRpcResponseFactory.CreateDispatchAcceptedResponse(1, 10);

            JObject parsed = JObject.Parse(response);
            Assert.That(parsed["uloop"]["phase"].ToString(), Is.EqualTo(JsonRpcResponsePhases.Accepted));
            Assert.That(parsed["uloop"]["heartbeatIntervalSeconds"].Value<int>(), Is.EqualTo(10));
        }

        [Test]
        public void CreateDispatchAcceptedResponse_WithoutHeartbeat_OmitsInterval()
        {
            // Tests that older CLIs that did not negotiate heartbeats get the legacy ack shape,
            // because they would treat unexpected extra frames as the final response.
            string response = JsonRpcResponseFactory.CreateDispatchAcceptedResponse(1, 0);

            JObject parsed = JObject.Parse(response);
            Assert.That(parsed["uloop"]["phase"].ToString(), Is.EqualTo(JsonRpcResponsePhases.Accepted));
            Assert.That(parsed["uloop"]["heartbeatIntervalSeconds"], Is.Null);
        }

        [Test]
        public void CreateHeartbeatResponse_WhenSerialized_CarriesPhaseAndStallSeconds()
        {
            // Tests the heartbeat frame shape the CLI parses for freeze diagnosis.
            string response = JsonRpcResponseFactory.CreateHeartbeatResponse(7, 12.5);

            JObject parsed = JObject.Parse(response);
            Assert.That(parsed["uloop"]["phase"].ToString(), Is.EqualTo(JsonRpcResponsePhases.Heartbeat));
            Assert.That(parsed["uloop"]["mainThreadStallSeconds"].Value<double>(), Is.EqualTo(12.5));
            Assert.That(parsed["id"].Value<int>(), Is.EqualTo(7));
        }

        [Test]
        public async Task SendHeartbeatsAsync_WhenRunning_WritesFramesUntilCancelled()
        {
            // Tests that the heartbeat loop emits frames on the interval and stops on cancellation
            // without leaving background work behind.
            int writtenFrameCount = 0;
            using CancellationTokenSource cancellationSource = new();
            UnityCliLoopBridgeHeartbeatService heartbeatService = new();

            Task heartbeatTask = heartbeatService.SendHeartbeatsAsync(
                () => "{}",
                _ =>
                {
                    Interlocked.Increment(ref writtenFrameCount);
                    return Task.CompletedTask;
                },
                TimeSpan.FromMilliseconds(10),
                cancellationSource.Token);

            await Task.Delay(100);
            cancellationSource.Cancel();
            await heartbeatTask;

            Assert.That(Volatile.Read(ref writtenFrameCount), Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public async Task SendHeartbeatsAsync_WhenWriteThrowsIOException_StopsWithoutFaulting()
        {
            // Tests that a broken connection ends the heartbeat loop silently; teardown is
            // owned by the read loop, not the heartbeat writer.
            using CancellationTokenSource cancellationSource = new();
            UnityCliLoopBridgeHeartbeatService heartbeatService = new();

            Task heartbeatTask = heartbeatService.SendHeartbeatsAsync(
                () => "{}",
                _ => throw new System.IO.IOException("broken pipe"),
                TimeSpan.FromMilliseconds(1),
                cancellationSource.Token);

            await heartbeatTask;

            Assert.That(heartbeatTask.IsCompletedSuccessfully, Is.True);
        }

        [Test]
        public void EditorMainThreadLivenessTracker_AfterRegistration_ReportsSmallStall()
        {
            // Tests that the tracker reports near-zero stall right after a recorded tick.
            EditorMainThreadLivenessTracker.RegisterForEditorStartup();

            double stallSeconds = EditorMainThreadLivenessTracker.SecondsSinceLastMainThreadTick();

            Assert.That(stallSeconds, Is.GreaterThanOrEqualTo(0));
            Assert.That(stallSeconds, Is.LessThan(60));
        }

        /// <summary>
        /// Verifies the heartbeat loop keeps writing until the stream reports disposal, then ends without faulting.
        /// </summary>
        [Test]
        public void SendHeartbeatsAsync_WhenWriteReportsDisposedStream_StopsWithoutFaulting()
        {
            UnityCliLoopBridgeHeartbeatService heartbeatService = new();
            int writeCount = 0;

            // Zero interval makes each delay complete synchronously, so the bounded write sequence
            // below runs to completion inside this call without timers.
            Task heartbeatTask = heartbeatService.SendHeartbeatsAsync(
                () => "{}",
                _ =>
                {
                    writeCount++;
                    if (writeCount < 2)
                    {
                        return Task.CompletedTask;
                    }

                    return Task.FromException(new ObjectDisposedException("client-stream"));
                },
                TimeSpan.Zero,
                CancellationToken.None);

            Assert.That(heartbeatTask.IsCompleted, Is.True);
            Assert.That(heartbeatTask.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(writeCount, Is.EqualTo(2));
        }

        /// <summary>
        /// Verifies stopping when no heartbeat task was started leaves the cancellation source untouched.
        /// </summary>
        [Test]
        public void StopHeartbeatsAsync_WhenNoHeartbeatTaskStarted_DoesNotCancelSource()
        {
            UnityCliLoopBridgeHeartbeatService heartbeatService = new();
            using CancellationTokenSource heartbeatCancellationSource = new CancellationTokenSource();

            Task stopTask = heartbeatService.StopHeartbeatsAsync(null, heartbeatCancellationSource);

            Assert.That(stopTask.IsCompleted, Is.True);
            stopTask.GetAwaiter().GetResult();
            Assert.That(heartbeatCancellationSource.IsCancellationRequested, Is.False);
        }

        /// <summary>
        /// Verifies stopping a started heartbeat task cancels its source and completes once the task has finished.
        /// </summary>
        [Test]
        public void StopHeartbeatsAsync_WhenHeartbeatTaskExists_CancelsSource()
        {
            UnityCliLoopBridgeHeartbeatService heartbeatService = new();
            using CancellationTokenSource heartbeatCancellationSource = new CancellationTokenSource();

            Task stopTask = heartbeatService.StopHeartbeatsAsync(Task.CompletedTask, heartbeatCancellationSource);

            Assert.That(stopTask.IsCompleted, Is.True);
            stopTask.GetAwaiter().GetResult();
            Assert.That(heartbeatCancellationSource.IsCancellationRequested, Is.True);
        }

        /// <summary>
        /// Verifies stopping a heartbeat task that has no cancellation source still awaits the task without throwing.
        /// </summary>
        [Test]
        public void StopHeartbeatsAsync_WhenCancellationSourceIsNull_CompletesWithHeartbeatTask()
        {
            UnityCliLoopBridgeHeartbeatService heartbeatService = new();

            Task stopTask = heartbeatService.StopHeartbeatsAsync(Task.CompletedTask, null);

            Assert.That(stopTask.IsCompleted, Is.True);
            Assert.That(stopTask.Status, Is.EqualTo(TaskStatus.RanToCompletion));
        }
    }
}
