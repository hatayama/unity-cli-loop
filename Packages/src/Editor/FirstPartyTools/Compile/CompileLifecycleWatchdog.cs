using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
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
        private readonly Action<string> _onCancelled;
        private readonly Func<int> _getAssemblyFinishedCount;
        private readonly Func<double> _getMonotonicSeconds;
        private readonly Action<int> _onAssemblyProgressStalled;
        private int _assemblyProgressStallBaselineCount;
        private double _assemblyProgressStallAnchorSeconds;
        private bool _assemblyProgressStallMeasurementStarted;
        private bool _assemblyProgressStallWarned;

        internal CompileLifecycleWatchdog(
            Func<bool> isEditorCompiling,
            Func<bool> isRequestCompleted,
            Func<Task> waitForPollAsync,
            Action<int> onCompileStartedObserved,
            Action<int> onStartTimeout,
            Action<int> onMissedCompletionCallback,
            Action<string> onCancelled,
            Func<int> getAssemblyFinishedCount,
            Func<double> getMonotonicSeconds,
            Action<int> onAssemblyProgressStalled)
        {
            Debug.Assert(isEditorCompiling != null, "isEditorCompiling must not be null");
            Debug.Assert(isRequestCompleted != null, "isRequestCompleted must not be null");
            Debug.Assert(waitForPollAsync != null, "waitForPollAsync must not be null");
            Debug.Assert(onCompileStartedObserved != null, "onCompileStartedObserved must not be null");
            Debug.Assert(onStartTimeout != null, "onStartTimeout must not be null");
            Debug.Assert(onMissedCompletionCallback != null, "onMissedCompletionCallback must not be null");
            Debug.Assert(onCancelled != null, "onCancelled must not be null");
            Debug.Assert(getAssemblyFinishedCount != null, "getAssemblyFinishedCount must not be null");
            Debug.Assert(getMonotonicSeconds != null, "getMonotonicSeconds must not be null");
            Debug.Assert(onAssemblyProgressStalled != null, "onAssemblyProgressStalled must not be null");

            _isEditorCompiling = isEditorCompiling ?? throw new ArgumentNullException(nameof(isEditorCompiling));
            _isRequestCompleted = isRequestCompleted ?? throw new ArgumentNullException(nameof(isRequestCompleted));
            _waitForPollAsync = waitForPollAsync ?? throw new ArgumentNullException(nameof(waitForPollAsync));
            _onCompileStartedObserved = onCompileStartedObserved ?? throw new ArgumentNullException(nameof(onCompileStartedObserved));
            _onStartTimeout = onStartTimeout ?? throw new ArgumentNullException(nameof(onStartTimeout));
            _onMissedCompletionCallback = onMissedCompletionCallback ?? throw new ArgumentNullException(nameof(onMissedCompletionCallback));
            _onCancelled = onCancelled ?? throw new ArgumentNullException(nameof(onCancelled));
            _getAssemblyFinishedCount = getAssemblyFinishedCount ??
                throw new ArgumentNullException(nameof(getAssemblyFinishedCount));
            _getMonotonicSeconds = getMonotonicSeconds ?? throw new ArgumentNullException(nameof(getMonotonicSeconds));
            _onAssemblyProgressStalled = onAssemblyProgressStalled ??
                throw new ArgumentNullException(nameof(onAssemblyProgressStalled));
        }

        /// <summary>
        /// Observes compile start and finish transitions for a single compile request.
        /// </summary>
        internal async Task WatchAsync(CancellationToken ct)
        {
            bool observedStart = false;
            int waitedForStartMs = 0;
            int stoppedAfterStartMs = 0;

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
                        stoppedAfterStartMs = 0;
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

                NotifyAssemblyProgressStallIfNeeded(observedStart, isEditorCompiling);

                // The delay is not cancelled directly because the watchdog must convert cancellation into cleanup.
                await _waitForPollAsync().ConfigureAwait(false);
                // Why CancellationToken.None: a cancelled ct would throw OCE here and skip the
                // loop-head _onCancelled conversion; cleanup must still run on the main thread.
                await MainThreadSwitcher.SwitchToMainThread(CancellationToken.None);
                if (!observedStart)
                {
                    waitedForStartMs += UnityCliLoopConstants.COMPILE_START_POLL_INTERVAL_MS;
                }
            }
        }

        /// <summary>
        /// Warns once when assembly callbacks stop arriving while Unity still reports compiling.
        /// Watch continues so a later compilationFinished can still complete the request.
        /// </summary>
        private void NotifyAssemblyProgressStallIfNeeded(bool observedStart, bool isEditorCompiling)
        {
            if (!observedStart)
            {
                return;
            }

            int assemblyFinishedCount = _getAssemblyFinishedCount();
            if (assemblyFinishedCount < 1)
            {
                return;
            }

            if (!_assemblyProgressStallMeasurementStarted ||
                assemblyFinishedCount != _assemblyProgressStallBaselineCount)
            {
                _assemblyProgressStallMeasurementStarted = true;
                _assemblyProgressStallBaselineCount = assemblyFinishedCount;
                _assemblyProgressStallAnchorSeconds = _getMonotonicSeconds();
                _assemblyProgressStallWarned = false;
                return;
            }

            if (!isEditorCompiling || _assemblyProgressStallWarned)
            {
                return;
            }

            int stalledMs = (int)((_getMonotonicSeconds() - _assemblyProgressStallAnchorSeconds) * 1000.0);
            if (stalledMs < UnityCliLoopConstants.COMPILE_ASSEMBLY_PROGRESS_STALL_WARNING_MS)
            {
                return;
            }

            _assemblyProgressStallWarned = true;
            _onAssemblyProgressStalled(stalledMs);
        }
    }
}
