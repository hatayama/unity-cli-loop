namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Shared limits for simulate-keyboard / simulate-mouse-input / simulate-mouse-ui durations.
    /// </summary>
    public static class SimulateInputConstants
    {
        // Why 30s: agents often pass milliseconds (e.g. 600) as seconds, which freezes Unity for
        // minutes and blocks every CLI command with server_busy. Cap rejects that class of typo.
        public const float MaxDurationSeconds = 30f;
    }
}
