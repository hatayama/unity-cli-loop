using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies that out-of-range mouse UI action enum values surface as a Success=false
    /// response through SimulateMouseUiUseCase's TryFromSchema entry point instead of a throw.
    /// </summary>
    [TestFixture]
    public sealed class SimulateMouseUiActionValidationTests
    {
        [Test]
        public async Task ExecuteAsync_WithUnknownMouseUiAction_ReturnsValidationFailureResponse()
        {
            // Verifies TryFromSchema rejects an integer-cast enum value with the exact wire-visible message.
            SimulateMouseUiUseCase useCase = new();
            UnityCliLoopMouseUiAction invalidAction = (UnityCliLoopMouseUiAction)999;
            SimulateMouseUiSchema request = new()
            {
                Action = invalidAction
            };

            SimulateMouseUiResponse response = await useCase.ExecuteAsync(request, CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.Message, Is.EqualTo($"Unknown mouse UI action: {invalidAction}"));
            Assert.That(response.Action, Is.EqualTo(invalidAction.ToString()));
        }
    }
}
