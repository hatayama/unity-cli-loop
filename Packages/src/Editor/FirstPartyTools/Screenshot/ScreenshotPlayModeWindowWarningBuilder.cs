using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds the Warning for a window screenshot taken while Play Mode is running.
    /// </summary>
    internal static class ScreenshotPlayModeWindowWarningBuilder
    {
        // Why only window+playing: rendering already is the Game View image, and Edit Mode
        // window captures are the expected chrome-inclusive Editor screenshot.
        public static string Build(CaptureMode captureMode, bool isPlaying)
        {
            if (captureMode != CaptureMode.window || !isPlaying)
            {
                return string.Empty;
            }

            return "This window capture includes Unity Editor chrome. If you wanted the Game View image (typical during Play Mode), re-run with --capture-mode rendering.";
        }
    }
}
