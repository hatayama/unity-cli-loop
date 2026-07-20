namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds the compile Warning text for the case where Play Mode was active with enabled
    /// pause points at the moment compile was requested: the domain reload that follows discards
    /// both the Play session state and every pause-point Harmony patch.
    /// </summary>
    internal static class CompilePausePointWarningBuilder
    {
        public static string BuildWarning(bool wasPlayingAtRequestStart, int activePausePointCount)
        {
            if (!wasPlayingAtRequestStart || activePausePointCount <= 0)
            {
                return null;
            }

            return "Play Mode was active with " + activePausePointCount + " enabled pause point(s). "
                + "The compile stops Play Mode and the domain reload discards the Play session state "
                + "and all pause point patches — re-enable pause points after the compile completes.";
        }
    }
}
