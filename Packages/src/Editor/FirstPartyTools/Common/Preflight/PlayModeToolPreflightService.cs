#nullable enable
using UnityEditor;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Runtime;

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
        public static PlayModeToolPreflightResult RequireActive()
        {
            if (!EditorApplication.isPlaying)
            {
                return PlayModeToolPreflightResult.Failure(PlayModeNotActiveMessage);
            }

            return PlayModeToolPreflightResult.Success();
        }

        /// <summary>
        /// Fails when PlayMode is not active, or is active but paused. The paused-message suffix
        /// describes the blocked action in the caller's vocabulary (for example "recording input").
        /// </summary>
        public static PlayModeToolPreflightResult RequireActiveAndNotPaused(string pausedActionDescription)
        {
            return Evaluate(
                EditorApplication.isPlaying,
                EditorApplication.isPaused,
                UloopPausePointRegistry.GetActivePausePointId(),
                pausedActionDescription);
        }

        /// <summary>
        /// Decides a paused-aware preflight outcome from already-read editor state. Separated from
        /// RequireActiveAndNotPaused so the paused branches — the only ones that produce a pause
        /// point id — are reachable from EditMode tests, where PlayMode is never actually running.
        /// </summary>
        public static PlayModeToolPreflightResult Evaluate(
            bool isPlaying,
            bool isPaused,
            string activePausePointId,
            string pausedActionDescription)
        {
            Debug.Assert(!string.IsNullOrEmpty(pausedActionDescription), "pausedActionDescription must not be null or empty");

            if (!isPlaying)
            {
                return PlayModeToolPreflightResult.Failure(PlayModeNotActiveMessage);
            }

            if (!isPaused)
            {
                return PlayModeToolPreflightResult.Success();
            }

            if (string.IsNullOrEmpty(activePausePointId))
            {
                return PlayModeToolPreflightResult.Failure(FormatPausedMessage(pausedActionDescription));
            }

            return PlayModeToolPreflightResult.FailureRejectedByPausePoint(
                FormatPausePointPausedMessage(activePausePointId, pausedActionDescription),
                activePausePointId);
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
