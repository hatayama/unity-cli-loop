using System;
using System.Collections.Generic;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Decides which dirty open assets focus return may save before Unity refreshes the asset database.
    /// </summary>
    internal static class ExternalAssetFocusReturnSavePolicy
    {
        /// <summary>
        /// Selects the dirty assets whose file changed or disappeared on disk while the Editor was unfocused.
        /// Only those would otherwise raise Unity's external-change dialog, so only those trade the disk
        /// state for the in-memory state; every other dirty asset keeps its unsaved edits untouched.
        /// An asset without a snapshot is skipped because no external change can be proven for it.
        /// </summary>
        internal static string[] SelectDirtyAssetsToSave(
            (string AssetPath, bool IsDirty)[] openAssets,
            Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)> snapshots,
            Func<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)> readFingerprint)
        {
            Debug.Assert(openAssets != null, "openAssets must not be null");
            Debug.Assert(snapshots != null, "snapshots must not be null");
            Debug.Assert(readFingerprint != null, "readFingerprint must not be null");

            List<string> selected = new List<string>();
            for (int i = 0; i < openAssets.Length; i++)
            {
                (string AssetPath, bool IsDirty) asset = openAssets[i];
                if (!asset.IsDirty)
                {
                    continue;
                }

                if (!snapshots.TryGetValue(
                        asset.AssetPath,
                        out (bool Exists, DateTime LastWriteTimeUtc, long Length) snapshot))
                {
                    continue;
                }

                if (ExternalAssetFileStateComparer.HasSameFileState(snapshot, readFingerprint(asset.AssetPath)))
                {
                    continue;
                }

                selected.Add(asset.AssetPath);
            }

            return selected.ToArray();
        }
    }
}
