namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Outcome of resolving a pause-point file:line against an active hot-reload shim generation.
    /// </summary>
    internal enum SourcePausePointShimResolveKind
    {
        TransplantChainJoin,
        ShimDirect,
        NotInPatchedMethod,
        PatchedMethodPdbUnavailable,
        NoStatementInPatchedMethod
    }
}
