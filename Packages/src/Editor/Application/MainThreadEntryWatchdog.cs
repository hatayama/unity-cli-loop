using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Application
{
    /// <summary>
    /// Cancels a request that is still waiting for its first main-thread switch once the Editor
    /// main thread has stalled for BlockedStallSeconds, so the CLI gets an answer instead of a
    /// silent wait. When the CLI reports a modal dialog for a command name, the wait of a request of
    /// that name that its sender's disconnect would cancel shrinks to DialogReportedStallSeconds for as
    /// long as the main thread has not ticked since the report.
    /// Never touches a request whose tool code has started.
    /// </summary>
    internal sealed class MainThreadEntryWatchdog : IMainThreadEntryWatchdog
    {
        // Matches the CLI's stall notice threshold and compile's silent-wait warning: the longest
        // ordinary Editor work (a scene save, a small import) stays under it, a dialog does not.
        internal const double BlockedStallSeconds = 30;
        // One poll: long enough for an idle Editor's sleeping loop to tick after the switch request
        // woke it, so a stall this long with a dialog reported is the dialog and not the sleep.
        internal const double DialogReportedStallSeconds = 1;
        internal static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

        private readonly IMainThreadLivenessPort _liveness;
        private readonly Func<CancellationToken, Task> _waitOnePoll;
        // Why the wall clock and not a monotonic one: the stall comes from
        // EditorMainThreadLivenessTracker, which reads DateTime.UtcNow. Taking the report's age from the
        // same clock keeps "stall >= age" meaning "no tick since the report" even when the clock jumps.
        private readonly Func<long> _getUtcTicks;
        // Why one immutable value swapped as a whole: reports arrive off the main thread and Observe
        // reads on a pool thread, so a name and a time kept apart could be read from two reports.
        private DialogReport _dialogReport;

        internal MainThreadEntryWatchdog(
            IMainThreadLivenessPort liveness,
            Func<CancellationToken, Task> waitOnePoll,
            Func<long> getUtcTicks = null)
        {
            Debug.Assert(liveness != null, "liveness must not be null");
            Debug.Assert(waitOnePoll != null, "waitOnePoll must not be null");

            _liveness = liveness ?? throw new ArgumentNullException(nameof(liveness));
            _waitOnePoll = waitOnePoll ?? throw new ArgumentNullException(nameof(waitOnePoll));
            _getUtcTicks = getUtcTicks ?? (() => DateTime.UtcNow.Ticks);
        }

        public void ReportBlockingDialog(string toolName)
        {
            // Why an empty name is dropped: it names no request, so it must fail none.
            if (string.IsNullOrEmpty(toolName))
            {
                return;
            }

            Interlocked.Exchange(ref _dialogReport, new DialogReport(toolName, _getUtcTicks()));
        }

        // Returns true when the lease was cancelled for a blocked main thread.
        internal bool Observe(ToolExecutionLease lease)
        {
            Debug.Assert(lease != null, "lease must not be null");

            double stallSeconds = _liveness.SecondsSinceLastMainThreadTick();
            if (stallSeconds < BlockedStallSeconds && !IsStalledByReportedDialog(lease, stallSeconds))
            {
                return false;
            }

            return lease.TryCancelForBlockedMainThread(stallSeconds);
        }

        // Why compare the stall with the report's age: the stall is the time since the last tick, so it
        // is at least as old as the report exactly when the main thread has not ticked since. A tick
        // after the report means the dialog closed, and whatever stalls the main thread later is not it.
        // Why only a request of the reported name: the report's sender waits for that request's answer
        // and reads it as not run.
        // Why only a request its sender's disconnect would cancel: a request that outlives its sender,
        // such as a default compile, is the only kind the CLI may hang up on and say keeps running once
        // the dialog closes. The name cannot tell it from a same-named request whose sender still waits.
        private bool IsStalledByReportedDialog(ToolExecutionLease lease, double stallSeconds)
        {
            DialogReport report = Volatile.Read(ref _dialogReport);
            if (report == null || report.ToolName != lease.ToolName || !lease.CancellableByUser ||
                stallSeconds < DialogReportedStallSeconds)
            {
                return false;
            }

            double secondsSinceReport = (_getUtcTicks() - report.ReportedUtcTicks) / (double)TimeSpan.TicksPerSecond;
            return stallSeconds >= secondsSinceReport;
        }

        // Why the first Observe comes after the first poll: an idle Editor's loop sleeps and its stall
        // counter grows; the switch request woke it, and one poll interval is enough for it to tick.
        public async Task GuardAsync(ToolExecutionLease lease, CancellationToken stop)
        {
            Debug.Assert(lease != null, "lease must not be null");

            while (!stop.IsCancellationRequested)
            {
                try
                {
                    await _waitOnePoll(stop).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (stop.IsCancellationRequested)
                {
                    return;
                }

                if (Observe(lease))
                {
                    return;
                }
            }
        }

        // A dialog the CLI reported: the command name whose request it saw waiting, and when.
        private sealed class DialogReport
        {
            internal DialogReport(string toolName, long reportedUtcTicks)
            {
                ToolName = toolName;
                ReportedUtcTicks = reportedUtcTicks;
            }

            internal string ToolName { get; }

            internal long ReportedUtcTicks { get; }
        }
    }
}
