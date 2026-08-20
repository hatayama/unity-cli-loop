namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// User-facing stop-reason values for control-play-mode StoppedBy.
    /// </summary>
    internal static class ControlPlayModeConstants
    {
        internal const string StoppedByCliControlPlayMode = "cli-control-play-mode";
        internal const string StoppedByCliCompileStopSetting = "cli-compile-stop-setting";
        internal const string StoppedByCliRunTestsCancel = "cli-run-tests-cancel";
        internal const string StoppedByScriptCompilation = "script-compilation";
        internal const string StoppedByUnknown = "unknown";
    }
}
