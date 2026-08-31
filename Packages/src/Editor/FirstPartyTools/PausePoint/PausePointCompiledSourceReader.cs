using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Loads last-compiled snapshot source text for pause-point line comparison and remap.
    /// </summary>
    internal static class PausePointCompiledSourceReader
    {
        internal static string LoadSnapshotOrEmpty(string requestedFile)
        {
            string normalizedFile = SourcePausePointPathNormalizer.ToForwardSlashes(requestedFile);
            string snapshotSource =
                HotReloadPausePointCoordination.GetVerifiedSnapshotSourceForFile?.Invoke(normalizedFile);
            return snapshotSource ?? string.Empty;
        }
    }
}
