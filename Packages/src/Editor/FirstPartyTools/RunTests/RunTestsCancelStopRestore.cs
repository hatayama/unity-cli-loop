using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Injectable side effects for cancel-time Test Runner stop and PlayMode restore.
    /// </summary>
    internal sealed class RunTestsCancelStopRestoreHooks
    {
        /// <summary>
        /// Attempts to cancel an in-flight Test Runner job. Returns false when unavailable.
        /// </summary>
        public Func<string, bool> TryCancelTestRun { get; set; }

        /// <summary>
        /// True when a Test Runner job is still active. Optional; null skips EditMode run polling.
        /// </summary>
        public Func<bool> IsRunActive { get; set; }

        /// <summary>
        /// True while the Editor is in Play Mode.
        /// </summary>
        public Func<bool> IsPlaying { get; set; }

        /// <summary>
        /// Requests Play Mode exit. Must be idempotent.
        /// </summary>
        public Action RequestExitPlayMode { get; set; }

        /// <summary>
        /// Wall-clock delay used for upper-bounded polling. Callers must pass CancellationToken.None.
        /// </summary>
        public Func<int, CancellationToken, Task> DelayAsync { get; set; }

        /// <summary>
        /// Warning logger for soft failures inside the no-throw stop path.
        /// </summary>
        public Action<string> LogWarning { get; set; }
    }

    /// <summary>
    /// Outcome of cancel-time stop/restore. Used to extend agent-facing timeout messages.
    /// </summary>
    internal readonly struct RunTestsCancelStopRestoreResult
    {
        public RunTestsCancelStopRestoreResult(
            bool testRunCancelAttempted,
            bool testRunCancelSucceeded,
            bool playModeExitRequested,
            bool playModeExitConfirmed,
            bool stopWaitTimedOut,
            string degradationNote)
        {
            TestRunCancelAttempted = testRunCancelAttempted;
            TestRunCancelSucceeded = testRunCancelSucceeded;
            PlayModeExitRequested = playModeExitRequested;
            PlayModeExitConfirmed = playModeExitConfirmed;
            StopWaitTimedOut = stopWaitTimedOut;
            DegradationNote = degradationNote ?? string.Empty;
        }

        public bool TestRunCancelAttempted { get; }
        public bool TestRunCancelSucceeded { get; }
        public bool PlayModeExitRequested { get; }
        public bool PlayModeExitConfirmed { get; }
        public bool StopWaitTimedOut { get; }
        public string DegradationNote { get; }
    }

    /// <summary>
    /// Stops Test Runner work and restores Editor Play Mode after run-tests cancellation.
    /// </summary>
    internal static class RunTestsCancelStopRestore
    {
        public const int DefaultStopWaitTimeoutMilliseconds = 10000;
        public const int DefaultPollIntervalMilliseconds = 100;

        /// <summary>
        /// Performs stop/restore without observing the already-canceled execution token.
        /// Why CancellationToken.None: executionCt is already canceled when this runs; waiting on
        /// it would throw immediately and skip PlayMode restore / scope lifetime guarantees.
        /// Why never throw: callers invoke this from cancel catch paths; throwing would hide the
        /// original OperationCanceledException and leave DomainReloadDisableScope dispose timing undefined.
        /// </summary>
        public static async Task<RunTestsCancelStopRestoreResult> StopAndRestoreAsync(
            bool isPlayMode,
            string runGuid,
            RunTestsCancelStopRestoreHooks hooks,
            int stopWaitTimeoutMilliseconds = DefaultStopWaitTimeoutMilliseconds,
            int pollIntervalMilliseconds = DefaultPollIntervalMilliseconds)
        {
            Debug.Assert(hooks != null, "hooks must not be null");
            Debug.Assert(hooks.IsPlaying != null, "hooks.IsPlaying must not be null");
            Debug.Assert(hooks.RequestExitPlayMode != null, "hooks.RequestExitPlayMode must not be null");
            Debug.Assert(hooks.DelayAsync != null, "hooks.DelayAsync must not be null");
            Debug.Assert(hooks.LogWarning != null, "hooks.LogWarning must not be null");
            Debug.Assert(stopWaitTimeoutMilliseconds > 0, "stopWaitTimeoutMilliseconds must be positive");
            Debug.Assert(pollIntervalMilliseconds > 0, "pollIntervalMilliseconds must be positive");

            try
            {
                return await StopAndRestoreCoreAsync(
                    isPlayMode,
                    runGuid,
                    hooks,
                    stopWaitTimeoutMilliseconds,
                    pollIntervalMilliseconds);
            }
            catch (Exception exception)
            {
                // Why catch-all: this method is an absolute no-throw boundary for cancel paths.
                hooks.LogWarning(
                    "run-tests stop/restore failed unexpectedly and was swallowed: " + exception);
                return new RunTestsCancelStopRestoreResult(
                    testRunCancelAttempted: false,
                    testRunCancelSucceeded: false,
                    playModeExitRequested: false,
                    playModeExitConfirmed: false,
                    stopWaitTimedOut: false,
                    degradationNote:
                        "Stop/restore failed unexpectedly; Editor state may still be dirty. " +
                        "Restart with `uloop launch -r` if Unity remains stuck.");
            }
        }

        private static async Task<RunTestsCancelStopRestoreResult> StopAndRestoreCoreAsync(
            bool isPlayMode,
            string runGuid,
            RunTestsCancelStopRestoreHooks hooks,
            int stopWaitTimeoutMilliseconds,
            int pollIntervalMilliseconds)
        {
            bool testRunCancelAttempted = false;
            bool testRunCancelSucceeded = false;
            if (hooks.TryCancelTestRun != null && !string.IsNullOrEmpty(runGuid))
            {
                testRunCancelAttempted = true;
                try
                {
                    testRunCancelSucceeded = hooks.TryCancelTestRun(runGuid);
                }
                catch (Exception exception)
                {
                    hooks.LogWarning(
                        "TryCancelTestRun failed and was ignored (falling back to public stop path): " +
                        exception);
                    testRunCancelSucceeded = false;
                }
            }

            bool playModeExitRequested = false;
            bool playModeExitConfirmed = !isPlayMode || !hooks.IsPlaying();
            bool stopWaitTimedOut = false;

            if (isPlayMode && hooks.IsPlaying())
            {
                playModeExitRequested = true;
                try
                {
                    hooks.RequestExitPlayMode();
                }
                catch (Exception exception)
                {
                    hooks.LogWarning("RequestExitPlayMode failed and was ignored: " + exception);
                }

                (bool confirmed, bool timedOut) = await WaitUntilAsync(
                    () => !hooks.IsPlaying(),
                    hooks.DelayAsync,
                    stopWaitTimeoutMilliseconds,
                    pollIntervalMilliseconds);
                playModeExitConfirmed = confirmed;
                stopWaitTimedOut = timedOut;
            }
            else if (!isPlayMode && hooks.IsRunActive != null && hooks.IsRunActive())
            {
                (bool confirmed, bool timedOut) = await WaitUntilAsync(
                    () => !hooks.IsRunActive(),
                    hooks.DelayAsync,
                    stopWaitTimeoutMilliseconds,
                    pollIntervalMilliseconds);
                stopWaitTimedOut = timedOut;
                if (!confirmed)
                {
                    // EditMode has no public job-cancel API under TF 1.3.9 without Option A.
                }
            }

            string degradationNote = BuildDegradationNote(
                isPlayMode,
                testRunCancelAttempted,
                testRunCancelSucceeded,
                playModeExitRequested,
                playModeExitConfirmed,
                stopWaitTimedOut);

            return new RunTestsCancelStopRestoreResult(
                testRunCancelAttempted,
                testRunCancelSucceeded,
                playModeExitRequested,
                playModeExitConfirmed,
                stopWaitTimedOut,
                degradationNote);
        }

        private static async Task<(bool Confirmed, bool TimedOut)> WaitUntilAsync(
            Func<bool> isDone,
            Func<int, CancellationToken, Task> delayAsync,
            int timeoutMilliseconds,
            int pollIntervalMilliseconds)
        {
            int elapsedMilliseconds = 0;
            while (elapsedMilliseconds < timeoutMilliseconds)
            {
                if (isDone())
                {
                    return (true, false);
                }

                await delayAsync(pollIntervalMilliseconds, CancellationToken.None);
                elapsedMilliseconds += pollIntervalMilliseconds;
            }

            return (isDone(), true);
        }

        private static string BuildDegradationNote(
            bool isPlayMode,
            bool testRunCancelAttempted,
            bool testRunCancelSucceeded,
            bool playModeExitRequested,
            bool playModeExitConfirmed,
            bool stopWaitTimedOut)
        {
            StringBuilder note = new();
            // Why guard on PlayMode exit confirmed: once Play Mode has exited, the in-flight
            // PlayMode job is effectively stopped; warning about missing CancelTestRun only
            // confuses agents. Keep the warning for EditMode and for unconfirmed PlayMode exit.
            if ((!testRunCancelAttempted || !testRunCancelSucceeded) &&
                (!isPlayMode || !playModeExitConfirmed))
            {
                note.Append(
                    "Unity Test Framework 1.3.9 has no public API to cancel an in-flight test job");
                if (!isPlayMode)
                {
                    note.Append("; an EditMode job may still be running until it finishes");
                }
                note.Append(". ");
            }

            if (playModeExitRequested && !playModeExitConfirmed)
            {
                note.Append("Play Mode exit was requested but not confirmed within the stop wait. ");
            }
            else if (playModeExitRequested && playModeExitConfirmed)
            {
                note.Append("Play Mode exit was requested and confirmed. ");
            }

            if (stopWaitTimedOut)
            {
                note.Append("Stop-wait polling reached its upper bound. ");
            }

            note.Append("Restart with `uloop launch -r` if Unity remains stuck.");
            return note.ToString();
        }
    }

    /// <summary>
    /// OperationCanceledException that carries cancel-time stop/restore details for timeout messaging.
    /// </summary>
    internal sealed class RunTestsExecutionCanceledException : OperationCanceledException
    {
        public RunTestsExecutionCanceledException(
            CancellationToken token,
            RunTestsCancelStopRestoreResult stopResult)
            : base("run-tests execution was canceled", token)
        {
            StopResult = stopResult;
        }

        public RunTestsCancelStopRestoreResult StopResult { get; }
    }
}
