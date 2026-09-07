namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// What removing one method's hot-reload patch did: the method held no patch, the patch was
    /// removed, or the ledger entry is gone but Harmony could not restore the original body.
    /// </summary>
    internal enum HotReloadRevertOutcome
    {
        NotPatched,
        Reverted,
        UnpatchFailed
    }
}
