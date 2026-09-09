namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Decision for an unchanged-source probe against the domain's applied-source records.
    /// </summary>
    internal enum HotReloadUnchangedSourceDecision
    {
        NotUnchanged,
        ShortCircuited,
        ReapplyNonBaseline
    }
}
