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
        public string SceneRefreshWarning { get; }

        internal HotReloadAutoRefreshHoldSyncResult(
            bool held,
            bool newlyArmed,
            bool releaseDeferred,
            string sceneRefreshWarning = null)
        {
            Held = held;
            NewlyArmed = newlyArmed;
            ReleaseDeferred = releaseDeferred;
            SceneRefreshWarning = sceneRefreshWarning;
        }

        internal static HotReloadAutoRefreshHoldSyncResult Unchanged(bool held)
        {
            return new HotReloadAutoRefreshHoldSyncResult(held, false, false);
        }
    }
}
