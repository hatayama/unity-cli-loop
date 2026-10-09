using System;
using System.IO;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    // Removes the snapshot directories of an assembly's earlier builds once the current build's
    // snapshot is complete.
    internal static partial class HotReloadSourceSnapshotter
    {
        private static void DeleteStaleSnapshotDirectories(
            string snapshotRoot,
            string assemblyName,
            string currentSnapshotDirectory)
        {
            string currentFullPath = Path.GetFullPath(currentSnapshotDirectory);
            string prefix = assemblyName + "-";
            foreach (string candidateDirectory in Directory.GetDirectories(snapshotRoot, assemblyName + "-*"))
            {
                if (string.Equals(
                        Path.GetFullPath(candidateDirectory),
                        currentFullPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string directoryName = Path.GetFileName(candidateDirectory);
                if (directoryName.Length <= prefix.Length)
                {
                    continue;
                }

                // Why: the glob is a prefix match, so hyphenated sibling assembly names also match.
                // Only delete when the suffix after "<assemblyName>-" is exactly an Mvid in "N"
                // format, optionally followed by the capture-only .tmp suffix.
                string mvidCandidate = directoryName.Substring(prefix.Length);
                if (mvidCandidate.EndsWith(
                        HotReloadSourceSnapshotLayout.IncompleteDirectorySuffix,
                        StringComparison.Ordinal))
                {
                    mvidCandidate = mvidCandidate.Substring(
                        0,
                        mvidCandidate.Length - HotReloadSourceSnapshotLayout.IncompleteDirectorySuffix.Length);
                }

                if (!Guid.TryParseExact(mvidCandidate, "N", out Guid _))
                {
                    continue;
                }

                // Capture and cleanup run serially on the main thread, and cleanup starts only
                // after the current capture's Move succeeds, so no active .tmp can be removed here.
                Directory.Delete(candidateDirectory, recursive: true);
            }
        }
    }
}
