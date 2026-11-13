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
    /// silent wait. When the CLI reports a modal dialog, the wait shrinks to
    /// DialogReportedStallSeconds for as long as the main thread has not ticked since the report.
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
        private const long NoDialogReport = long.MinValue;

        private readonly IMainThreadLivenessPort _liveness;
        private readonly Func<CancellationToken, Task> _waitOnePoll;
        private readonly Func<long> _getTimestamp;
        private long _dialogReportedTimestamp = NoDialogReport;

        internal MainThreadEntryWatchdog(
            IMainThreadLivenessPort liveness,
            Func<CancellationToken, Task> waitOnePoll,
            Func<long> getTimestamp = null)
        {
            Debug.Assert(liveness != null, "liveness must not be null");
            Debug.Assert(waitOnePoll != null, "waitOnePoll must not be null");

            _liveness = liveness ?? throw new ArgumentNullException(nameof(liveness));
            _waitOnePoll = waitOnePoll ?? throw new ArgumentNullException(nameof(waitOnePoll));
            _getTimestamp = getTimestamp ?? Stopwatch.GetTimestamp;
        }

        public void ReportBlockingDialog()
        {
            Interlocked.Exchange(ref _dialogReportedTimestamp, _getTimestamp());
        }

        // Returns true when the lease was cancelled for a blocked main thread.
        internal bool Observe(ToolExecutionLease lease)
        {
            Debug.Assert(lease != null, "lease must not be null");

            double stallSeconds = _liveness.SecondsSinceLastMainThreadTick();
            if (stallSeconds < BlockedStallSeconds && !IsStalledByReportedDialog(stallSeconds))
            {
                return false;
            }

            return lease.TryCancelForBlockedMainThread(stallSeconds);
        }

        // Why compare the stall with the report's age: the stall is the time since the last tick, so it
        // is at least as old as the report exactly when the main thread has not ticked since. A tick
        // after the report means the dialog closed, and whatever stalls the main thread later is not it.
        private bool IsStalledByReportedDialog(double stallSeconds)
        {
            long reportedTimestamp = Interlocked.Read(ref _dialogReportedTimestamp);
            if (reportedTimestamp == NoDialogReport || stallSeconds < DialogReportedStallSeconds)
            {
                return false;
            }

            double secondsSinceReport = (_getTimestamp() - reportedTimestamp) / (double)Stopwatch.Frequency;
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
    }
}
