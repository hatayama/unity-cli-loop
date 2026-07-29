namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Persistence keys for the focus-gated Play Mode window-raise suppression.
    /// </summary>
    internal static class PlayModeFocusSuppressionConstants
    {
        // Why EditorUserSettings: the flag must survive both domain reload and editor restart so a
        // crash while unfocused still restores PlayFocused views when focus next returns.
        internal const string SuppressedConfigKey =
            "io.github.hatayama.UnityCliLoop.PlayModeFocusSuppression.Suppressed";
        internal const string SuppressedConfigValue = "1";
    }
}
