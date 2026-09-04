#if ULOOP_HAS_INPUT_SYSTEM
#nullable enable
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Creates wire-visible responses for replay-input outcomes that carry structured state beyond a
    /// message.
    /// </summary>
    internal static class ReplayInputResponseFactory
    {
        /// <summary>
        /// Creates the failure response for a rejected PlayMode preflight. Separate from the other
        /// failure shapes so only a genuine pre-execution refusal can claim RejectedBeforeExecution:
        /// the CLI aborts a pause-point wait on that flag.
        /// </summary>
        internal static ReplayInputResponse PreflightRejectedResult(
            ReplayInputAction action,
            PlayModeToolPreflightResult preflight)
        {
            Debug.Assert(!preflight.IsValid, "PreflightRejectedResult must only be called for a rejected preflight");
            return new ReplayInputResponse
            {
                Success = false,
                Message = preflight.ErrorMessage,
                Action = action.ToString(),
                RejectedByActivePausePointId = preflight.RejectedByActivePausePointId,
                RejectedBeforeExecution = true
            };
        }
    }
}
#endif
