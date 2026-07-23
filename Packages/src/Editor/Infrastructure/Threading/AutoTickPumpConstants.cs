namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Timing constants for the scoped SignalTick pump that keeps the editor alive while unfocused.
    /// </summary>
    internal static class AutoTickPumpConstants
    {
        // Why: ~60Hz matches a focused editor and com.unity.pipeline's AutoTickCommand default.
        // Smaller intervals waste CPU; larger ones slow frame-dependent compile/test progress.
        internal const int PUMP_INTERVAL_MS = 16;

        // Why: command teardown and back-to-back CLI polling leave brief idle gaps; without a
        // trailing window the editor would re-throttle between requests and stall again.
        // Domain-reload recovery also needs a short awake period after Infrastructure startup.
        internal const double TRAILING_WINDOW_SECONDS = 10.0;
    }
}
