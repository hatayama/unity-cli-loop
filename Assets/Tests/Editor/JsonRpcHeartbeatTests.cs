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
            string response = JsonRpcProcessor.CreateDispatchAcceptedResponse(1, 10);

            JObject parsed = JObject.Parse(response);
            Assert.That(parsed["uloop"]["phase"].ToString(), Is.EqualTo(JsonRpcResponsePhases.Accepted));
            Assert.That(parsed["uloop"]["heartbeatIntervalSeconds"].Value<int>(), Is.EqualTo(10));
        }

        [Test]
        public void CreateDispatchAcceptedResponse_WithoutHeartbeat_OmitsInterval()
        {
            // Tests that older CLIs that did not negotiate heartbeats get the legacy ack shape,
            // because they would treat unexpected extra frames as the final response.
            string response = JsonRpcProcessor.CreateDispatchAcceptedResponse(1, 0);

            JObject parsed = JObject.Parse(response);
            Assert.That(parsed["uloop"]["phase"].ToString(), Is.EqualTo(JsonRpcResponsePhases.Accepted));
            Assert.That(parsed["uloop"]["heartbeatIntervalSeconds"], Is.Null);
        }

        [Test]
        public void CreateHeartbeatResponse_WhenSerialized_CarriesPhaseAndStallSeconds()
        {
            // Tests the heartbeat frame shape the CLI parses for freeze diagnosis.
            string response = JsonRpcProcessor.CreateHeartbeatResponse(7, 12.5);

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

            Task heartbeatTask = UnityCliLoopBridgeServer.SendHeartbeatsAsync(
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

            Task heartbeatTask = UnityCliLoopBridgeServer.SendHeartbeatsAsync(
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
    }
}
