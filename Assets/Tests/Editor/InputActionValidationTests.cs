#if ULOOP_HAS_INPUT_SYSTEM
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies that out-of-range action enum values reaching the replay use case
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
            ReplayInputSchema request = new()
            {
                Action = (ReplayInputAction)999
            };

            ReplayInputResponse result =
                await useCase.ReplayInputAsync(request, CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Does.Contain("Unknown replay-input action"));
            Assert.That(result.Action, Is.EqualTo(request.Action.ToString()));
        }

    }
}
#endif
