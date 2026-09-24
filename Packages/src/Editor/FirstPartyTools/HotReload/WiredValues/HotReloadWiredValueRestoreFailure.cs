namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// A wired value that did not come back to its host's replacement, and why.
    /// </summary>
    internal sealed class HotReloadWiredValueRestoreFailure
    {
        internal HotReloadWiredValueRestoreFailure(string hostIdentity, string storeFieldKey, string reason)
        {
            HostIdentity = hostIdentity;
            StoreFieldKey = storeFieldKey;
            Reason = reason;
        }

        internal string HostIdentity { get; }

        internal string StoreFieldKey { get; }

        internal string Reason { get; }
    }
}
