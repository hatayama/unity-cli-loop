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
    /// Loads, once per domain and before the first hot reload run, what that run would otherwise
    /// load cold: the call sites, referenced-method sets and PDB document lists of the assemblies
    /// earlier runs of this project edited. Each kind of work is one item, run in order.
    /// When a run comes while the warm-up is running, the run waits for the item in flight, whose
    /// result it then finds in the caches, and the remaining items are dropped. When a run comes
    /// before the warm-up starts, the warm-up never starts.
    /// Why no macOS activity is held while it runs: an activity is held only while a command is
    /// handled, and the warm-up is not one. The Editor is not throttled for the first half minute
    /// after a reload, and a run that comes takes an activity that speeds up the item in flight too.
    /// </summary>
    internal sealed class HotReloadWarmUp
    {
        private const string RunStartedFirstSkipReason = "run_started_first";

        private readonly IHotReloadWarmUpContextSource _contextSource;
        private readonly IReadOnlyList<IHotReloadWarmUpItem> _items;
        private readonly object _gate = new object();

        private HotReloadWarmUpState _state = HotReloadWarmUpState.NotStarted;
        private bool _runStartedFirst;
        private CancellationTokenSource _cancellation;
        private string _cancelledBy;
        private Task _completion = Task.CompletedTask;

        internal HotReloadWarmUp(IHotReloadWarmUpContextSource contextSource, IReadOnlyList<IHotReloadWarmUpItem> items)
        {
            Debug.Assert(contextSource != null, "contextSource must not be null.");
            Debug.Assert(items != null, "items must not be null.");
            _contextSource = contextSource;
            _items = items;
        }

        /// <summary>Completes when a started warm-up has finished; never faults.</summary>
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

        internal HotReloadWarmUpState State
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
        /// Starts the warm-up on the main thread. Does nothing after the first call, and skips when
        /// a run came first. Never throws.
        /// </summary>
        internal void Start()
        {
            lock (_gate)
            {
                if (_state != HotReloadWarmUpState.NotStarted)
                {
                    return;
                }

                if (!_runStartedFirst)
                {
                    _state = HotReloadWarmUpState.Running;
                    _cancellation = new CancellationTokenSource();
                    _completion = RunAllAsync(_cancellation.Token);
                    return;
                }

                _state = HotReloadWarmUpState.Finished;
            }

            LogSkipped(RunStartedFirstSkipReason);
        }

        /// <summary>
        /// Called by a run before it reads anything. Keeps a warm-up that has not started from
        /// starting, and stops a running one: the returned task completes once the item in flight
        /// has finished. Completed at once when no warm-up runs. Callable from any thread.
        /// </summary>
        internal Task YieldToRunAsync()
        {
            lock (_gate)
            {
                _runStartedFirst = true;
                return CancelRunningLocked(HotReloadConstants.WarmUpCancelledByRun);
            }
        }

        /// <summary>
        /// Stops a running warm-up for <paramref name="trigger"/>; the returned task completes once
        /// the item in flight has finished. Completed at once when no warm-up runs. A warm-up that
        /// has not started can still start afterwards.
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
            if (_state != HotReloadWarmUpState.Running)
            {
                return Task.CompletedTask;
            }

            if (_cancelledBy == null)
            {
                _cancelledBy = cancelledBy;
            }

            _cancellation.Cancel();
            return _completion;
        }

        // Starts synchronously on the main thread from Start(), so the capture reads Unity APIs there.
        private async Task RunAllAsync(CancellationToken ct)
        {
            Stopwatch total = Stopwatch.StartNew();
            try
            {
                HotReloadWarmUpCapture capture = _contextSource.Capture();
                if (capture.Context == null)
                {
                    LogSkipped(capture.SkipReason);
                    return;
                }

                List<HotReloadWarmUpItemOutcome> outcomes = new List<HotReloadWarmUpItemOutcome>();
                foreach (IHotReloadWarmUpItem item in _items)
                {
                    // Why every remaining item is listed as cancelled: the entry then shows which
                    // items never ran.
                    if (ct.IsCancellationRequested)
                    {
                        outcomes.Add(HotReloadWarmUpItemOutcome.Cancelled(item.Name));
                        continue;
                    }

                    // Why ConfigureAwait(false): the next item switches to the main thread itself,
                    // and the entry after the last item must not wait for a main thread that a
                    // reload may never give back.
                    outcomes.Add(await RunItemAsync(item, capture.Context, ct).ConfigureAwait(false));
                }

                LogComplete(capture.Context, outcomes, total.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                // Approved deviation: this is the boundary of work that started from an Editor
                // update tick; nothing above it observes the task.
                Debug.LogException(ex);
            }
            finally
            {
                lock (_gate)
                {
                    _state = HotReloadWarmUpState.Finished;
                }
            }
        }

        private static async Task<HotReloadWarmUpItemOutcome> RunItemAsync(
            IHotReloadWarmUpItem item,
            HotReloadWarmUpContext context,
            CancellationToken ct)
        {
            Stopwatch watch = Stopwatch.StartNew();
            try
            {
                // Why each item enters on the main thread: after the previous item's
                // ConfigureAwait(false) this runs on a pool thread, and an item that first touches
                // a cache's Shared instance reads Application.dataPath. Why ct is passed: a switch
                // waits for the next update tick, and one that ignored a cancel would start this
                // item on the tick after the run that cancelled it had begun.
                await MainThreadSwitcher.SwitchToMainThread(ct);
                await item.RunAsync(context, ct).ConfigureAwait(false);
                return HotReloadWarmUpItemOutcome.Done(item.Name, watch.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return HotReloadWarmUpItemOutcome.Cancelled(item.Name);
            }
            catch (Exception ex) when (IsSkippableItemException(ex))
            {
                // Approved deviation: best-effort background work. The run redoes this work and
                // surfaces the real error.
                Debug.LogWarning("[UnityCliLoop] Hot reload warm-up item '" + item.Name + "' failed: " + ex.Message);
                return HotReloadWarmUpItemOutcome.Failed(item.Name, watch.ElapsedMilliseconds, ex);
            }
        }

        private static bool IsSkippableItemException(Exception ex)
        {
            return ex is IOException || ex is UnauthorizedAccessException || ex is BadImageFormatException;
        }

        private void LogComplete(
            HotReloadWarmUpContext context,
            List<HotReloadWarmUpItemOutcome> outcomes,
            long totalMs)
        {
            string[] targets = new string[context.Targets.Count];
            for (int index = 0; index < targets.Length; index++)
            {
                targets[index] = context.Targets[index].AssemblyName;
            }

            object[] items = new object[outcomes.Count];
            for (int index = 0; index < items.Length; index++)
            {
                HotReloadWarmUpItemOutcome outcome = outcomes[index];
                items[index] = new
                {
                    name = outcome.Name,
                    ms = outcome.Ms,
                    outcome = ToOutcomeName(outcome.Kind),
                    detail = outcome.Detail
                };
            }

            string cancelledBy;
            lock (_gate)
            {
                cancelledBy = _cancelledBy;
            }

            VibeLogger.LogInfo(
                HotReloadConstants.VibeLogWarmUpComplete,
                "Hot reload warm-up finished.",
                new
                {
                    targetCount = targets.Length,
                    targets,
                    items,
                    totalMs,
                    cancelledBy
                });
        }

        private static string ToOutcomeName(HotReloadWarmUpItemOutcomeKind kind)
        {
            switch (kind)
            {
                case HotReloadWarmUpItemOutcomeKind.Done:
                    return "done";
                case HotReloadWarmUpItemOutcomeKind.Cancelled:
                    return "cancelled";
                case HotReloadWarmUpItemOutcomeKind.Failed:
                    return "failed";
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        private static void LogSkipped(string reason)
        {
            VibeLogger.LogInfo(
                HotReloadConstants.VibeLogWarmUpSkipped,
                "Hot reload warm-up skipped.",
                new { reason });
        }
    }

    internal enum HotReloadWarmUpState
    {
        NotStarted,
        Running,
        Finished
    }
}
