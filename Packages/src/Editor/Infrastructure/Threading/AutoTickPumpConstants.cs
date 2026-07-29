namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Timing constants for the always-on SignalTick pump that keeps the editor alive while unfocused.
    /// </summary>
    internal static class AutoTickPumpConstants
    {
        // Why: ~60Hz matches a focused editor and com.unity.pipeline's AutoTickCommand default.
        // Smaller intervals waste CPU; larger ones slow frame-dependent compile/test progress.
        internal const int PUMP_INTERVAL_MS = 16;
    }
}
