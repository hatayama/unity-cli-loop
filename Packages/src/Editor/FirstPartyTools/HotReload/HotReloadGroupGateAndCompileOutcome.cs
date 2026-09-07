namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// What the gate and the first shim compile of one group left for the commit boundary: the
    /// group already reported its own failure, it succeeded with no entry to patch, or it holds
    /// the entries the apply stage patches.
    /// </summary>
    internal enum HotReloadGroupGateAndCompileOutcome
    {
        Failed,
        ReadyWithoutEntries,
        ReadyWithEntries
    }
}
