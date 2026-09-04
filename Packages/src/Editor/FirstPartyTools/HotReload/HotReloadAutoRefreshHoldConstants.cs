namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Session key, response copy, and VibeLogger names for the patch-lifetime Auto Refresh hold.
    /// </summary>
    internal static class HotReloadAutoRefreshHoldConstants
    {
        internal const string SessionStateKey =
            "io.github.hatayama.UnityCliLoop.HotReloadAutoRefreshHold.Held";

        internal const string NewlyArmedMessageSuffix =
            "Auto Refresh is held while patches are active, so returning focus to the Editor will not recompile; run 'uloop compile' or '--revert-all' to release it.";

        internal const string ReleaseDeferredWarning =
            "Auto Refresh hold released; pending script edits import on the next focus return or 'uloop compile'.";

        internal const string SceneRefreshBlockedWarning =
            "Auto Refresh hold released, but the open scene has unsaved changes and was modified on disk; resolve it, then run 'uloop compile'.";

        internal const string VibeArmed = "hot_reload_auto_refresh_hold_armed";
        internal const string VibeReleased = "hot_reload_auto_refresh_hold_released";
        internal const string VibeReleaseDeferred = "hot_reload_auto_refresh_hold_release_deferred";
        internal const string VibeFailed = "hot_reload_auto_refresh_hold_failed";
        internal const string VibeReleaseFailed = "hot_reload_auto_refresh_hold_release_failed";
        internal const string VibeSceneRefreshBlocked =
            "hot_reload_auto_refresh_hold_scene_refresh_blocked";

        // Why 0.5s: matches the retired focus-loss hold; domain-reload throws need a later retry
        // without walking AssetDatabase every Editor update tick.
        internal const double ReconcileIntervalSeconds = 0.5d;
    }
}
