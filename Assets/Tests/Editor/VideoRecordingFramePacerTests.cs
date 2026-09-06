using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies wall-clock frame pacing for video recording.
    /// </summary>
    public sealed class VideoRecordingFramePacerTests
    {
        /// <summary>
        /// What: no frames are due at the start of a recording.
        /// </summary>
        [Test]
        public void FramesDue_WhenElapsedIsZero_ReturnsZero()
        {
            int due = VideoRecordingFramePacer.FramesDue(0.0, 30, 0);

            Assert.That(due, Is.EqualTo(0));
        }

        /// <summary>
        /// What: 30 fps for 0.5 seconds with no encoded frames requests 15 frames.
        /// </summary>
        [Test]
        public void FramesDue_WhenHalfSecondAt30FpsAndNoneEncoded_Returns15()
        {
            int due = VideoRecordingFramePacer.FramesDue(0.5, 30, 0);

            Assert.That(due, Is.EqualTo(15));
        }

        /// <summary>
        /// What: a caught-up recording requests no additional frames.
        /// </summary>
        [Test]
        public void FramesDue_WhenOneSecondAt30FpsAnd30Encoded_ReturnsZero()
        {
            int due = VideoRecordingFramePacer.FramesDue(1.0, 30, 30);

            Assert.That(due, Is.EqualTo(0));
        }

        /// <summary>
        /// What: a long stall is capped so one tick cannot enqueue hundreds of frames.
        /// </summary>
        [Test]
        public void FramesDue_WhenTenSecondsAndNoneEncoded_ReturnsMaxFramesPerTick()
        {
            int due = VideoRecordingFramePacer.FramesDue(10.0, 30, 0);

            Assert.That(due, Is.EqualTo(VideoRecordingFramePacer.MaxFramesPerTick));
            Assert.That(due, Is.EqualTo(60));
        }

        /// <summary>
        /// What: encoded count above expected never produces a negative due count.
        /// </summary>
        [Test]
        public void FramesDue_WhenEncodedExceedsExpected_ReturnsZero()
        {
            int due = VideoRecordingFramePacer.FramesDue(0.5, 30, 20);

            Assert.That(due, Is.EqualTo(0));
        }
    }
}
