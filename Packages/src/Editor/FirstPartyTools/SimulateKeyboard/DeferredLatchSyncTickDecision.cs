#if ULOOP_HAS_INPUT_SYSTEM
#nullable enable

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Outcome of one deferred latch-sync callback tick: whether to run the player-view
    /// sync, and whether to consume the one-shot registration.
    /// </summary>
    internal readonly struct DeferredLatchSyncTickDecision
    {
        internal DeferredLatchSyncTickDecision(bool shouldSync, bool shouldUnsubscribe)
        {
            ShouldSync = shouldSync;
            ShouldUnsubscribe = shouldUnsubscribe;
        }

        internal bool ShouldSync { get; }

        internal bool ShouldUnsubscribe { get; }
    }
}
#endif
