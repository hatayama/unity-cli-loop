namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds the compile Warning text when Play Mode was active or hot-reload changes were live
    /// at the moment compile was requested: the compile stops Play Mode and the following
    /// domain reload discards the Play session state, every pause-point Harmony patch when any
    /// are enabled, and every active hot-reload patch.
    /// </summary>
    internal static class CompilePlayModeStopWarningBuilder
    {
        public static string BuildWarning(
            bool wasPlayingAtRequestStart,
            int activePausePointCount,
            int activeHotReloadChangeCount)
        {
            string playWarning = BuildPlayModeStopWarning(wasPlayingAtRequestStart, activePausePointCount);
            string hotReloadWarning = BuildHotReloadDropWarning(activeHotReloadChangeCount);
            if (playWarning == null)
            {
                return hotReloadWarning;
            }

            if (hotReloadWarning == null)
            {
                return playWarning;
            }

            return playWarning + " " + hotReloadWarning;
        }

        private static string BuildPlayModeStopWarning(bool wasPlayingAtRequestStart, int activePausePointCount)
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

        private static string BuildHotReloadDropWarning(int activeHotReloadChangeCount)
        {
            if (activeHotReloadChangeCount <= 0)
            {
                return null;
            }

            return activeHotReloadChangeCount
                + " active hot-reload change(s) were live when this compile was requested. "
                + "A successful compile reloads the domain and drops every hot-reload patch; "
                + "the edited source files are compiled in, so the behavior stays without re-applying them.";
        }
    }
}
