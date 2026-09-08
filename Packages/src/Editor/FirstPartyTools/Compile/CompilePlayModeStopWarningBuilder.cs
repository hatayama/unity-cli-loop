namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds the compile Warning text for the state that the compile is about to discard:
    /// the Play session when Play Mode was active, every pause-point Harmony patch when any are
    /// enabled (in Play Mode and in Edit Mode alike, because the compile reloads the domain
    /// either way), and every active hot-reload patch.
    /// </summary>
    internal static class CompilePlayModeStopWarningBuilder
    {
        public static string BuildWarning(
            bool wasPlayingAtRequestStart,
            int activePausePointCount,
            int activeHotReloadChangeCount)
        {
            string primaryWarning = wasPlayingAtRequestStart
                ? BuildPlayModeStopWarning(activePausePointCount)
                : BuildEditModePausePointDropWarning(activePausePointCount);
            string hotReloadWarning = BuildHotReloadDropWarning(activeHotReloadChangeCount);
            return Join(primaryWarning, hotReloadWarning);
        }

        private static string Join(string primaryWarning, string hotReloadWarning)
        {
            if (primaryWarning == null)
            {
                return hotReloadWarning;
            }

            if (hotReloadWarning == null)
            {
                return primaryWarning;
            }

            return primaryWarning + " " + hotReloadWarning;
        }

        private static string BuildPlayModeStopWarning(int activePausePointCount)
        {
            if (activePausePointCount > 0)
            {
                return "Play Mode was active with " + activePausePointCount + " enabled pause point(s). "
                    + "The compile stops Play Mode and the domain reload discards the Play session state "
                    + "and all pause point patches — re-enable pause points after the compile completes.";
            }

            return "Play Mode was active when this compile was requested. The compile stops Play Mode and the domain reload discards the Play session state — re-establish your runtime state before continuing verification.";
        }

        /// <summary>
        /// An Edit Mode compile keeps no Play session to lose, but its domain reload still drops
        /// every pause point patch, so the caller has to be told the patches are gone.
        /// </summary>
        private static string BuildEditModePausePointDropWarning(int activePausePointCount)
        {
            if (activePausePointCount <= 0)
            {
                return null;
            }

            return activePausePointCount + " enabled pause point(s) were armed when this compile was requested. "
                + "A successful compile reloads the domain and drops every pause point patch — "
                + "re-enable them after the compile completes.";
        }

        private static string BuildHotReloadDropWarning(int activeHotReloadChangeCount)
        {
            if (activeHotReloadChangeCount <= 0)
            {
                return null;
            }

            return activeHotReloadChangeCount
                + " active hot-reload change(s) were live when this compile was requested. "
                + "A successful compile reloads the domain and drops every hot-reload change, "
                + "introduced types included; the edited source files are compiled in, so the "
                + "behavior stays without re-applying them.";
        }
    }
}
