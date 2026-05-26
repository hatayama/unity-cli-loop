using System;
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
        public async Task WaitUntilAsync_WhenStateIsAlreadyExpected_ReturnsTrueWithoutPolling()
        {
            // Verifies that already-completed PlayMode transitions return immediately.
            int pollCount = 0;

            bool completed = await ControlPlayModeStateWaiter.WaitUntilAsync(
                () => true,
                (CancellationToken ct) =>
                {
                    pollCount++;
                    return Task.CompletedTask;
                },
                () => 0,
                100,
                CancellationToken.None);

            Assert.That(completed, Is.True);
            Assert.That(pollCount, Is.EqualTo(0));
        }

        [Test]
        public async Task WaitUntilAsync_WhenStateChangesBeforeTimeout_ReturnsTrue()
        {
            // Verifies that pending PlayMode transitions poll until the requested state appears.
            int elapsedMilliseconds = 0;
            int pollCount = 0;

            bool completed = await ControlPlayModeStateWaiter.WaitUntilAsync(
                () => pollCount >= 2,
                (CancellationToken ct) =>
                {
                    pollCount++;
                    elapsedMilliseconds += 50;
                    return Task.CompletedTask;
                },
                () => elapsedMilliseconds,
                200,
                CancellationToken.None);

            Assert.That(completed, Is.True);
            Assert.That(pollCount, Is.EqualTo(2));
        }

        [Test]
        public async Task WaitUntilAsync_WhenStateDoesNotChangeBeforeTimeout_ReturnsFalse()
        {
            // Verifies that slow PlayMode transitions stop waiting when the configured timeout elapses.
            int elapsedMilliseconds = 0;
            int pollCount = 0;

            bool completed = await ControlPlayModeStateWaiter.WaitUntilAsync(
                () => false,
                (CancellationToken ct) =>
                {
                    pollCount++;
                    elapsedMilliseconds += 100;
                    return Task.CompletedTask;
                },
                () => elapsedMilliseconds,
                150,
                CancellationToken.None);

            Assert.That(completed, Is.False);
            Assert.That(pollCount, Is.EqualTo(2));
        }

        [Test]
        public void ControlPlayModeSchema_WhenCreated_UsesToolReadinessSizedTimeout()
        {
            // Verifies that PlayMode waits default to the repository's long-running tool readiness window.
            ControlPlayModeSchema schema = new ControlPlayModeSchema();

            Assert.That(schema.TimeoutSeconds, Is.EqualTo(ControlPlayModeUseCase.DefaultTimeoutSeconds));
        }
    }
}
