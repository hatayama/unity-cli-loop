namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// What the group preparation decided about one file, before anything is mutated.
    /// </summary>
    internal enum HotReloadGroupFilePreparationKind
    {
        SkippedByGroup,
        NoEntriesToApply,
        ResolutionFailed,
        Resolved
    }
}
