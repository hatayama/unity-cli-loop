namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds the compile Warning text when Play Mode was active at the moment compile was
    /// requested: the compile stops Play Mode and the following domain reload discards the
    /// Play session state, and also every pause-point Harmony patch when any are enabled.
    /// </summary>
    internal static class CompilePlayModeStopWarningBuilder
    {
        public static string BuildWarning(bool wasPlayingAtRequestStart, int activePausePointCount)
        {
            if (!wasPlayingAtRequestStart)
            {
                return null;
            }

            if (activePausePointCount > 0)
            {
                return "Play Mode was active with " + activePausePointCount + " enabled pause point(s). "
                    + "The compile stops Play Mode and the domain reload discards the Play session state "
                    + "and all pause point patches — re-enable pause points after the compile completes.";
            }

            return "Play Mode was active when this compile was requested. The compile stops Play Mode and the domain reload discards the Play session state — re-establish your runtime state before continuing verification.";
        }
    }
}
