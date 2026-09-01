using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies H.264-safe even-dimension rounding for video frames.
    /// </summary>
    public sealed class VideoFrameSizePolicyTests
    {
        /// <summary>
        /// What: an odd size is rounded down to the previous even value.
        /// </summary>
        [Test]
        public void RoundDownToEven_WhenOdd_ReturnsPreviousEven()
        {
            int rounded = VideoFrameSizePolicy.RoundDownToEven(723);

            Assert.That(rounded, Is.EqualTo(722));
        }

        /// <summary>
        /// What: an even size is left unchanged.
        /// </summary>
        [Test]
        public void RoundDownToEven_WhenEven_ReturnsSameValue()
        {
            int rounded = VideoFrameSizePolicy.RoundDownToEven(1286);

            Assert.That(rounded, Is.EqualTo(1286));
        }

        /// <summary>
        /// What: size 1 rounds to 0 so Start can reject an unusable encoder size.
        /// </summary>
        [Test]
        public void RoundDownToEven_WhenOne_ReturnsZero()
        {
            int rounded = VideoFrameSizePolicy.RoundDownToEven(1);

            Assert.That(rounded, Is.EqualTo(0));
        }

        /// <summary>
        /// What: scale 1.0 even-rounds the Game View size with no shrink.
        /// </summary>
        [Test]
        public void Resolve_WhenScaleIsOne_EvenRoundsSourceSize()
        {
            (int width, int height) size = VideoFrameSizePolicy.Resolve(1286, 723, 1.0f);

            Assert.That(size.width, Is.EqualTo(1286));
            Assert.That(size.height, Is.EqualTo(722));
        }

        /// <summary>
        /// What: scale 0.5 floors then even-rounds (1286×723 → 642×360).
        /// </summary>
        [Test]
        public void Resolve_WhenScaleIsHalf_FloorsThenEvenRounds()
        {
            (int width, int height) size = VideoFrameSizePolicy.Resolve(1286, 723, 0.5f);

            Assert.That(size.width, Is.EqualTo(642));
            Assert.That(size.height, Is.EqualTo(360));
        }

        /// <summary>
        /// What: scale 0.25 floors then even-rounds (1286×723 → 320×180).
        /// </summary>
        [Test]
        public void Resolve_WhenScaleIsQuarter_FloorsThenEvenRounds()
        {
            (int width, int height) size = VideoFrameSizePolicy.Resolve(1286, 723, 0.25f);

            Assert.That(size.width, Is.EqualTo(320));
            Assert.That(size.height, Is.EqualTo(180));
        }

        /// <summary>
        /// What: scale 0.1 floors then even-rounds (1286×723 → 128×72).
        /// </summary>
        [Test]
        public void Resolve_WhenScaleIsOneTenth_FloorsThenEvenRounds()
        {
            (int width, int height) size = VideoFrameSizePolicy.Resolve(1286, 723, 0.1f);

            Assert.That(size.width, Is.EqualTo(128));
            Assert.That(size.height, Is.EqualTo(72));
        }

        /// <summary>
        /// What: a source that shrinks below 2px even-rounds to 0 so Start can reject it.
        /// </summary>
        [Test]
        public void Resolve_WhenScaleShrinksBelowTwoPixels_ReturnsZeroWidth()
        {
            (int width, int height) size = VideoFrameSizePolicy.Resolve(3, 8, 0.25f);

            Assert.That(size.width, Is.EqualTo(0));
            Assert.That(size.height, Is.EqualTo(2));
        }

        /// <summary>
        /// What: scale 0.5 of 1286×723 matches a 642×360 encoder after Resolve.
        /// </summary>
        [Test]
        public void MatchesEncoderSize_WhenHalfOf1286x723_Matches642x360()
        {
            bool matches = VideoFrameSizePolicy.MatchesEncoderSize(
                1286,
                723,
                0.5f,
                642,
                360);

            Assert.That(matches, Is.True);
        }

        /// <summary>
        /// What: a resized Game View fails the 0.5 match against the original encoder size.
        /// </summary>
        [Test]
        public void MatchesEncoderSize_WhenSourceResized_DoesNotMatchOriginalEncoder()
        {
            bool matches = VideoFrameSizePolicy.MatchesEncoderSize(
                1288,
                724,
                0.5f,
                642,
                360);

            Assert.That(matches, Is.False);
        }

        /// <summary>
        /// What: a 3px-narrower Game View at 0.5 does not match the original encoder size.
        /// </summary>
        [Test]
        public void MatchesEncoderSize_WhenSourceShrinksByThreePixelsAtHalf_DoesNotMatch()
        {
            bool matches = VideoFrameSizePolicy.MatchesEncoderSize(
                1283,
                723,
                0.5f,
                642,
                360);

            Assert.That(matches, Is.False);
        }
    }
}
