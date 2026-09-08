namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    internal enum HotReloadMethodOutcomeKind
    {
        Patched = 0,
        Skipped = 1,
        Failed = 2,
        Added = 3,
        AlreadyActive = 4,
        Stale = 5
    }
}
