namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Which instruction stream an injection's indexes and local slots were resolved against.
    /// </summary>
    internal enum SourcePausePointPatchInjectionTargetKind
    {
        OriginalBody,
        TransplantChainJoin,
        ShimDirect
    }
}
