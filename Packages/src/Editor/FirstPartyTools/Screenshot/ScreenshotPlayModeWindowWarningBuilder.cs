using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds the Warning for a window screenshot taken while Play Mode is running.
    /// </summary>
    internal static class ScreenshotPlayModeWindowWarningBuilder
    {
        // Why window+playing+at-least-one-image: rendering already is the Game View image,
        // Edit Mode window captures are the expected chrome-inclusive Editor screenshot,
        // and a chrome warning would describe images that do not exist when capturedCount is 0.
        public static string Build(CaptureMode captureMode, bool isPlaying, int capturedCount)
        {
            if (captureMode != CaptureMode.window || !isPlaying || capturedCount == 0)
            {
                return string.Empty;
            }

            return "This window capture includes Unity Editor chrome. If you wanted the Game View image (typical during Play Mode), re-run with --capture-mode rendering.";
        }
    }
}
