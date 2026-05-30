using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Describes one compile lifecycle polling snapshot for diagnostics.
    /// </summary>
    internal readonly struct CompileLifecycleWatchdogSnapshot
    {
        internal CompileLifecycleWatchdogSnapshot(
            int waitedForStartMs,
            int waitedAfterStartMs,
            int stoppedAfterStartMs,
            bool observedStart,
            bool editorCompiling)
        {
            WaitedForStartMs = waitedForStartMs;
            WaitedAfterStartMs = waitedAfterStartMs;
            StoppedAfterStartMs = stoppedAfterStartMs;
            ObservedStart = observedStart;
            EditorCompiling = editorCompiling;
        }

        internal int WaitedForStartMs { get; }
        internal int WaitedAfterStartMs { get; }
        internal int StoppedAfterStartMs { get; }
        internal bool ObservedStart { get; }
        internal bool EditorCompiling { get; }
    }

    /// <summary>
    /// Watches one Unity compile request so the controller can recover when Unity misses lifecycle callbacks.
    /// </summary>
    internal sealed class CompileLifecycleWatchdog
    {
        private readonly Func<bool> _isEditorCompiling;
        private readonly Func<bool> _isRequestCompleted;
        private readonly Func<Task> _waitForPollAsync;
        private readonly Action<int> _onCompileStartedObserved;
        private readonly Action<int> _onStartTimeout;
        private readonly Action<int> _onMissedCompletionCallback;
        private readonly Action<CompileLifecycleWatchdogSnapshot> _onStillWaiting;
        private readonly Action<string> _onCancelled;

        internal CompileLifecycleWatchdog(
            Func<bool> isEditorCompiling,
            Func<bool> isRequestCompleted,
            Func<Task> waitForPollAsync,
            Action<int> onCompileStartedObserved,
            Action<int> onStartTimeout,
            Action<int> onMissedCompletionCallback,
            Action<CompileLifecycleWatchdogSnapshot> onStillWaiting,
            Action<string> onCancelled)
        {
            Debug.Assert(isEditorCompiling != null, "isEditorCompiling must not be null");
            Debug.Assert(isRequestCompleted != null, "isRequestCompleted must not be null");
            Debug.Assert(waitForPollAsync != null, "waitForPollAsync must not be null");
            Debug.Assert(onCompileStartedObserved != null, "onCompileStartedObserved must not be null");
            Debug.Assert(onStartTimeout != null, "onStartTimeout must not be null");
            Debug.Assert(onMissedCompletionCallback != null, "onMissedCompletionCallback must not be null");
            Debug.Assert(onStillWaiting != null, "onStillWaiting must not be null");
            Debug.Assert(onCancelled != null, "onCancelled must not be null");

            _isEditorCompiling = isEditorCompiling ?? throw new ArgumentNullException(nameof(isEditorCompiling));
            _isRequestCompleted = isRequestCompleted ?? throw new ArgumentNullException(nameof(isRequestCompleted));
            _waitForPollAsync = waitForPollAsync ?? throw new ArgumentNullException(nameof(waitForPollAsync));
            _onCompileStartedObserved = onCompileStartedObserved ?? throw new ArgumentNullException(nameof(onCompileStartedObserved));
            _onStartTimeout = onStartTimeout ?? throw new ArgumentNullException(nameof(onStartTimeout));
            _onMissedCompletionCallback = onMissedCompletionCallback ?? throw new ArgumentNullException(nameof(onMissedCompletionCallback));
            _onStillWaiting = onStillWaiting ?? throw new ArgumentNullException(nameof(onStillWaiting));
            _onCancelled = onCancelled ?? throw new ArgumentNullException(nameof(onCancelled));
        }

        /// <summary>
        /// Observes compile start and finish transitions for a single compile request.
        /// </summary>
        internal async Task WatchAsync(CancellationToken ct)
        {
            bool observedStart = false;
            int waitedForStartMs = 0;
            int waitedAfterStartMs = 0;
            int stoppedAfterStartMs = 0;
            int nextDiagnosticLogMs = UnityCliLoopConstants.COMPILE_WAIT_DIAGNOSTIC_LOG_INTERVAL_MS;

            while (!_isRequestCompleted())
            {
                if (ct.IsCancellationRequested)
                {
                    string reason = observedStart
                        ? "Compilation request was cancelled before Unity reported completion."
                        : "Compilation request was cancelled before it started.";
                    _onCancelled(reason);
                    return;
                }

                bool isEditorCompiling = _isEditorCompiling();
                if (!observedStart)
                {
                    if (isEditorCompiling)
                    {
                        observedStart = true;
                        waitedAfterStartMs = 0;
                        stoppedAfterStartMs = 0;
                        nextDiagnosticLogMs = UnityCliLoopConstants.COMPILE_WAIT_DIAGNOSTIC_LOG_INTERVAL_MS;
                        _onCompileStartedObserved(waitedForStartMs);
                    }
                    else if (waitedForStartMs >= UnityCliLoopConstants.COMPILE_START_TIMEOUT_MS)
                    {
                        if (!_isRequestCompleted())
                        {
                            _onStartTimeout(waitedForStartMs);
                        }
                        return;
                    }
                }
                else if (isEditorCompiling)
                {
                    stoppedAfterStartMs = 0;
                }
                else
                {
                    stoppedAfterStartMs += UnityCliLoopConstants.COMPILE_START_POLL_INTERVAL_MS;
                    if (stoppedAfterStartMs >= UnityCliLoopConstants.COMPILE_FINISH_MISSED_CALLBACK_GRACE_MS)
                    {
                        if (!_isRequestCompleted())
                        {
                            _onMissedCompletionCallback(stoppedAfterStartMs);
                        }
                        return;
                    }
                }

                // The delay is not cancelled directly because the watchdog must convert cancellation into cleanup.
                await _waitForPollAsync();
                if (!observedStart)
                {
                    waitedForStartMs += UnityCliLoopConstants.COMPILE_START_POLL_INTERVAL_MS;
                }
                else
                {
                    waitedAfterStartMs += UnityCliLoopConstants.COMPILE_START_POLL_INTERVAL_MS;
                }

                if (!_isRequestCompleted() &&
                    ShouldReportStillWaiting(
                        observedStart,
                        waitedForStartMs,
                        waitedAfterStartMs,
                        nextDiagnosticLogMs))
                {
                    _onStillWaiting(new CompileLifecycleWatchdogSnapshot(
                        waitedForStartMs,
                        waitedAfterStartMs,
                        stoppedAfterStartMs,
                        observedStart,
                        isEditorCompiling));
                    nextDiagnosticLogMs += UnityCliLoopConstants.COMPILE_WAIT_DIAGNOSTIC_LOG_INTERVAL_MS;
                }
            }
        }

        private static bool ShouldReportStillWaiting(
            bool observedStart,
            int waitedForStartMs,
            int waitedAfterStartMs,
            int nextDiagnosticLogMs)
        {
            int waitedMs = observedStart ? waitedAfterStartMs : waitedForStartMs;
            return waitedMs >= nextDiagnosticLogMs;
        }
    }
}
