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
    }
}
