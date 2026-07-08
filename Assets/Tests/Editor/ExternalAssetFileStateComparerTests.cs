using System;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests the shared external asset file-state identity contract.
    /// </summary>
    public sealed class ExternalAssetFileStateComparerTests
    {
        private static readonly DateTime SavedTime =
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// Verifies file states match when every fingerprint field is equal.
        /// </summary>
        [Test]
        public void HasSameFileState_WhenAllFieldsMatch_ReturnsTrue()
        {
            bool hasSameFileState = ExternalAssetFileStateComparer.HasSameFileState(
                (true, SavedTime, 10),
                (true, SavedTime, 10));

            Assert.That(hasSameFileState, Is.True);
        }

        /// <summary>
        /// Verifies file states differ when only file existence changes.
        /// </summary>
        [Test]
        public void HasSameFileState_WhenExistenceDiffers_ReturnsFalse()
        {
            bool hasSameFileState = ExternalAssetFileStateComparer.HasSameFileState(
                (true, SavedTime, 10),
                (false, SavedTime, 10));

            Assert.That(hasSameFileState, Is.False);
        }

        /// <summary>
        /// Verifies file states differ when only the last write time changes.
        /// </summary>
        [Test]
        public void HasSameFileState_WhenLastWriteTimeDiffers_ReturnsFalse()
        {
            bool hasSameFileState = ExternalAssetFileStateComparer.HasSameFileState(
                (true, SavedTime, 10),
                (true, SavedTime.AddMinutes(1), 10));

            Assert.That(hasSameFileState, Is.False);
        }

        /// <summary>
        /// Verifies file states differ when only the file length changes.
        /// </summary>
        [Test]
        public void HasSameFileState_WhenLengthDiffers_ReturnsFalse()
        {
            bool hasSameFileState = ExternalAssetFileStateComparer.HasSameFileState(
                (true, SavedTime, 10),
                (true, SavedTime, 20));

            Assert.That(hasSameFileState, Is.False);
        }
    }
}
