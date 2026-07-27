using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies the preflight result carries the active pause point's id as a
    /// structured field, not only inside the human-readable rejection message.
    /// </summary>
    public class PlayModeToolPreflightResultTests
    {
        private const string PausedActionDescription = "simulating keyboard input";

        [Test]
        public void Evaluate_WhenPlayModeIsNotActive_ReportsNoPausePointId()
        {
            // Verifies a not-active rejection has nothing to do with a pause point, so the structured field stays empty.
            PlayModeToolPreflightResult result = PlayModeToolPreflightService.Evaluate(
                isPlaying: false,
                isPaused: false,
                activePausePointId: "marker",
                pausedActionDescription: PausedActionDescription);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo(PlayModeToolPreflightService.PlayModeNotActiveMessage));
            Assert.That(result.RejectedByActivePausePointId, Is.Null);
        }

        [Test]
        public void Evaluate_WhenPausedByPausePoint_ReportsThatPausePointId()
        {
            // Verifies a pause-point-owned pause reports the id in a field a caller can compare, since the message alone forces string matching.
            PlayModeToolPreflightResult result = PlayModeToolPreflightService.Evaluate(
                isPlaying: true,
                isPaused: true,
                activePausePointId: "marker",
                pausedActionDescription: PausedActionDescription);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.RejectedByActivePausePointId, Is.EqualTo("marker"));
            Assert.That(
                result.ErrorMessage,
                Is.EqualTo(PlayModeToolPreflightService.FormatPausePointPausedMessage("marker", PausedActionDescription)));
        }

        [Test]
        public void Evaluate_WhenPausedWithoutPausePoint_ReportsNoPausePointId()
        {
            // Verifies a manual pause is not attributed to a pause point, so a caller cannot mistake it for one of its own markers.
            PlayModeToolPreflightResult result = PlayModeToolPreflightService.Evaluate(
                isPlaying: true,
                isPaused: true,
                activePausePointId: string.Empty,
                pausedActionDescription: PausedActionDescription);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.RejectedByActivePausePointId, Is.Null);
            Assert.That(
                result.ErrorMessage,
                Is.EqualTo(PlayModeToolPreflightService.FormatPausedMessage(PausedActionDescription)));
        }

        [Test]
        public void Evaluate_WhenPlayingAndNotPaused_Succeeds()
        {
            // Verifies the success path stays a plain success with no rejection details attached.
            PlayModeToolPreflightResult result = PlayModeToolPreflightService.Evaluate(
                isPlaying: true,
                isPaused: false,
                activePausePointId: "marker",
                pausedActionDescription: PausedActionDescription);

            Assert.That(result.IsValid, Is.True);
            Assert.That(result.ErrorMessage, Is.Empty);
            Assert.That(result.RejectedByActivePausePointId, Is.Null);
        }
    }
}
