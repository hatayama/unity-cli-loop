#nullable enable
using UnityEditor;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Provides shared PlayMode preflight validation for first-party tools that require Unity to be in PlayMode.
    /// </summary>
    public static class PlayModeToolPreflightService
    {
        // The not-active message is a wire-visible tool response, so callers must reproduce it byte-for-byte.
        public const string PlayModeNotActiveMessage =
            "PlayMode is not active. Use control-play-mode tool to start PlayMode first.";

        /// <summary>
        /// Fails when PlayMode is not active. Use for tools that tolerate paused PlayMode.
        /// </summary>
        public static ValidationResult RequireActive()
        {
            if (!EditorApplication.isPlaying)
            {
                return ValidationResult.Failure(PlayModeNotActiveMessage);
            }

            return ValidationResult.Success();
        }

        /// <summary>
        /// Fails when PlayMode is not active, or is active but paused. The paused-message suffix
        /// describes the blocked action in the caller's vocabulary (for example "recording input").
        /// </summary>
        public static ValidationResult RequireActiveAndNotPaused(string pausedActionDescription)
        {
            Debug.Assert(!string.IsNullOrEmpty(pausedActionDescription), "pausedActionDescription must not be null or empty");

            if (!EditorApplication.isPlaying)
            {
                return ValidationResult.Failure(PlayModeNotActiveMessage);
            }

            if (EditorApplication.isPaused)
            {
                string activePausePointId = UloopPausePointRegistry.GetActivePausePointId();
                string message = string.IsNullOrEmpty(activePausePointId)
                    ? FormatPausedMessage(pausedActionDescription)
                    : FormatPausePointPausedMessage(activePausePointId, pausedActionDescription);
                return ValidationResult.Failure(message);
            }

            return ValidationResult.Success();
        }

        /// <summary>
        /// Composes the paused-mode failure message. Exposed so tests can pin the exact
        /// wire-visible string for each caller's action suffix.
        /// </summary>
        public static string FormatPausedMessage(string pausedActionDescription)
        {
            Debug.Assert(!string.IsNullOrEmpty(pausedActionDescription), "pausedActionDescription must not be null or empty");
            return $"PlayMode is paused. Resume PlayMode before {pausedActionDescription}.";
        }

        /// <summary>
        /// Composes the paused-mode failure message for the common case where a pause point is
        /// what's holding PlayMode paused, so agents calling a simulate/record tool right after a
        /// pause-point hit see the real cause instead of a generic "PlayMode is paused" rejection.
        /// </summary>
        public static string FormatPausePointPausedMessage(string pausePointId, string pausedActionDescription)
        {
            Debug.Assert(!string.IsNullOrEmpty(pausePointId), "pausePointId must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(pausedActionDescription), "pausedActionDescription must not be null or empty");
            return
                $"PlayMode is paused because pause point '{pausePointId}' is active (check pause-point-status). Resume PlayMode before {pausedActionDescription}.";
        }
    }
}
