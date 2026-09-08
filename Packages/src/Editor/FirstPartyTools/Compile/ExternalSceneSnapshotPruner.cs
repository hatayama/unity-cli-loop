using System;
using System.Collections.Generic;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Drops Scene fingerprints that no longer belong to a Scene open in the Editor.
    /// </summary>
    internal static class ExternalSceneSnapshotPruner
    {
        /// <summary>
        /// Removes every snapshot whose Scene is not in <paramref name="openScenePaths"/> and returns the removed paths.
        /// Fingerprints recorded while a Scene was loaded only at runtime must not survive into Edit Mode,
        /// where they would otherwise trigger a reload for a Scene the user never opened.
        /// </summary>
        public static string[] RemoveSnapshotsForScenesNotOpen(
            Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)> snapshots,
            string[] openScenePaths)
        {
            Debug.Assert(snapshots != null, "snapshots must not be null");
            Debug.Assert(openScenePaths != null, "openScenePaths must not be null");

            HashSet<string> openScenePathSet = new HashSet<string>(openScenePaths, StringComparer.Ordinal);
            List<string> removedScenePaths = new List<string>();
            foreach (string assetPath in snapshots.Keys)
            {
                if (!openScenePathSet.Contains(assetPath))
                {
                    removedScenePaths.Add(assetPath);
                }
            }

            for (int i = 0; i < removedScenePaths.Count; i++)
            {
                snapshots.Remove(removedScenePaths[i]);
            }

            return removedScenePaths.ToArray();
        }
    }
}
