using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests PlayMode state bridge responses without depending on Editor PlayMode transitions.
    /// </summary>
    [TestFixture]
    public sealed class PlayModeStateBridgeCommandTests
    {
        [Test]
        public void BuildResponse_WhenEditorIsPlayingAndPaused_ReturnsPausedMessage()
        {
            // Verifies Debug.Break polling can distinguish the paused PlayMode state.
            GetPlayModeStateResponse response = PlayModeStateBridgeCommand.BuildResponse(
                isPlaying: true,
                isPaused: true);

            Assert.That(response.IsPlaying, Is.True);
            Assert.That(response.IsPaused, Is.True);
            Assert.That(response.Message, Does.Contain("paused"));
        }

        [Test]
        public void BuildResponse_WhenEditorIsNotPlaying_ReturnsUnpausedMessage()
        {
            // Verifies non-PlayMode idle state is reported as unpaused instead of a debug break.
            GetPlayModeStateResponse response = PlayModeStateBridgeCommand.BuildResponse(
                isPlaying: false,
                isPaused: false);

            Assert.That(response.IsPlaying, Is.False);
            Assert.That(response.IsPaused, Is.False);
            Assert.That(response.Message, Does.Contain("not playing"));
        }
    }
}
