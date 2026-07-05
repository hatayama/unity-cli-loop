using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies PlayMode preflight validation shared by first-party tool use cases.
    /// </summary>
    public class PlayModeToolPreflightServiceTests
    {
        private const string ExpectedNotActiveMessage =
            "PlayMode is not active. Use control-play-mode tool to start PlayMode first.";

        [Test]
        public void RequireActive_WhenEditModeIsNotPlaying_ReturnsNotActiveFailure()
        {
            // Verifies the active-only preflight fails with the exact wire-visible not-active message.
            ValidationResult result = PlayModeToolPreflightService.RequireActive();

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo(ExpectedNotActiveMessage));
        }

        [Test]
        public void RequireActiveAndNotPaused_WhenEditModeIsNotPlaying_ReturnsNotActiveFailure()
        {
            // Verifies the paused-aware preflight also fails with the exact not-active message when PlayMode is inactive.
            ValidationResult result = PlayModeToolPreflightService.RequireActiveAndNotPaused("recording input");

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo(ExpectedNotActiveMessage));
        }

        [Test]
        public void PlayModeNotActiveMessage_MatchesOriginalToolResponseString()
        {
            // Pins the not-active constant exposed to callers so any refactor keeps the wire string byte-identical.
            Assert.That(
                PlayModeToolPreflightService.PlayModeNotActiveMessage,
                Is.EqualTo(ExpectedNotActiveMessage));
        }

        [Test]
        public void FormatPausedMessage_WithRecordingInputSuffix_MatchesOriginalToolResponse()
        {
            // Pins RecordInput's exact wire-visible paused message.
            Assert.That(
                PlayModeToolPreflightService.FormatPausedMessage("recording input"),
                Is.EqualTo("PlayMode is paused. Resume PlayMode before recording input."));
        }

        [Test]
        public void FormatPausedMessage_WithReplayingInputSuffix_MatchesOriginalToolResponse()
        {
            // Pins ReplayInput's exact wire-visible paused message.
            Assert.That(
                PlayModeToolPreflightService.FormatPausedMessage("replaying input"),
                Is.EqualTo("PlayMode is paused. Resume PlayMode before replaying input."));
        }

        [Test]
        public void FormatPausedMessage_WithSimulatingKeyboardInputSuffix_MatchesOriginalToolResponse()
        {
            // Pins SimulateKeyboard's exact wire-visible paused message.
            Assert.That(
                PlayModeToolPreflightService.FormatPausedMessage("simulating keyboard input"),
                Is.EqualTo("PlayMode is paused. Resume PlayMode before simulating keyboard input."));
        }

        [Test]
        public void FormatPausedMessage_WithSimulatingMouseInputSuffix_MatchesOriginalToolResponse()
        {
            // Pins SimulateMouseInput's exact wire-visible paused message.
            Assert.That(
                PlayModeToolPreflightService.FormatPausedMessage("simulating mouse input"),
                Is.EqualTo("PlayMode is paused. Resume PlayMode before simulating mouse input."));
        }
    }
}
