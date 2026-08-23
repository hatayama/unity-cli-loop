using System.IO;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Loads last-compiled or on-disk source text for pause-point line comparison and remap.
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

        // Why disk only after snapshot miss: a verified hot-reload snapshot is the last compiled
        // source; without one the on-disk file is the only text the span scan can read, and
        // re-resolve still fail-opens if those line numbers no longer match the PDB.
        internal static string LoadSnapshotOrDiskOrEmpty(string requestedFile)
        {
            string snapshotSource = LoadSnapshotOrEmpty(requestedFile);
            if (!string.IsNullOrEmpty(snapshotSource))
            {
                return snapshotSource;
            }

            if (string.IsNullOrEmpty(requestedFile))
            {
                return string.Empty;
            }

            string normalizedFile = SourcePausePointPathNormalizer.ToForwardSlashes(requestedFile);
            string absoluteFilePath = Path.Combine(UnityCliLoopPathResolver.GetProjectRoot(), normalizedFile);
            if (!File.Exists(absoluteFilePath))
            {
                return string.Empty;
            }

            return File.ReadAllText(absoluteFilePath);
        }
    }
}
