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
        /// Schedules one preflight for when Edit Mode returns. Called when the Editor leaves Edit Mode, because
        /// a file changed during Play (or while the Editor stayed focused) is followed by no focus return, and
        /// with Auto Refresh enabled Unity imports it right after Play Mode ends and raises its reload dialog.
        /// </summary>
        public void DeferUntilEditMode()
        {
            IsDeferred = true;
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

        /// <summary>
        /// Returns whether a deferred preflight must run now, and clears the deferral so it runs only once.
        /// </summary>
        public bool ConsumeOnEnteredEditMode()
        {
            bool wasDeferred = IsDeferred;
            IsDeferred = false;
            return wasDeferred;
        }
    }
}
