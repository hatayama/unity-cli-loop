using NUnit.Framework;
using UnityEditor;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies record-video quality maps onto MediaEncoder bitrate modes.
    /// </summary>
    public sealed class RecordVideoQualityPolicyTests
    {
        /// <summary>
        /// What: each quality value maps to the matching VideoBitrateMode.
        /// </summary>
        [TestCase(RecordVideoQuality.Low, VideoBitrateMode.Low)]
        [TestCase(RecordVideoQuality.Medium, VideoBitrateMode.Medium)]
        [TestCase(RecordVideoQuality.High, VideoBitrateMode.High)]
        public void ToBitrateMode_MapsEachQuality(RecordVideoQuality quality, VideoBitrateMode expected)
        {
            VideoBitrateMode mode = VideoQualityPolicy.ToBitrateMode(quality);

            Assert.That(mode, Is.EqualTo(expected));
        }
    }
}
