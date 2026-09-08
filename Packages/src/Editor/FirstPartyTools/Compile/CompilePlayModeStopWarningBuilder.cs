using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds the compile Warning text for the state that the compile is about to discard:
    /// the Play session when Play Mode was active, every pause-point Harmony patch that will not
    /// come back (in Play Mode and in Edit Mode alike, because the compile reloads the domain
    /// either way), and every active hot-reload patch. Pause points enabled with --persist are
    /// counted separately, because those do come back.
    /// </summary>
    internal static class CompilePlayModeStopWarningBuilder
    {
        public static string BuildWarning(
            bool wasPlayingAtRequestStart,
            int activePausePointCount,
            int activePersistedPausePointCount,
            int activeHotReloadChangeCount)
        {
            int droppedPausePointCount = CountDropped(activePausePointCount, activePersistedPausePointCount);
            string primaryWarning = wasPlayingAtRequestStart
                ? BuildPlayModeStopWarning(droppedPausePointCount)
                : BuildEditModePausePointDropWarning(droppedPausePointCount);
            return Join(
                primaryWarning,
                BuildPersistedPausePointNotice(activePersistedPausePointCount),
                BuildHotReloadDropWarning(activeHotReloadChangeCount));
        }

        // Persisted pause points are re-armed after the reload, so they are not part of the count
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

        private static string BuildPlayModeStopWarning(int droppedPausePointCount)
        {
            if (droppedPausePointCount > 0)
            {
                return "Play Mode was active with " + droppedPausePointCount + " enabled pause point(s). "
                    + "The compile stops Play Mode and the domain reload discards the Play session state "
                    + "and all pause point patches — re-enable pause points after the compile completes.";
            }

            return "Play Mode was active when this compile was requested. The compile stops Play Mode and the domain reload discards the Play session state — re-establish your runtime state before continuing verification.";
        }

        /// <summary>
        /// An Edit Mode compile keeps no Play session to lose, but its domain reload still drops
        /// every pause point patch, so the caller has to be told the patches are gone.
        /// </summary>
        private static string BuildEditModePausePointDropWarning(int droppedPausePointCount)
        {
            if (droppedPausePointCount <= 0)
            {
                return null;
            }

            return droppedPausePointCount + " enabled pause point(s) were armed when this compile was requested. "
                + "A successful compile reloads the domain and drops every pause point patch — "
                + "re-enable them after the compile completes.";
        }

        /// <summary>
        /// Persisted pause points are not lost, but the caller still has to check whether the
        /// re-arm succeeded, so they get their own sentence rather than being counted as dropped.
        /// The ControlPlayMode Play-start warning carries the same sentence; the two tools are
        /// separate assemblies and must not reference each other.
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
