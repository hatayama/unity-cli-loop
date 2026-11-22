namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// What happened when a compile result was matched with the reload it caused.
    /// </summary>
    internal enum ReloadAttributionOutcome
    {
        /// <summary>The result already reported a reload, and nothing changed.</summary>
        AlreadySettled,

        /// <summary>The result now reports the reload, for good.</summary>
        Settled,

        /// <summary>No reload has finished since the given time, and nothing changed.</summary>
        NoReloadYet
    }
}
