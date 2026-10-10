using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.ToolContracts;

using Debug = UnityEngine.Debug;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Reads, after a run and off the main thread, the compiled assemblies the run's caller-note
    /// load budget refused, so the next run finds them in the call-site cache. One backfill at a
    /// time: the next run waits for the dll in flight and the rest is dropped, as a run does with
    /// the warm-up. Stopped as well before a compile, a domain reload and a services replacement.
    /// </summary>
    internal sealed class HotReloadCallSiteBackfill
    {
        private readonly Func<string, CancellationToken, Task> _loadDll;
        private readonly object _gate = new object();

        private HotReloadCallSiteBackfillState _state = HotReloadCallSiteBackfillState.Idle;
        private CancellationTokenSource _cancellation;
        private string _cancelledBy;
        private Task _completion = Task.CompletedTask;

        internal HotReloadCallSiteBackfill(Func<string, CancellationToken, Task> loadDll)
        {
            Debug.Assert(loadDll != null, "loadDll must not be null.");
            _loadDll = loadDll;
        }

        /// <summary>Completes when a started backfill has finished; never faults.</summary>
        internal Task Completion
        {
            get
            {
                lock (_gate)
                {
                    return _completion;
                }
            }
        }

        internal HotReloadCallSiteBackfillState State
        {
            get
            {
                lock (_gate)
                {
                    return _state;
                }
            }
        }

        /// <summary>
        /// Starts reading <paramref name="dllPaths"/> on the main thread. Precondition: no backfill
        /// is running (the run calling this yielded to the previous one first). Nothing starts and
        /// nothing is logged for an empty list.
        /// </summary>
        internal void Start(IReadOnlyList<string> dllPaths, string correlationId)
        {
            Debug.Assert(dllPaths != null, "dllPaths must not be null.");
            Debug.Assert(!string.IsNullOrEmpty(correlationId), "correlationId must not be null or empty.");
            if (dllPaths.Count == 0)
            {
                return;
            }

            lock (_gate)
            {
                Debug.Assert(
                    _state == HotReloadCallSiteBackfillState.Idle,
                    "a backfill starts only after the previous one stopped.");
                // Why return (not overwrite): a second Start would drop the running cancellation
                // and completion, leaving reads nobody can stop or await. The next run refuses
                // the dropped dlls again, so nothing is lost.
                if (_state != HotReloadCallSiteBackfillState.Idle)
                {
                    return;
                }

                _state = HotReloadCallSiteBackfillState.Running;
                _cancelledBy = null;
                _cancellation = new CancellationTokenSource();
                _completion = RunAllAsync(dllPaths, correlationId, _cancellation.Token);
            }
        }

        /// <summary>
        /// Called by a run before it reads anything: stops a running backfill; the returned task
        /// completes once the dll in flight has been read. Completed at once when none runs.
        /// </summary>
        internal Task YieldToRunAsync()
        {
            lock (_gate)
            {
                return CancelRunningLocked(HotReloadConstants.WarmUpCancelledByRun);
            }
        }

        /// <summary>
        /// Stops a running backfill for <paramref name="trigger"/>; same contract as
        /// <see cref="YieldToRunAsync"/>.
        /// </summary>
        internal Task Shutdown(string trigger)
        {
            Debug.Assert(!string.IsNullOrEmpty(trigger), "trigger must not be null or empty.");
            lock (_gate)
            {
                return CancelRunningLocked(trigger);
            }
        }

        private Task CancelRunningLocked(string cancelledBy)
        {
            if (_state != HotReloadCallSiteBackfillState.Running)
            {
                return Task.CompletedTask;
            }

            // Why only the first reason is kept: it is the one that stopped the reads.
            if (_cancelledBy == null)
            {
                _cancelledBy = cancelledBy;
            }

            _cancellation.Cancel();
            return _completion;
        }

        private async Task RunAllAsync(IReadOnlyList<string> dllPaths, string correlationId, CancellationToken ct)
        {
            Stopwatch total = Stopwatch.StartNew();
            int loaded = 0;
            int failed = 0;
            try
            {
                try
                {
                    foreach (string dllPath in dllPaths)
                    {
                        // Why between dlls: a read cannot be interrupted, so a cancel takes effect
                        // before the next one, and the run that cancelled waits for this one only.
                        if (ct.IsCancellationRequested)
                        {
                            break;
                        }

                        if (await TryLoadAsync(dllPath, ct).ConfigureAwait(false))
                        {
                            loaded++;
                        }
                        else
                        {
                            failed++;
                        }
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // A load cancelled before it started read nothing; it counts as neither.
                }

                LogComplete(dllPaths.Count, loaded, failed, total.ElapsedMilliseconds, correlationId);
            }
            catch (Exception ex)
            {
                // Approved deviation: this is the boundary of work that started at the end of a run;
                // nothing above it observes the task (as in HotReloadWarmUp.RunAllAsync).
                Debug.LogException(ex);
            }
            finally
            {
                lock (_gate)
                {
                    _state = HotReloadCallSiteBackfillState.Idle;
                }
            }
        }

        // True when the dll was read (or was already cached); false when the read failed for a
        // reason the next run surfaces itself. OperationCanceledException propagates to the caller.
        private async Task<bool> TryLoadAsync(string dllPath, CancellationToken ct)
        {
            try
            {
                await _loadDll(dllPath, ct).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex) when (IsSkippableLoadException(ex))
            {
                // Approved deviation: best-effort background work; the next run reads the dll
                // itself and surfaces the real error.
                Debug.LogWarning("[UnityCliLoop] Hot reload caller-note backfill could not read a dll: " + ex.Message);
                return false;
            }
        }

        private static bool IsSkippableLoadException(Exception ex)
        {
            return ex is IOException || ex is UnauthorizedAccessException || ex is BadImageFormatException;
        }

        private void LogComplete(int requested, int loaded, int failed, long totalMs, string correlationId)
        {
            string cancelledBy;
            lock (_gate)
            {
                cancelledBy = _cancelledBy;
            }

            VibeLogger.LogInfo(
                HotReloadConstants.VibeLogCallerNoteBackfillComplete,
                "Hot reload caller-note backfill finished.",
                new { requested, loaded, failed, totalMs, cancelledBy },
                correlationId);
        }
    }

    internal enum HotReloadCallSiteBackfillState
    {
        Idle,
        Running
    }
}
