namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Appends the Warning for a screenshot image taken while Play Mode is paused.
    /// </summary>
    internal static class ScreenshotPausedPlayModeWarningBuilder
    {
        public const string PausedWarning =
            "Play Mode was paused during this capture. UGUI/canvas content may reflect the frame rendered before the pause, while other observations reflect the paused state. To refresh it, run `uloop control-play-mode --action Step` once, then capture again.";

        // Why playing+paused+image: isPaused stays true after Play Mode exits, an elements-only
        // response has no image that could be stale, and zero images leave nothing to describe.
        public static string Append(
            string existingWarning,
            bool isPlaying,
            bool isPaused,
            bool elementsOnly,
            int capturedCount)
        {
            if (!isPlaying || !isPaused || elementsOnly || capturedCount == 0)
            {
                return existingWarning;
            }

            if (string.IsNullOrEmpty(existingWarning))
            {
                return PausedWarning;
            }

            return existingWarning + " " + PausedWarning;
        }
    }
}
