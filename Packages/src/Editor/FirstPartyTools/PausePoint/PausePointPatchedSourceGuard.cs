namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Refuses a pause point inside a hot-reload patched method whose file changed on disk after
    /// the patch, because the patch's line numbers no longer follow the file.
    /// </summary>
    internal static class PausePointPatchedSourceGuard
    {
        // Why only for a line inside a patched method: its marker would follow the line numbers
        // of the source the patch was compiled from. A line outside every patched method goes to
        // the compiled resolver, which compares against the compiled snapshot on its own, and
        // arming the compiled body of a patched method instead would stop in code that never runs.
        internal static PausePointResponse RefuseWhenChangedOrNull(
            bool shimSourceChanged,
            SourcePausePointShimResolution shimResolution,
            string normalizedFile,
            int line)
        {
            if (!shimSourceChanged || !IsInsidePatchedMethod(shimResolution.Kind))
            {
                return null;
            }

            return PausePointFailureResponse.Create(
                string.Format(SourcePausePointConstants.PatchedSourceChangedOnDiskMessageFormat, normalizedFile, line),
                SourcePausePointConstants.ErrorCodePatchedSourceChanged,
                SourcePausePointConstants.PatchedSourceChangedOnDiskHint);
        }

        private static bool IsInsidePatchedMethod(SourcePausePointShimResolveKind kind)
        {
            return kind == SourcePausePointShimResolveKind.TransplantChainJoin
                || kind == SourcePausePointShimResolveKind.ShimDirect
                || kind == SourcePausePointShimResolveKind.NoStatementInPatchedMethod;
        }
    }
}
