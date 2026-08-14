namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds the Play-start Warning for the case where entering Play Mode from Edit mode
    /// will trigger a domain reload while hot-reload patches or enabled pause points exist:
    /// the reload silently discards both, and edits that were only hot-reloaded are not in
    /// the compiled assemblies the new session runs.
    /// </summary>
    internal static class PlayModeStartDomainReloadDropWarningBuilder
    {
        public static string BuildWarning(
            bool wasPlayingAtRequestStart,
            bool isDomainReloadDisabledOnEnterPlayMode,
            int activeHotReloadPatchCount,
            int activePausePointCount)
        {
            if (wasPlayingAtRequestStart || isDomainReloadDisabledOnEnterPlayMode)
            {
                return null;
            }
            if (activeHotReloadPatchCount <= 0 && activePausePointCount <= 0)
            {
                return null;
            }

            if (activeHotReloadPatchCount > 0 && activePausePointCount > 0)
            {
                return "Entering Play Mode triggers a domain reload that will discard "
                    + activeHotReloadPatchCount
                    + " active hot-reload change(s) and "
                    + activePausePointCount
                    + " enabled pause point(s). The new session runs the last compiled assemblies, so hot-reloaded edits that were never compiled are not in effect — run `uloop compile` before Play to keep them, or re-apply `uloop hot-reload` and re-enable pause points after Play Mode starts.";
            }

            if (activeHotReloadPatchCount > 0)
            {
                return "Entering Play Mode triggers a domain reload that will discard "
                    + activeHotReloadPatchCount
                    + " active hot-reload change(s). The new session runs the last compiled assemblies, so hot-reloaded edits that were never compiled are not in effect — run `uloop compile` before Play to keep them, or re-apply `uloop hot-reload` after Play Mode starts.";
            }

            return "Entering Play Mode triggers a domain reload that will discard "
                + activePausePointCount
                + " enabled pause point(s). Re-enable them after Play Mode starts.";
        }
    }
}
