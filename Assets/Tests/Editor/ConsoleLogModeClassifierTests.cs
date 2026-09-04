using NUnit.Framework;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies Unity Console mode flags map to the public log severity.
    /// </summary>
    [TestFixture]
    public sealed class ConsoleLogModeClassifierTests
    {
        /// <summary>
        /// What: known scripting and assertion mode flags map to their Unity log types.
        /// </summary>
        [TestCase(0x804400, LogType.Log)]
        [TestCase(0x804200, LogType.Warning)]
        [TestCase(0x804100, LogType.Error)]
        [TestCase((1 << 21) | (1 << 19) | (1 << 18), LogType.Assert)]
        [TestCase(1 << 1, LogType.Assert)]
        public void Classify_WhenModeContainsKnownSeverity_ReturnsExpectedLogType(int mode, LogType expected)
        {
            ConsoleLogModeClassifier classifier = new ConsoleLogModeClassifier();

            LogType actual = classifier.Classify(mode);

            Assert.That(actual, Is.EqualTo(expected));
        }
    }
}
