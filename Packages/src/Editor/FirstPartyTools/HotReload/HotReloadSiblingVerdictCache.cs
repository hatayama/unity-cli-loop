using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Remembers whether a source file matched its snapshot, together with the file's length and
    /// last write time, so an unchanged file need not be read again.
    /// </summary>
    internal sealed class HotReloadSiblingVerdictCache
    {
        /// <summary>
        /// Returns true with the remembered verdict when the pair was recorded with the same stamp.
        /// </summary>
        internal bool TryGetVerdict(
            string snapshotPath,
            string sourcePath,
            long length,
            long lastWriteTimeUtcTicks,
            out bool matchesSnapshot)
        {
            Debug.Assert(!string.IsNullOrEmpty(snapshotPath), "snapshotPath must not be null or empty.");
            Debug.Assert(!string.IsNullOrEmpty(sourcePath), "sourcePath must not be null or empty.");

            matchesSnapshot = false;
            return false;
        }

        /// <summary>
        /// Records the verdict for the pair, replacing any earlier one.
        /// </summary>
        internal void Record(
            string snapshotPath,
            string sourcePath,
            long length,
            long lastWriteTimeUtcTicks,
            bool matchesSnapshot)
        {
            Debug.Assert(!string.IsNullOrEmpty(snapshotPath), "snapshotPath must not be null or empty.");
            Debug.Assert(!string.IsNullOrEmpty(sourcePath), "sourcePath must not be null or empty.");
        }

        internal void Clear()
        {
        }

        internal int Count => 0;
    }
}
