using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies how stop and status report the last completed recording when nothing is recording, and that a
    /// window start with invalid parameters is rejected before any window lookup. The last-recording store is
    /// set aside before each test and put back afterwards, and the tests are inconclusive while a recording runs.
    /// </summary>
    public sealed class RecordVideoUseCaseLastRecordingTests
    {
        private LastCompletedRecording _previousRecording;
        private bool _storeSetAside;

        [SetUp]
        public void SetUp()
        {
            // Stop and start act on a real recording, so the tests do not run while one is in progress. The test
            // framework still runs TearDown when SetUp fails an assumption, so TearDown restores the store only
            // when this SetUp actually set it aside; otherwise it would overwrite the live recording's store.
            _storeSetAside = false;
            Assume.That(RecordVideoService.IsRecording, Is.False);
            _previousRecording = LastCompletedRecordingTestState.TakeAndClear();
            _storeSetAside = true;
        }

        [TearDown]
        public void TearDown()
        {
            if (!_storeSetAside)
            {
                return;
            }

            LastCompletedRecordingTestState.Restore(_previousRecording);
        }

        /// <summary>
        /// Verifies stop reports an unreported last recording once, copies every snapshot field, and marks it reported.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_StopWithAnUnreportedLastRecording_ReportsItAndMarksItReported()
        {
            LastCompletedRecordingStore.Save(CreateSnapshot());

            RecordVideoResponse response = await Execute(RecordVideoAction.stop);

            Assert.That(response.Success, Is.True);
            Assert.That(response.Message, Is.EqualTo(RecordVideoConstants.StoppedMessage));
            Assert.That(response.Action, Is.EqualTo("stop"));
            AssertSnapshotFields(response);
            Assert.That(LastCompletedRecordingStore.TryRead().IsReported, Is.True);
        }

        /// <summary>
        /// Verifies stop fails when the last recording was already reported.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_StopWithAnAlreadyReportedLastRecording_ReturnsNoRecordingFailure()
        {
            LastCompletedRecordingStore.Save(CreateSnapshot());
            LastCompletedRecordingStore.MarkReported();

            RecordVideoResponse response = await Execute(RecordVideoAction.stop);

            Assert.That(response.Success, Is.False);
            Assert.That(response.Message, Is.EqualTo(RecordVideoConstants.NoRecordingMessage));
            Assert.That(response.OutputPath, Is.Null.Or.Empty);
        }

        /// <summary>
        /// Verifies status shows the last recording without marking it reported, so a later stop still reports it.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_StatusWithALastRecording_ShowsItWithoutMarkingItReported()
        {
            LastCompletedRecordingStore.Save(CreateSnapshot());

            RecordVideoResponse response = await Execute(RecordVideoAction.status);

            Assert.That(response.Success, Is.True);
            Assert.That(response.Message, Is.EqualTo(RecordVideoConstants.StatusIdleMessage));
            Assert.That(response.Action, Is.EqualTo("status"));
            AssertSnapshotFields(response);
            Assert.That(LastCompletedRecordingStore.TryRead().IsReported, Is.False);
        }

        /// <summary>
        /// Verifies a window start with an out-of-range frame rate fails with the validation message.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_StartForAWindowWithAnInvalidFrameRate_ReturnsTheValidationFailure()
        {
            RecordVideoSchema parameters = new RecordVideoSchema
            {
                Action = RecordVideoAction.start,
                WindowName = "NoSuchWindow-uloop-test",
                FrameRate = 0
            };

            RecordVideoResponse response = await new RecordVideoUseCase().ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.Message, Does.StartWith("FrameRate must be between"));
            Assert.That(response.IsRecording, Is.False);
        }

        private static Task<RecordVideoResponse> Execute(RecordVideoAction action)
        {
            return new RecordVideoUseCase().ExecuteAsync(new RecordVideoSchema { Action = action }, CancellationToken.None);
        }

        private static VideoRecordingSnapshot CreateSnapshot()
        {
            return new VideoRecordingSnapshot("<PROJECT_ROOT>/clip.mp4", 640, 360, 24, 48, 3, 2.5, "MaxDuration", false, "high");
        }

        private static void AssertSnapshotFields(RecordVideoResponse response)
        {
            Assert.That(response.IsRecording, Is.False);
            Assert.That(response.OutputPath, Is.EqualTo("<PROJECT_ROOT>/clip.mp4"));
            Assert.That(response.Width, Is.EqualTo(640));
            Assert.That(response.Height, Is.EqualTo(360));
            Assert.That(response.FrameRate, Is.EqualTo(24));
            Assert.That(response.EncodedFrameCount, Is.EqualTo(48));
            Assert.That(response.SkippedFrameCount, Is.EqualTo(3));
            Assert.That(response.ElapsedSeconds, Is.EqualTo(2.5));
            Assert.That(response.StoppedBy, Is.EqualTo("MaxDuration"));
            Assert.That(response.Quality, Is.EqualTo("high"));
        }
    }
}
