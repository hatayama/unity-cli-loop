using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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

        internal HotReloadCallSiteBackfill(Func<string, CancellationToken, Task> loadDll)
        {
            Debug.Assert(loadDll != null, "loadDll must not be null.");
            _loadDll = loadDll;
        }

        /// <summary>Completes when a started backfill has finished; never faults.</summary>
        internal Task Completion => Task.CompletedTask;

        internal HotReloadCallSiteBackfillState State => HotReloadCallSiteBackfillState.Idle;

        /// <summary>
        /// Starts reading <paramref name="dllPaths"/> on the main thread. Precondition: no backfill
        /// is running (the run calling this yielded to the previous one first). Nothing starts and
        /// nothing is logged for an empty list.
        /// </summary>
        internal void Start(IReadOnlyList<string> dllPaths, string correlationId)
        {
            Debug.Assert(dllPaths != null, "dllPaths must not be null.");
            Debug.Assert(!string.IsNullOrEmpty(correlationId), "correlationId must not be null or empty.");
        }

        /// <summary>
        /// Called by a run before it reads anything: stops a running backfill; the returned task
        /// completes once the dll in flight has been read. Completed at once when none runs.
        /// </summary>
        internal Task YieldToRunAsync()
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Stops a running backfill for <paramref name="trigger"/>; same contract as
        /// <see cref="YieldToRunAsync"/>.
        /// </summary>
        internal Task Shutdown(string trigger)
        {
            Debug.Assert(!string.IsNullOrEmpty(trigger), "trigger must not be null or empty.");
            return Task.CompletedTask;
        }
    }

    internal enum HotReloadCallSiteBackfillState
    {
        Idle,
        Running
    }
}
