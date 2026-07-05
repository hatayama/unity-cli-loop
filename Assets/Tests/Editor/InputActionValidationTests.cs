#if ULOOP_HAS_INPUT_SYSTEM
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies that out-of-range action enum values reaching record/replay use cases
    /// surface as Success=false responses rather than JSON-RPC errors.
    /// </summary>
    [TestFixture]
    public sealed class InputActionValidationTests
    {
        [Test]
        public async Task ReplayInputAsync_WithUnknownAction_ReturnsValidationFailureResponse()
        {
            // Verifies invalid replay-input action enum values do not escape as an exception.
            ReplayInputUseCase useCase = new();
            UnityCliLoopReplayInputRequest request = new()
            {
                Action = (ReplayInputAction)999
            };

            UnityCliLoopReplayInputResult result =
                await useCase.ReplayInputAsync(request, CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Does.Contain("Unknown replay-input action"));
            Assert.That(result.Action, Is.EqualTo(request.Action.ToString()));
        }

        [Test]
        public async Task RecordInputAsync_WithUnknownAction_ReturnsValidationFailureResponse()
        {
            // Verifies invalid record-input action enum values do not escape as an exception.
            RecordInputUseCase useCase = new();
            UnityCliLoopRecordInputRequest request = new()
            {
                Action = (RecordInputAction)999
            };

            UnityCliLoopRecordInputResult result =
                await useCase.RecordInputAsync(request, CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Does.Contain("Unknown record-input action"));
            Assert.That(result.Action, Is.EqualTo(request.Action.ToString()));
        }
    }
}
#endif
