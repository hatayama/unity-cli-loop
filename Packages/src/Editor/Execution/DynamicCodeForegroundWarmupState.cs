namespace io.github.hatayama.uLoopMCP
{
    internal static class DynamicCodeForegroundWarmupState
    {
        private static readonly object SyncRoot = new();
        private static ForegroundWarmupStatus _status = ForegroundWarmupStatus.Pending;

        internal static bool TryBegin()
        {
            lock (SyncRoot)
            {
                if (_status != ForegroundWarmupStatus.Pending)
                {
                    return false;
                }

                _status = ForegroundWarmupStatus.Running;
                return true;
            }
        }

        internal static void MarkCompleted()
        {
            lock (SyncRoot)
            {
                if (_status == ForegroundWarmupStatus.Completed)
                {
                    return;
                }

                System.Diagnostics.Debug.Assert(
                    _status == ForegroundWarmupStatus.Running,
                    "Foreground warmup must only complete after it has started.");
                _status = ForegroundWarmupStatus.Completed;
            }
        }

        internal static void MarkCompletedByBackgroundWarmup()
        {
            lock (SyncRoot)
            {
                // Why: startup and recovery already run their own hidden prewarm before users see the
                // first foreground request, so that successful background path should satisfy the
                // one-shot fallback as well.
                // Why not force the foreground fallback to run anyway: that makes warmed startups pay
                // a second hidden execute-dynamic-code request and pushes the first user-visible call
                // back into the hundreds of milliseconds we already worked to remove.
                _status = ForegroundWarmupStatus.Completed;
            }
        }

        internal static void MarkCompletedByForegroundExecution()
        {
            lock (SyncRoot)
            {
                // Why: a real foreground execution succeeding after a transient hidden-warmup miss
                // proves the user-visible path is already usable for the next request.
                // Why not insist on the hidden warmup succeeding first: that keeps Pending alive
                // after the exact success case we care about and injects another needless warmup.
                _status = ForegroundWarmupStatus.Completed;
            }
        }

        internal static void ResetAfterIncompleteAttempt()
        {
            lock (SyncRoot)
            {
                // Why: a failed hidden warmup leaves the next foreground execution cold, so the next
                // request still needs one more chance to pay that startup tax before users do.
                // Why not mark every attempt as completed: that would permanently skip the fallback
                // after a transient startup failure and bring back the slow first real execution.
                if (_status == ForegroundWarmupStatus.Completed)
                {
                    return;
                }

                _status = ForegroundWarmupStatus.Pending;
            }
        }

        internal static void Reset()
        {
            lock (SyncRoot)
            {
                _status = ForegroundWarmupStatus.Pending;
            }
        }

        private enum ForegroundWarmupStatus
        {
            Pending,
            Running,
            Completed
        }
    }
}
