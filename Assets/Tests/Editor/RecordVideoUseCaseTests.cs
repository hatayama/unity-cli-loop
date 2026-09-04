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
