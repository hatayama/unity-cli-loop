using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

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
            PlayModeToolPreflightResult result = PlayModeToolPreflightService.RequireActive();

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo(ExpectedNotActiveMessage));
        }

        [Test]
        public void RequireActiveAndNotPaused_WhenEditModeIsNotPlaying_ReturnsNotActiveFailure()
        {
            // Verifies the paused-aware preflight also fails with the exact not-active message when PlayMode is inactive.
            PlayModeToolPreflightResult result = PlayModeToolPreflightService.RequireActiveAndNotPaused(ReplayInputUseCase.PausedActionDescription);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo(ExpectedNotActiveMessage));
        }

        [Test]
        public void PlayModeNotActiveMessage_EqualsExpectedWireString()
        {
            // Pins the not-active constant exposed to callers so any refactor keeps the wire string byte-identical.
            Assert.That(
                PlayModeToolPreflightService.PlayModeNotActiveMessage,
                Is.EqualTo(ExpectedNotActiveMessage));
        }

        [Test]
        public void FormatPausedMessage_WithReplayInputSuffix_ReturnsExpectedWireString()
        {
            // Verifies ReplayInput's paused preflight message stays byte-identical, including the suffix the use case actually passes.
            Assert.That(
                PlayModeToolPreflightService.FormatPausedMessage(ReplayInputUseCase.PausedActionDescription),
                Is.EqualTo("PlayMode is paused. Resume PlayMode before replaying input."));
        }

        [Test]
        public void FormatPausedMessage_WithSimulateKeyboardSuffix_ReturnsExpectedWireString()
        {
            // Verifies SimulateKeyboard's paused preflight message stays byte-identical, including the suffix the use case actually passes.
            Assert.That(
                PlayModeToolPreflightService.FormatPausedMessage(SimulateKeyboardUseCase.PausedActionDescription),
                Is.EqualTo("PlayMode is paused. Resume PlayMode before simulating keyboard input."));
        }

        [Test]
        public void FormatPausedMessage_WithSimulateMouseInputSuffix_ReturnsExpectedWireString()
        {
            // Verifies SimulateMouseInput's paused preflight message stays byte-identical, including the suffix the use case actually passes.
            Assert.That(
                PlayModeToolPreflightService.FormatPausedMessage(SimulateMouseInputUseCase.PausedActionDescription),
                Is.EqualTo("PlayMode is paused. Resume PlayMode before simulating mouse input."));
        }

        [Test]
        public void FormatPausedMessage_WithSimulateMouseUiSuffix_ReturnsExpectedWireString()
        {
            // Verifies SimulateMouseUi's paused preflight message stays byte-identical, including the suffix the use case actually passes.
            Assert.That(
                PlayModeToolPreflightService.FormatPausedMessage(SimulateMouseUiUseCase.PausedActionDescription),
                Is.EqualTo("PlayMode is paused. Resume PlayMode before simulating UI input."));
        }

        [Test]
        public void FormatPausePointPausedMessage_WithSimulateKeyboardSuffix_ReturnsExpectedWireString()
        {
            // Verifies the pause-point-aware message names the active pause point so agents calling
            // simulate-keyboard right after a pause-point hit see the real cause instead of a generic rejection.
            Assert.That(
                PlayModeToolPreflightService.FormatPausePointPausedMessage(
                    "example-pause-point-id",
                    SimulateKeyboardUseCase.PausedActionDescription),
                Is.EqualTo(
                    "PlayMode is paused because pause point 'example-pause-point-id' is active " +
                    "(check pause-point-status). Resume PlayMode before simulating keyboard input."));
        }

        [Test]
        public void FormatPausePointPausedMessage_WithSimulateMouseInputSuffix_ReturnsExpectedWireString()
        {
            // Verifies the pause-point-aware message stays byte-identical for SimulateMouseInput's suffix too.
            Assert.That(
                PlayModeToolPreflightService.FormatPausePointPausedMessage(
                    "example-pause-point-id",
                    SimulateMouseInputUseCase.PausedActionDescription),
                Is.EqualTo(
                    "PlayMode is paused because pause point 'example-pause-point-id' is active " +
                    "(check pause-point-status). Resume PlayMode before simulating mouse input."));
        }
    }
}
