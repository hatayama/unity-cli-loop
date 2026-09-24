namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Timing constants for the always-on SignalTick pump kept against macOS parking an unfocused editor.
    /// </summary>
    internal static class AutoTickPumpConstants
    {
        // Why: matches com.unity.pipeline's AutoTickCommand default. It only throttles how often the
        // pump signals; an unfocused editor still updates about every 100ms whatever this value is.
        internal const int PUMP_INTERVAL_MS = 16;
    }
}
