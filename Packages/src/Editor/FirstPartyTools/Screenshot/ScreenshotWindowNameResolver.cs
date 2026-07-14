using System;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Resolves which Editor window title to capture for screenshot --capture-mode window.
    /// </summary>
    internal static class ScreenshotWindowNameResolver
    {
        /// <summary>
        /// Decide whether to retry with the Device Simulator title after a Game window miss.
        /// why: CLI default window name is Game, but Device Simulator replaces that tab so the title is Simulator.
        /// </summary>
        internal static bool ShouldFallbackToSimulator(
            string requestedWindowName,
            WindowMatchMode matchMode,
            int matchCount)
        {
            if (matchCount > 0)
            {
                return false;
            }

            // why: only the default Game exact lookup means "capture the play view chrome"
            if (matchMode != WindowMatchMode.exact)
            {
                return false;
            }

            return string.Equals(
                requestedWindowName,
                UnityCliLoopConstants.SCREENSHOT_DEFAULT_WINDOW_NAME,
                StringComparison.OrdinalIgnoreCase);
        }

        internal static string ResolveCaptureWindowName(
            string requestedWindowName,
            WindowMatchMode matchMode,
            int primaryMatchCount)
        {
            if (ShouldFallbackToSimulator(requestedWindowName, matchMode, primaryMatchCount))
            {
                return UnityCliLoopConstants.SCREENSHOT_SIMULATOR_WINDOW_NAME;
            }

            return requestedWindowName;
        }
    }
}
