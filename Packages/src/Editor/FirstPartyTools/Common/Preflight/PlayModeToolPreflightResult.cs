#nullable enable
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Outcome of a PlayMode preflight check: whether the tool may run, the wire-visible rejection
    /// message, and — when a pause point is what refused the call — that pause point's id as a
    /// separate field. The id exists on its own because callers (the CLI's --trigger diagnosis
    /// above all) have to tell "refused by the marker I am waiting on" from "refused for some other
    /// reason", and the message text is too brittle to match on.
    /// </summary>
    public class PlayModeToolPreflightResult
    {
        public bool IsValid { get; }

        /// <summary>Rejection message, empty on success. Never null, so callers can assign it to a
        /// tool response's non-nullable Message without a null check.</summary>
        public string ErrorMessage { get; }

        /// <summary>Id of the pause point holding PlayMode paused, null for every other outcome.</summary>
        public string? RejectedByActivePausePointId { get; }

        private PlayModeToolPreflightResult(bool isValid, string errorMessage, string? rejectedByActivePausePointId)
        {
            IsValid = isValid;
            ErrorMessage = errorMessage;
            RejectedByActivePausePointId = rejectedByActivePausePointId;
        }

        /// <summary>Creates the success outcome.</summary>
        public static PlayModeToolPreflightResult Success()
        {
            return new PlayModeToolPreflightResult(true, string.Empty, null);
        }

        /// <summary>Creates a rejection that has no pause point behind it.</summary>
        public static PlayModeToolPreflightResult Failure(string errorMessage)
        {
            Debug.Assert(!string.IsNullOrEmpty(errorMessage), "errorMessage must not be null or empty");
            return new PlayModeToolPreflightResult(false, errorMessage, null);
        }

        /// <summary>Creates a rejection caused by an active pause point, recording which one.</summary>
        public static PlayModeToolPreflightResult FailureRejectedByPausePoint(
            string errorMessage,
            string pausePointId)
        {
            Debug.Assert(!string.IsNullOrEmpty(errorMessage), "errorMessage must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(pausePointId), "pausePointId must not be null or empty");
            return new PlayModeToolPreflightResult(false, errorMessage, pausePointId);
        }
    }
}
