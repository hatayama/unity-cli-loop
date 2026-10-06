namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Milliseconds an apply run spent in each phase, as the response reports them.
    /// </summary>
    public sealed class HotReloadTimingResponse
    {
        /// <summary>Transform worker runs, introduced-type preparation included.</summary>
        public long AnalysisMs { get; set; }

        /// <summary>The signature-change gate and the shim compile, isolation retries included.</summary>
        public long ShimCompileMs { get; set; }

        /// <summary>Applying the patches.</summary>
        public long PatchMs { get; set; }

        /// <summary>
        /// What is left of TotalMs after the three phases: file resolution, planning, and the
        /// checks that find unchanged methods.
        /// </summary>
        public long OtherMs { get; set; }

        /// <summary>The whole run inside the Editor.</summary>
        public long TotalMs { get; set; }
    }
}
