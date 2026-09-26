using System.Collections.Generic;
using System.IO;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Lists the owner files of introduced types Play entry discarded, or whose later additions
    /// revert-all dropped, that are still on disk, for an omitted --files run to select again.
    /// </summary>
    internal static class HotReloadDroppedIntroducedSourceFiles
    {
        // Why a file the user deleted is left out: selecting it would only add a failed row, and
        // pointing at a file the user removed tells them nothing they can act on.
        internal static IReadOnlyList<string> ListExistingOnDisk()
        {
            IReadOnlyList<string> recordedPaths = HotReloadPlayModeEntryDropSourceLedger.GetProjectRelativePaths();
            List<string> existingPaths = new List<string>(recordedPaths.Count);
            if (recordedPaths.Count == 0)
            {
                return existingPaths;
            }

            // Resolved the way the run resolves a sibling file it reads from disk: the
            // project-relative path joined to the project root.
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            for (int index = 0; index < recordedPaths.Count; index++)
            {
                string absolutePath = Path.GetFullPath(
                    Path.Combine(
                        projectRoot,
                        recordedPaths[index].Replace('/', Path.DirectorySeparatorChar)));
                if (File.Exists(absolutePath))
                {
                    existingPaths.Add(recordedPaths[index]);
                }
            }

            return existingPaths;
        }
    }
}
