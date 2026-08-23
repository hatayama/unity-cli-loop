using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Resolves CaptureMode.auto to window or rendering from the current Play Mode state.
    /// </summary>
    internal static class ScreenshotCaptureModeResolver
    {
        /// <summary>
        /// Returns window or rendering. auto follows Play Mode; GameView is an explicit rendering alias.
        /// </summary>
        internal static CaptureMode Resolve(CaptureMode requested, bool isPlaying)
        {
            if (requested == CaptureMode.GameView)
            {
                return CaptureMode.rendering;
            }

            if (requested != CaptureMode.auto)
            {
                return requested;
            }

            if (isPlaying)
            {
                return CaptureMode.rendering;
            }

            return CaptureMode.window;
        }

        /// <summary>
        /// Returns the wire name for a resolved capture mode ("window" or "rendering").
        /// </summary>
        internal static string ToWireName(CaptureMode resolved)
        {
            Debug.Assert(
                resolved == CaptureMode.window || resolved == CaptureMode.rendering,
                "ToWireName requires a resolved capture mode; auto and GameView must be resolved first.");
            if (resolved == CaptureMode.window)
            {
                return UnityCliLoopConstants.SCREENSHOT_RESOLVED_CAPTURE_MODE_WINDOW;
            }

            return UnityCliLoopConstants.SCREENSHOT_RESOLVED_CAPTURE_MODE_RENDERING;
        }
    }
}
