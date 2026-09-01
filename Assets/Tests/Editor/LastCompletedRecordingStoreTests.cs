using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies SessionState round-trip for the last automatically stopped recording.
    /// </summary>
    public sealed class LastCompletedRecordingStoreTests
    {
        [SetUp]
        public void SetUp()
        {
            LastCompletedRecordingStore.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            LastCompletedRecordingStore.Clear();
        }

        /// <summary>
        /// What: Save, TryRead, MarkReported, and Clear keep reported and empty states consistent.
        /// </summary>
        [Test]
        public void SaveTryReadMarkReportedClear_RoundTripsReportedAndEmptyState()
        {
            VideoRecordingSnapshot snapshot = new VideoRecordingSnapshot(
                "/tmp/gameview.webm",
                1280,
                720,
                30,
                90,
                0,
                3.0,
                RecordVideoConstants.StoppedByMaxDuration,
                false,
                RecordVideoQuality.High.ToString());

            LastCompletedRecordingStore.Save(snapshot);
            LastCompletedRecording unread = LastCompletedRecordingStore.TryRead();
            Assert.That(unread.HasValue, Is.True);
            Assert.That(unread.IsReported, Is.False);
            Assert.That(unread.Snapshot.OutputPath, Is.EqualTo(snapshot.OutputPath));
            Assert.That(unread.Snapshot.Width, Is.EqualTo(snapshot.Width));
            Assert.That(unread.Snapshot.Height, Is.EqualTo(snapshot.Height));
            Assert.That(unread.Snapshot.FrameRate, Is.EqualTo(snapshot.FrameRate));
            Assert.That(unread.Snapshot.EncodedFrameCount, Is.EqualTo(snapshot.EncodedFrameCount));
            Assert.That(unread.Snapshot.SkippedFrameCount, Is.EqualTo(snapshot.SkippedFrameCount));
            Assert.That(unread.Snapshot.ElapsedSeconds, Is.EqualTo(snapshot.ElapsedSeconds).Within(0.001));
            Assert.That(unread.Snapshot.StoppedBy, Is.EqualTo(snapshot.StoppedBy));
            Assert.That(unread.Snapshot.Quality, Is.EqualTo(snapshot.Quality));

            LastCompletedRecordingStore.MarkReported();
            LastCompletedRecording reported = LastCompletedRecordingStore.TryRead();
            Assert.That(reported.HasValue, Is.True);
            Assert.That(reported.IsReported, Is.True);

            LastCompletedRecordingStore.Clear();
            LastCompletedRecording cleared = LastCompletedRecordingStore.TryRead();
            Assert.That(cleared.HasValue, Is.False);
        }
    }
}
