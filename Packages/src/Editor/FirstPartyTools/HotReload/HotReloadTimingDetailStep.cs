namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// One named step outside the apply run's phases and the milliseconds the run spent in it.
    /// </summary>
    internal sealed class HotReloadTimingDetailStep
    {
        public HotReloadTimingDetailStep(string step, long ms)
        {
            Step = step;
            Ms = ms;
        }

        public string Step { get; }

        public long Ms { get; }
    }
}
