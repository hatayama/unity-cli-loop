namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Sums the milliseconds one apply run spends in each phase across all of its groups.
    /// </summary>
    /// <remarks>
    /// Why an accumulator the run hands down: group results are per file and carry no group-level
    /// data, so the run hands this accumulator down to the group processor instead. Each run makes
    /// its own, so no long-lived object keeps the state of a run.
    /// </remarks>
    internal sealed class HotReloadRunTiming
    {
        private long _analysisMs;
        private long _shimCompileMs;
        private long _patchMs;

        public void AddAnalysis(long milliseconds)
        {
            _analysisMs += HotReloadTimingBreakdown.RequireElapsed(milliseconds, nameof(milliseconds));
        }

        public void AddShimCompile(long milliseconds)
        {
            _shimCompileMs += HotReloadTimingBreakdown.RequireElapsed(milliseconds, nameof(milliseconds));
        }

        public void AddPatch(long milliseconds)
        {
            _patchMs += HotReloadTimingBreakdown.RequireElapsed(milliseconds, nameof(milliseconds));
        }

        public HotReloadTimingBreakdown Complete(long totalMs)
        {
            return new HotReloadTimingBreakdown(_analysisMs, _shimCompileMs, _patchMs, totalMs);
        }
    }
}
