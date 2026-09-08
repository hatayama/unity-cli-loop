using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds the Play-start Warning for the case where entering Play Mode from Edit mode
    /// will trigger a domain reload while hot-reload patches or enabled pause points exist:
    /// the reload silently discards both, and edits that were only hot-reloaded are not in
    /// the compiled assemblies the new session runs. Pause points enabled with --persist are
    /// counted separately, because those are re-armed after the reload.
    /// </summary>
    internal static class PlayModeStartDomainReloadDropWarningBuilder
    {
        public static string BuildWarning(
            bool wasPlayingAtRequestStart,
            bool isDomainReloadDisabledOnEnterPlayMode,
            int activeHotReloadChangeCount,
            int activePausePointCount,
            int activePersistedPausePointCount)
        {
            if (wasPlayingAtRequestStart || isDomainReloadDisabledOnEnterPlayMode)
            {
                return null;
            }
            if (activeHotReloadChangeCount <= 0 && activePausePointCount <= 0)
            {
                return null;
            }

            int droppedPausePointCount = CountDropped(activePausePointCount, activePersistedPausePointCount);
            return Join(
                BuildDropWarning(activeHotReloadChangeCount, droppedPausePointCount),
                BuildPersistedPausePointNotice(activePersistedPausePointCount));
        }

        // Persisted pause points come back after the reload, so they are not part of the count
        // the caller has to re-enable by hand.
        private static int CountDropped(int activePausePointCount, int activePersistedPausePointCount)
        {
            int dropped = activePausePointCount - activePersistedPausePointCount;
            return dropped < 0 ? 0 : dropped;
        }

        private static string Join(params string[] sentences)
        {
            List<string> present = new(sentences.Length);
            foreach (string sentence in sentences)
            {
                if (sentence != null)
                {
                    present.Add(sentence);
                }
            }

            return present.Count == 0 ? null : string.Join(" ", present);
        }

        private static string BuildDropWarning(int activeHotReloadChangeCount, int droppedPausePointCount)
        {
            if (activeHotReloadChangeCount > 0 && droppedPausePointCount > 0)
            {
                return "Entering Play Mode triggers a domain reload that will discard "
                    + activeHotReloadChangeCount
                    + " active hot-reload change(s) and "
                    + droppedPausePointCount
                    + " enabled pause point(s). The new session runs the last compiled assemblies, so hot-reloaded edits that were never compiled are not in effect — run `uloop compile` before Play to keep them, or re-apply `uloop hot-reload` and re-enable pause points after Play Mode starts.";
            }

            if (activeHotReloadChangeCount > 0)
            {
                return "Entering Play Mode triggers a domain reload that will discard "
                    + activeHotReloadChangeCount
                    + " active hot-reload change(s). The new session runs the last compiled assemblies, so hot-reloaded edits that were never compiled are not in effect — run `uloop compile` before Play to keep them, or re-apply `uloop hot-reload` after Play Mode starts.";
            }

            if (droppedPausePointCount <= 0)
            {
                return null;
            }

            return "Entering Play Mode triggers a domain reload that will discard "
                + droppedPausePointCount
                + " enabled pause point(s). Re-enable them after Play Mode starts.";
        }

        /// <summary>
        /// Persisted pause points are not lost, but the caller still has to check whether the
        /// re-arm succeeded, so they get their own sentence rather than being counted as dropped.
        /// The Compile warning carries the same sentence; the two tools are separate assemblies
        /// and must not reference each other.
        /// </summary>
        private static string BuildPersistedPausePointNotice(int activePersistedPausePointCount)
        {
            if (activePersistedPausePointCount <= 0)
            {
                return null;
            }

            return activePersistedPausePointCount
                + " persisted pause point(s) re-arm automatically after the reload; "
                + "check pause-point-status for the re-arm result.";
        }
    }
}
