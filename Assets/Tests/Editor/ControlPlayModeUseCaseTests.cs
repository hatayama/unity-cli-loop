using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies Control Play Mode behavior without entering PlayMode.
    /// </summary>
    public sealed class ControlPlayModeUseCaseTests
    {
        [Test]
        public void ControlPlayModeSchema_WhenCreated_UsesToolReadinessSizedTimeout()
        {
            // Verifies that PlayMode waits default to the repository's long-running tool readiness window.
            ControlPlayModeSchema schema = new ControlPlayModeSchema();

            Assert.That(schema.TimeoutSeconds, Is.EqualTo(ControlPlayModeUseCase.DefaultTimeoutSeconds));
        }

        [Test]
        public async Task ExecuteAsync_WhenStatusOnly_ReturnsCurrentPlayModeState()
        {
            // Verifies that the CLI can inspect PlayMode state without changing it during post-reload waits.
            ControlPlayModeUseCase useCase = new ControlPlayModeUseCase();
            ControlPlayModeSchema schema = new ControlPlayModeSchema
            {
                StatusOnly = true,
            };

            ControlPlayModeResponse response = await useCase.ExecuteAsync(schema, CancellationToken.None);

            Assert.That(response.Message, Is.EqualTo("Play mode status"));
        }
    }
}
