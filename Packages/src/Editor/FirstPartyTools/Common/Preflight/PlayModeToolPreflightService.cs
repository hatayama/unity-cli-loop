#nullable enable
using UnityEditor;
using UnityEngine;

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
                return ValidationResult.Failure(FormatPausedMessage(pausedActionDescription));
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
    }
}
