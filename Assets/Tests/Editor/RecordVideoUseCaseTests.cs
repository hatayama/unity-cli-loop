using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies record-video UseCase outcomes that are reachable in Edit Mode.
    /// </summary>
    public sealed class RecordVideoUseCaseTests
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
        /// What: Start in Edit Mode fails preflight and does not start a recording.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_StartInEditMode_ReturnsPreflightFailure()
        {
            RecordVideoUseCase useCase = new RecordVideoUseCase();
            RecordVideoSchema parameters = new RecordVideoSchema
            {
                Action = RecordVideoAction.Start
            };

            RecordVideoResponse response = await useCase.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.IsRecording, Is.False);
            Assert.That(response.Message, Is.EqualTo(PlayModeToolPreflightService.PlayModeNotActiveMessage));
            Assert.That(response.Action, Is.EqualTo(RecordVideoAction.Start.ToString()));
        }

        /// <summary>
        /// What: Status with no recording and no last-completed snapshot reports idle success.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_StatusWhenIdle_ReturnsSuccessWithoutOutputPath()
        {
            RecordVideoUseCase useCase = new RecordVideoUseCase();
            RecordVideoSchema parameters = new RecordVideoSchema
            {
                Action = RecordVideoAction.Status
            };

            RecordVideoResponse response = await useCase.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.Success, Is.True);
            Assert.That(response.IsRecording, Is.False);
            Assert.That(response.OutputPath, Is.Null.Or.Empty);
            Assert.That(response.Message, Is.EqualTo(RecordVideoConstants.StatusIdleMessage));
            Assert.That(response.Action, Is.EqualTo(RecordVideoAction.Status.ToString()));
        }

        /// <summary>
        /// What: Stop with no recording and no unreported snapshot returns the no-recording failure.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_StopWhenIdle_ReturnsNoRecordingFailure()
        {
            RecordVideoUseCase useCase = new RecordVideoUseCase();
            RecordVideoSchema parameters = new RecordVideoSchema
            {
                Action = RecordVideoAction.Stop
            };

            RecordVideoResponse response = await useCase.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.IsRecording, Is.False);
            Assert.That(response.Message, Is.EqualTo(RecordVideoConstants.NoRecordingMessage));
            Assert.That(response.Action, Is.EqualTo(RecordVideoAction.Stop.ToString()));
        }

        /// <summary>
        /// What: an undefined Action is rejected instead of falling through to Start.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_UndefinedAction_ReturnsFailure()
        {
            RecordVideoUseCase useCase = new RecordVideoUseCase();
            RecordVideoSchema parameters = new RecordVideoSchema
            {
                Action = (RecordVideoAction)99
            };

            RecordVideoResponse response = await useCase.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.IsRecording, Is.False);
            Assert.That(response.Message, Is.EqualTo(RecordVideoConstants.InvalidActionMessage));
            Assert.That(response.Action, Is.EqualTo("99"));
        }
    }
}
