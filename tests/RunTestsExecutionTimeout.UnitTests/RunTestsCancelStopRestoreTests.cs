using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.UnitTests
{
    /// <summary>
    /// Pure unit coverage for cancel-time Test Runner stop and PlayMode restore orchestration.
    /// </summary>
    [TestFixture]
    public class RunTestsCancelStopRestoreTests
    {
        [Test]
        public async Task StopAndRestoreAsync_WhenPlayModeIsActive_ShouldExitThenConfirmBeforeReturning()
        {
            // Verifies PlayMode cancel path requests exit and waits until not playing.
            int playingChecks = 0;
            bool exitRequested = false;
            RecordingHooks hooks = new(
                isPlaying: () =>
                {
                    playingChecks++;
                    return !exitRequested;
                },
                requestExitPlayMode: () => exitRequested = true);

            RunTestsCancelStopRestoreResult result = await RunTestsCancelStopRestore.StopAndRestoreAsync(
                isPlayMode: true,
                runGuid: null,
                hooks.Create(),
                stopWaitTimeoutMilliseconds: 1000,
                pollIntervalMilliseconds: 1);

            Assert.That(exitRequested, Is.True);
            Assert.That(result.PlayModeExitRequested, Is.True);
            Assert.That(result.PlayModeExitConfirmed, Is.True);
            Assert.That(result.StopWaitTimedOut, Is.False);
            Assert.That(playingChecks, Is.GreaterThan(0));
            Assert.That(result.DegradationNote, Does.Contain("Play Mode exit was requested and confirmed"));
        }

        [Test]
        public async Task StopAndRestoreAsync_WhenPlayModeExitNeverConfirms_ShouldTimeOutWithoutThrowing()
        {
            // Verifies the upper bound frees cancel handling when PlayMode exit never completes.
            RecordingHooks hooks = new(
                isPlaying: () => true,
                requestExitPlayMode: () => { });

            RunTestsCancelStopRestoreResult result = await RunTestsCancelStopRestore.StopAndRestoreAsync(
                isPlayMode: true,
                runGuid: null,
                hooks.Create(),
                stopWaitTimeoutMilliseconds: 5,
                pollIntervalMilliseconds: 1);

            Assert.That(result.PlayModeExitRequested, Is.True);
            Assert.That(result.PlayModeExitConfirmed, Is.False);
            Assert.That(result.StopWaitTimedOut, Is.True);
            Assert.That(result.DegradationNote, Does.Contain("not confirmed"));
        }

        [Test]
        public async Task StopAndRestoreAsync_WhenTryCancelThrows_ShouldFallBackToPlayModeExit()
        {
            // Verifies Option A style cancel failures degrade to the public PlayMode exit path.
            bool exitRequested = false;
            RecordingHooks hooks = new(
                isPlaying: () => !exitRequested,
                requestExitPlayMode: () => exitRequested = true,
                tryCancelTestRun: _ => throw new InvalidOperationException("lookup failed"));

            RunTestsCancelStopRestoreResult result = await RunTestsCancelStopRestore.StopAndRestoreAsync(
                isPlayMode: true,
                runGuid: "run-guid",
                hooks.Create(),
                stopWaitTimeoutMilliseconds: 1000,
                pollIntervalMilliseconds: 1);

            Assert.That(result.TestRunCancelAttempted, Is.True);
            Assert.That(result.TestRunCancelSucceeded, Is.False);
            Assert.That(exitRequested, Is.True);
            Assert.That(result.PlayModeExitConfirmed, Is.True);
            Assert.That(hooks.Warnings.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task StopAndRestoreAsync_WhenDelayThrows_ShouldReturnDegradedResultWithoutThrowing()
        {
            // Verifies the no-throw contract: unexpected hook failures become degradation notes.
            RecordingHooks hooks = new(
                isPlaying: () => true,
                requestExitPlayMode: () => { },
                delayAsync: (_, _) => throw new InvalidOperationException("delay failed"));

            RunTestsCancelStopRestoreResult result = await RunTestsCancelStopRestore.StopAndRestoreAsync(
                isPlayMode: true,
                runGuid: null,
                hooks.Create(),
                stopWaitTimeoutMilliseconds: 1000,
                pollIntervalMilliseconds: 1);

            Assert.That(result.DegradationNote, Does.Contain("failed unexpectedly"));
            Assert.That(hooks.Warnings.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task StopAndRestoreAsync_WhenEditModeAndCancelUnavailable_ShouldSkipPlayModeExit()
        {
            // Verifies EditMode Option B path does not touch PlayMode and documents job-stop limits.
            bool exitRequested = false;
            RecordingHooks hooks = new(
                isPlaying: () => false,
                requestExitPlayMode: () => exitRequested = true);

            RunTestsCancelStopRestoreResult result = await RunTestsCancelStopRestore.StopAndRestoreAsync(
                isPlayMode: false,
                runGuid: null,
                hooks.Create());

            Assert.That(exitRequested, Is.False);
            Assert.That(result.PlayModeExitRequested, Is.False);
            Assert.That(result.DegradationNote, Does.Contain("no public API"));
            Assert.That(result.DegradationNote, Does.Contain("EditMode"));
        }

        [Test]
        public async Task StopAndRestoreAsync_ShouldNotObserveAlreadyCanceledTokensForInternalWaits()
        {
            // Verifies internal polling uses CancellationToken.None rather than a canceled execution token.
            using CancellationTokenSource canceled = new();
            canceled.Cancel();
            CancellationToken observedToken = CancellationToken.None;
            bool exitRequested = false;
            RecordingHooks hooks = new(
                isPlaying: () => !exitRequested,
                requestExitPlayMode: () => exitRequested = true,
                delayAsync: (milliseconds, ct) =>
                {
                    observedToken = ct;
                    ct.ThrowIfCancellationRequested();
                    return Task.CompletedTask;
                });

            await RunTestsCancelStopRestore.StopAndRestoreAsync(
                isPlayMode: true,
                runGuid: null,
                hooks.Create(),
                stopWaitTimeoutMilliseconds: 1000,
                pollIntervalMilliseconds: 1);

            Assert.That(observedToken.IsCancellationRequested, Is.False);
            Assert.That(canceled.IsCancellationRequested, Is.True);
        }

        private sealed class RecordingHooks
        {
            private readonly Func<bool> _isPlaying;
            private readonly Action _requestExitPlayMode;
            private readonly Func<string, bool> _tryCancelTestRun;
            private readonly Func<int, CancellationToken, Task> _delayAsync;

            public RecordingHooks(
                Func<bool> isPlaying,
                Action requestExitPlayMode,
                Func<string, bool> tryCancelTestRun = null,
                Func<int, CancellationToken, Task> delayAsync = null)
            {
                _isPlaying = isPlaying;
                _requestExitPlayMode = requestExitPlayMode;
                _tryCancelTestRun = tryCancelTestRun;
                _delayAsync = delayAsync ?? ((_, _) => Task.CompletedTask);
            }

            public System.Collections.Generic.List<string> Warnings { get; } = new();

            public RunTestsCancelStopRestoreHooks Create()
            {
                return new RunTestsCancelStopRestoreHooks
                {
                    TryCancelTestRun = _tryCancelTestRun,
                    IsRunActive = null,
                    IsPlaying = _isPlaying,
                    RequestExitPlayMode = _requestExitPlayMode,
                    DelayAsync = _delayAsync,
                    LogWarning = warning => Warnings.Add(warning)
                };
            }
        }
    }
}
