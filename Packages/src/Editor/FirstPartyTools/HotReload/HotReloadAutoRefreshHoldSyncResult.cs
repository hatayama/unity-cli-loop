namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Outcome of one Auto Refresh hold sync against the live patch ledger.
    /// </summary>
    internal sealed class HotReloadAutoRefreshHoldSyncResult
    {
        public bool Held { get; }
        public bool NewlyArmed { get; }
        public bool ReleaseDeferred { get; }

        internal HotReloadAutoRefreshHoldSyncResult(bool held, bool newlyArmed, bool releaseDeferred)
        {
            Held = held;
            NewlyArmed = newlyArmed;
            ReleaseDeferred = releaseDeferred;
        }

        internal static HotReloadAutoRefreshHoldSyncResult Unchanged(bool held)
        {
            return new HotReloadAutoRefreshHoldSyncResult(held, false, false);
        }
    }
}
