using UnityEditor;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Remembers that the Scene preflight must run once Edit Mode returns: for a focus return skipped during
    /// Play Mode, and for every Play session.
    /// </summary>
    internal sealed class ExternalSceneFocusReturnDeferral
    {
        public ExternalSceneFocusReturnDeferral(bool isDeferred)
        {
            IsDeferred = isDeferred;
        }

        public bool IsDeferred { get; private set; }

        /// <summary>
        /// Decides whether the focus-return preflight may run now. In Play Mode the open-Scene list also holds
        /// Scenes loaded at runtime and EditorSceneManager.RestoreSceneManagerSetup throws, so the preflight
        /// is deferred instead of being applied to that state.
        /// </summary>
        public bool ShouldResolveOnFocusReturn(bool isPlayingOrWillChangePlaymode)
        {
            if (!isPlayingOrWillChangePlaymode)
            {
                return true;
            }

            IsDeferred = true;
            return false;
        }

        /// <summary>
        /// Decides whether the preflight must run for this Play Mode transition. Leaving Edit Mode schedules
        /// one preflight for when Edit Mode returns, and that preflight runs whether or not the Editor is
        /// focused: Unity's own post-Play refresh can import a changed Scene before any focus return, and only
        /// the preflight's import-then-reload keeps the reload dialog from appearing. The caller passes the focus
        /// state and resolves exactly when this returns true, so ignoring focus stays part of this tested decision
        /// instead of a condition the caller could add back.
        /// </summary>
        public bool ShouldResolveOnPlayModeStateChange(PlayModeStateChange state, bool isFocused)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                IsDeferred = true;
                return false;
            }

            if (state != PlayModeStateChange.EnteredEditMode)
            {
                return false;
            }

            return ConsumeDeferral();
        }

        /// <summary>
        /// Decides whether Initialize may replace the restored fingerprints with the current disk state.
        /// A deferred focus return needs the pre-reload fingerprints to compare against, so re-recording
        /// them after the Play Mode domain reload would hide exactly the change it was deferred to detect.
        /// </summary>
        public bool ShouldRecordBaselineOnInitialize(bool isFocused, bool restoredSceneSnapshots)
        {
            return !restoredSceneSnapshots || (isFocused && !IsDeferred);
        }

        private bool ConsumeDeferral()
        {
            bool wasDeferred = IsDeferred;
            IsDeferred = false;
            return wasDeferred;
        }
    }
}
