using Newtonsoft.Json;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that pins the wire name of the pause-point rejection field on every tool response
    /// a --trigger can dispatch, because the CLI matches on that exact name to detect a trigger that
    /// was refused before it ran.
    /// </summary>
    public class PausePointRejectionResponseFieldTests
    {
        private const string ExpectedJsonFragment = "\"RejectedByActivePausePointId\":\"marker\"";

        [Test]
        public void SimulateKeyboardResponse_WhenRejectedByPausePoint_SerializesTheRejectionField()
        {
            // Verifies simulate-keyboard's rejection response carries the field under the name the CLI reads.
            string json = JsonConvert.SerializeObject(
                new SimulateKeyboardResponse { Success = false, RejectedByActivePausePointId = "marker" });

            Assert.That(json, Does.Contain(ExpectedJsonFragment));
        }

        [Test]
        public void SimulateMouseInputResponse_WhenRejectedByPausePoint_SerializesTheRejectionField()
        {
            // Verifies simulate-mouse-input's rejection response carries the field under the name the CLI reads.
            string json = JsonConvert.SerializeObject(
                new SimulateMouseInputResponse { Success = false, RejectedByActivePausePointId = "marker" });

            Assert.That(json, Does.Contain(ExpectedJsonFragment));
        }

        [Test]
        public void SimulateMouseUiResponse_WhenRejectedByPausePoint_SerializesTheRejectionField()
        {
            // Verifies simulate-mouse-ui's rejection response carries the field under the name the CLI reads.
            string json = JsonConvert.SerializeObject(
                new SimulateMouseUiResponse { Success = false, RejectedByActivePausePointId = "marker" });

            Assert.That(json, Does.Contain(ExpectedJsonFragment));
        }

        [Test]
        public void ReplayInputResponse_WhenRejectedByPausePoint_SerializesTheRejectionField()
        {
            // Verifies replay-input reports the same structured rejection: it is a realistic --trigger target.
            string json = JsonConvert.SerializeObject(
                new ReplayInputResponse { Success = false, RejectedByActivePausePointId = "marker" });

            Assert.That(json, Does.Contain(ExpectedJsonFragment));
        }
    }
}
