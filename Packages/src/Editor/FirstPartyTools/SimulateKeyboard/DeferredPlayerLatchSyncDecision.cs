#if ULOOP_HAS_INPUT_SYSTEM
#nullable enable
using UnityEngine.InputSystem.LowLevel;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Pure fire and one-shot consume rules for deferred player-view key latch sync.
    /// </summary>
    internal static class DeferredPlayerLatchSyncDecision
    {
        /// <summary>
        /// Decides whether this Input System update should sync stale player latches and
        /// then drop the callback. Why Editor is kept: pause only runs Editor updates, so
        /// consuming the one-shot there would skip the first player update after resume.
        /// Why not HasFlag: Default includes Editor and would look like a player update.
        /// </summary>
        internal static DeferredLatchSyncTickDecision Decide(InputUpdateType currentUpdateType)
        {
            bool isPlayerUpdate = currentUpdateType == InputUpdateType.Dynamic
                || currentUpdateType == InputUpdateType.Fixed
                || currentUpdateType == InputUpdateType.Manual;
            return new DeferredLatchSyncTickDecision(isPlayerUpdate, isPlayerUpdate);
        }
    }
}
#endif
