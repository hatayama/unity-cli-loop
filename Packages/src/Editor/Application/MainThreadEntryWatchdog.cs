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
    /// silent wait. Never touches a request whose tool code has started.
    /// </summary>
    internal sealed class MainThreadEntryWatchdog : IMainThreadEntryWatchdog
    {
        // Matches the CLI's stall notice threshold and compile's silent-wait warning: the longest
        // ordinary Editor work (a scene save, a small import) stays under it, a dialog does not.
        internal const double BlockedStallSeconds = 30;
        internal static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

        private readonly IMainThreadLivenessPort _liveness;
        private readonly Func<CancellationToken, Task> _waitOnePoll;

        internal MainThreadEntryWatchdog(IMainThreadLivenessPort liveness, Func<CancellationToken, Task> waitOnePoll)
        {
            Debug.Assert(liveness != null, "liveness must not be null");
            Debug.Assert(waitOnePoll != null, "waitOnePoll must not be null");

            _liveness = liveness ?? throw new ArgumentNullException(nameof(liveness));
            _waitOnePoll = waitOnePoll ?? throw new ArgumentNullException(nameof(waitOnePoll));
        }

        // Returns true when the lease was cancelled for a blocked main thread.
        internal bool Observe(ToolExecutionLease lease)
        {
            Debug.Assert(lease != null, "lease must not be null");

            double stallSeconds = _liveness.SecondsSinceLastMainThreadTick();
            if (stallSeconds < BlockedStallSeconds)
            {
                return false;
            }

            return lease.TryCancelForBlockedMainThread(stallSeconds);
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
