
namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Provides Dynamic Code Foreground Warmup State operations for its owning module.
    /// </summary>
    internal sealed class DynamicCodeForegroundWarmupStateService
    {
        private readonly object _syncRoot = new();
        private ForegroundWarmupStatus _status = ForegroundWarmupStatus.Pending;

        internal bool TryBegin()
        {
            lock (_syncRoot)
            {
                if (_status != ForegroundWarmupStatus.Pending)
                {
                    return false;
                }

                _status = ForegroundWarmupStatus.Running;
                return true;
            }
        }

        internal void MarkCompleted()
        {
            lock (_syncRoot)
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

        internal void MarkCompletedByForegroundExecution()
        {
            lock (_syncRoot)
            {
                // Why: a real foreground execution succeeding after a transient hidden-warmup miss
                // proves the user-visible path is already usable for the next request.
                // Why not insist on the hidden warmup succeeding first: that keeps Pending alive
                // after the exact success case we care about and injects another needless warmup.
                _status = ForegroundWarmupStatus.Completed;
            }
        }

        internal void ResetAfterIncompleteAttempt()
        {
            lock (_syncRoot)
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

        internal void Reset()
        {
            lock (_syncRoot)
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

    /// <summary>
    /// Stores Dynamic Code Foreground Warmup state shared by the owning workflow.
    /// </summary>
    internal static class DynamicCodeForegroundWarmupState
    {
        private static readonly DynamicCodeForegroundWarmupStateService ServiceValue =
            new DynamicCodeForegroundWarmupStateService();

        internal static bool TryBegin()
        {
            return ServiceValue.TryBegin();
        }

        internal static void MarkCompleted()
        {
            ServiceValue.MarkCompleted();
        }

        internal static void MarkCompletedByForegroundExecution()
        {
            ServiceValue.MarkCompletedByForegroundExecution();
        }

        internal static void ResetAfterIncompleteAttempt()
        {
            ServiceValue.ResetAfterIncompleteAttempt();
        }

        internal static void Reset()
        {
            ServiceValue.Reset();
        }
    }
}
