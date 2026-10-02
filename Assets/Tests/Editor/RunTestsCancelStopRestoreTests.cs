using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies cancel-time Test Runner stop and Play Mode restore through scripted hooks, including the
    /// bounded wait loops and the no-throw boundary.
    /// </summary>
    public sealed class RunTestsCancelStopRestoreTests
    {
        private const string RunGuid = "run-guid";
        private const int TimeoutMilliseconds = 300;
        private const int PollIntervalMilliseconds = 100;

        private ScriptedHooks _hooks;

        [SetUp]
        public void SetUp()
        {
            _hooks = new ScriptedHooks();
        }

        /// <summary>
        /// Verifies a successful EditMode cancel with no active run reports the attempt and only the restart hint.
        /// </summary>
        [Test]
        public async Task StopAndRestoreAsync_EditModeWithASuccessfulCancel_ReturnsWithoutWarning()
        {
            _hooks.CancelResult = true;

            RunTestsCancelStopRestoreResult result = await StopAndRestore(isPlayMode: false, RunGuid);

            Assert.That(_hooks.CancelGuids, Is.EqualTo(new List<string> { RunGuid }));
            Assert.That(result.TestRunCancelAttempted, Is.True);
            Assert.That(result.TestRunCancelSucceeded, Is.True);
            Assert.That(result.PlayModeExitRequested, Is.False);
            Assert.That(result.PlayModeExitConfirmed, Is.True);
            Assert.That(result.StopWaitTimedOut, Is.False);
            Assert.That(result.DegradationNote, Is.EqualTo("Restart with `uloop launch -r` if Unity remains stuck."));
            Assert.That(_hooks.Warnings, Is.Empty);
        }

        /// <summary>
        /// Verifies a missing run GUID skips the cancel hook and explains that an EditMode job may keep running.
        /// </summary>
        [Test]
        public async Task StopAndRestoreAsync_WithoutARunGuid_SkipsTheCancelAndWarnsAboutTheEditModeJob()
        {
            RunTestsCancelStopRestoreResult result = await StopAndRestore(isPlayMode: false, string.Empty);

            Assert.That(_hooks.CancelGuids, Is.Empty);
            Assert.That(result.TestRunCancelAttempted, Is.False);
            Assert.That(
                result.DegradationNote,
                Does.StartWith("Unity Test Framework 1.3.9 has no public API to cancel an in-flight test job; " +
                               "an EditMode job may still be running until it finishes. "));
        }

        /// <summary>
        /// Verifies a throwing cancel hook is logged and treated as a failed cancel instead of escaping.
        /// </summary>
        [Test]
        public async Task StopAndRestoreAsync_WhenTheCancelHookThrows_WarnsAndFallsBack()
        {
            _hooks.CancelException = new InvalidOperationException("cancel failed");

            RunTestsCancelStopRestoreResult result = await StopAndRestore(isPlayMode: false, RunGuid);

            Assert.That(result.TestRunCancelAttempted, Is.True);
            Assert.That(result.TestRunCancelSucceeded, Is.False);
            Assert.That(_hooks.Warnings.Count, Is.EqualTo(1));
            Assert.That(_hooks.Warnings[0], Does.StartWith("TryCancelTestRun failed and was ignored"));
            Assert.That(result.DegradationNote, Does.StartWith("Unity Test Framework 1.3.9"));
        }

        /// <summary>
        /// Verifies an active EditMode run is polled until it reports inactive, without timing out.
        /// </summary>
        [Test]
        public async Task StopAndRestoreAsync_EditModeRunThatStops_PollsUntilItIsInactive()
        {
            _hooks.RunActiveAnswers.Enqueue(true);
            _hooks.RunActiveAnswers.Enqueue(true);
            _hooks.RunActiveAnswers.Enqueue(false);

            RunTestsCancelStopRestoreResult result = await StopAndRestore(isPlayMode: false, RunGuid);

            Assert.That(_hooks.Delays, Is.EqualTo(new List<int> { PollIntervalMilliseconds }));
            Assert.That(result.StopWaitTimedOut, Is.False);
        }

        /// <summary>
        /// Verifies a run that stays active is polled up to the timeout and then reported as timed out.
        /// </summary>
        [Test]
        public async Task StopAndRestoreAsync_EditModeRunThatNeverStops_TimesOutAtTheUpperBound()
        {
            _hooks.RunActiveDefault = true;

            RunTestsCancelStopRestoreResult result = await StopAndRestore(isPlayMode: false, RunGuid);

            Assert.That(
                _hooks.Delays,
                Is.EqualTo(new List<int> { PollIntervalMilliseconds, PollIntervalMilliseconds, PollIntervalMilliseconds }));
            Assert.That(result.StopWaitTimedOut, Is.True);
            Assert.That(result.DegradationNote, Does.Contain("Stop-wait polling reached its upper bound. "));
        }

        /// <summary>
        /// Verifies Play Mode is exited and confirmed, and that a confirmed exit drops the missing-cancel warning.
        /// </summary>
        [Test]
        public async Task StopAndRestoreAsync_PlayModeThatExits_RequestsAndConfirmsTheExit()
        {
            _hooks.PlayingAnswers.Enqueue(true);
            _hooks.PlayingAnswers.Enqueue(true);
            _hooks.PlayingAnswers.Enqueue(true);
            _hooks.PlayingAnswers.Enqueue(false);

            RunTestsCancelStopRestoreResult result = await StopAndRestore(isPlayMode: true, string.Empty);

            Assert.That(_hooks.ExitRequestCount, Is.EqualTo(1));
            Assert.That(result.PlayModeExitRequested, Is.True);
            Assert.That(result.PlayModeExitConfirmed, Is.True);
            Assert.That(result.StopWaitTimedOut, Is.False);
            Assert.That(
                result.DegradationNote,
                Is.EqualTo("Play Mode exit was requested and confirmed. " +
                           "Restart with `uloop launch -r` if Unity remains stuck."));
        }

        /// <summary>
        /// Verifies Play Mode that stays on is reported as unconfirmed and timed out, keeping the missing-cancel
        /// warning without the EditMode clause.
        /// </summary>
        [Test]
        public async Task StopAndRestoreAsync_PlayModeThatNeverExits_ReportsTheUnconfirmedExit()
        {
            _hooks.PlayingDefault = true;

            RunTestsCancelStopRestoreResult result = await StopAndRestore(isPlayMode: true, string.Empty);

            Assert.That(result.PlayModeExitRequested, Is.True);
            Assert.That(result.PlayModeExitConfirmed, Is.False);
            Assert.That(result.StopWaitTimedOut, Is.True);
            Assert.That(
                result.DegradationNote,
                Is.EqualTo("Unity Test Framework 1.3.9 has no public API to cancel an in-flight test job. " +
                           "Play Mode exit was requested but not confirmed within the stop wait. " +
                           "Stop-wait polling reached its upper bound. " +
                           "Restart with `uloop launch -r` if Unity remains stuck."));
        }

        /// <summary>
        /// Verifies a throwing exit request is logged and the wait still confirms the exit.
        /// </summary>
        [Test]
        public async Task StopAndRestoreAsync_WhenTheExitRequestThrows_StillWaitsForTheExit()
        {
            _hooks.ExitException = new InvalidOperationException("exit failed");
            _hooks.PlayingAnswers.Enqueue(true);
            _hooks.PlayingAnswers.Enqueue(true);
            _hooks.PlayingAnswers.Enqueue(false);

            RunTestsCancelStopRestoreResult result = await StopAndRestore(isPlayMode: true, string.Empty);

            Assert.That(_hooks.Warnings.Count, Is.EqualTo(1));
            Assert.That(_hooks.Warnings[0], Does.StartWith("RequestExitPlayMode failed and was ignored"));
            Assert.That(result.PlayModeExitConfirmed, Is.True);
        }

        /// <summary>
        /// Verifies a PlayMode run whose Editor already left Play Mode needs no exit request.
        /// </summary>
        [Test]
        public async Task StopAndRestoreAsync_PlayModeRunAlreadyStopped_DoesNotRequestAnExit()
        {
            RunTestsCancelStopRestoreResult result = await StopAndRestore(isPlayMode: true, string.Empty);

            Assert.That(_hooks.ExitRequestCount, Is.EqualTo(0));
            Assert.That(result.PlayModeExitRequested, Is.False);
            Assert.That(result.PlayModeExitConfirmed, Is.True);
            Assert.That(result.DegradationNote, Is.EqualTo("Restart with `uloop launch -r` if Unity remains stuck."));
        }

        /// <summary>
        /// Verifies an unexpected failure is logged and returned as an all-false result instead of thrown.
        /// </summary>
        [Test]
        public async Task StopAndRestoreAsync_WhenAHookThrowsOutsideItsGuard_SwallowsTheFailure()
        {
            _hooks.PlayingException = new InvalidOperationException("is-playing failed");

            RunTestsCancelStopRestoreResult result = await StopAndRestore(isPlayMode: true, string.Empty);

            Assert.That(_hooks.Warnings.Count, Is.EqualTo(1));
            Assert.That(_hooks.Warnings[0], Does.StartWith("run-tests stop/restore failed unexpectedly and was swallowed"));
            Assert.That(result.TestRunCancelAttempted, Is.False);
            Assert.That(result.PlayModeExitConfirmed, Is.False);
            Assert.That(result.DegradationNote, Does.StartWith("Stop/restore failed unexpectedly"));
        }

        /// <summary>
        /// Verifies a result built without a note never exposes null to the timeout message.
        /// </summary>
        [Test]
        public void Result_WithANullNote_ExposesAnEmptyNote()
        {
            RunTestsCancelStopRestoreResult result = new RunTestsCancelStopRestoreResult(
                true, false, true, false, true, null);

            Assert.That(result.DegradationNote, Is.Empty);
            Assert.That(result.TestRunCancelAttempted, Is.True);
            Assert.That(result.PlayModeExitRequested, Is.True);
            Assert.That(result.StopWaitTimedOut, Is.True);
        }

        private Task<RunTestsCancelStopRestoreResult> StopAndRestore(bool isPlayMode, string runGuid)
        {
            return RunTestsCancelStopRestore.StopAndRestoreAsync(
                isPlayMode,
                runGuid,
                _hooks.ToHooks(),
                TimeoutMilliseconds,
                PollIntervalMilliseconds);
        }

        private sealed class ScriptedHooks
        {
            internal bool CancelResult { get; set; }
            internal Exception CancelException { get; set; }
            internal Exception ExitException { get; set; }
            internal Exception PlayingException { get; set; }
            internal Queue<bool> PlayingAnswers { get; } = new Queue<bool>();
            internal bool PlayingDefault { get; set; }
            internal Queue<bool> RunActiveAnswers { get; } = new Queue<bool>();
            internal bool RunActiveDefault { get; set; }
            internal List<string> CancelGuids { get; } = new List<string>();
            internal List<int> Delays { get; } = new List<int>();
            internal List<string> Warnings { get; } = new List<string>();
            internal int ExitRequestCount { get; private set; }

            internal RunTestsCancelStopRestoreHooks ToHooks()
            {
                return new RunTestsCancelStopRestoreHooks
                {
                    TryCancelTestRun = TryCancelTestRun,
                    IsRunActive = () => RunActiveAnswers.Count > 0 ? RunActiveAnswers.Dequeue() : RunActiveDefault,
                    IsPlaying = IsPlaying,
                    RequestExitPlayMode = RequestExitPlayMode,
                    DelayAsync = DelayAsync,
                    LogWarning = Warnings.Add
                };
            }

            private bool TryCancelTestRun(string runGuid)
            {
                CancelGuids.Add(runGuid);
                if (CancelException != null)
                {
                    throw CancelException;
                }

                return CancelResult;
            }

            private bool IsPlaying()
            {
                if (PlayingException != null)
                {
                    throw PlayingException;
                }

                return PlayingAnswers.Count > 0 ? PlayingAnswers.Dequeue() : PlayingDefault;
            }

            private void RequestExitPlayMode()
            {
                ExitRequestCount++;
                if (ExitException != null)
                {
                    throw ExitException;
                }
            }

            private Task DelayAsync(int milliseconds, CancellationToken ct)
            {
                // Completes immediately so the bounded polling loop runs synchronously without wall-clock waits.
                Delays.Add(milliseconds);
                return Task.CompletedTask;
            }
        }
    }
}
