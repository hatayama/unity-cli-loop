using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies the pure SessionState value parsers used by editor-session repositories.
    /// </summary>
    public sealed class UnityCliLoopEditorSessionStateStorageTests
    {
        /// <summary>
        /// Verifies that a digit string one past the 64-bit integer maximum is rejected instead of wrapping around.
        /// </summary>
        [Test]
        public void ParseUtcTicks_WhenDigitsOverflowLong_ReturnsInvalid()
        {
            (bool isValid, long value) = UnityCliLoopEditorSessionStateStorage.ParseUtcTicks("9223372036854775808");

            Assert.That(isValid, Is.False);
            Assert.That(value, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies that blank and duplicate request ids are dropped while the first-seen order is kept.
        /// </summary>
        [Test]
        public void ParseRequestIdIndex_WhenIndexHasBlankAndDuplicateEntries_ReturnsDistinctTrimmedIds()
        {
            string[] requestIds = UnityCliLoopEditorSessionStateStorage.ParseRequestIdIndex(
                "request-a\n   \n request-b \nrequest-a\n");

            Assert.That(requestIds, Is.EqualTo(new[] { "request-a", "request-b" }));
        }
    }
}
