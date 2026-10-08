using System.Collections.Generic;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Remembers whether a source file matched its snapshot, together with the file's length and
    /// last write time, so an unchanged file need not be read again.
    /// </summary>
    internal sealed class HotReloadSiblingVerdictCache
    {
        // Why both paths in the key: the same file compared with another assembly generation's
        // snapshot is a different verdict, and the same snapshot can be compared with another file.
        private readonly Dictionary<(string snapshotPath, string sourcePath), Entry> _entries =
            new Dictionary<(string snapshotPath, string sourcePath), Entry>();

        // Why a lock: the rebind planner compares on a thread-pool continuation while
        // compilationStarted clears the memo on the main thread.
        private readonly object _gate = new object();

        /// <summary>
        /// Returns true with the remembered verdict when the pair was recorded with the same stamp.
        /// </summary>
        /// <remarks>
        /// Why a changed stamp reads as no verdict: the caller then has a single path that reads,
        /// compares, and records again, so a stale verdict is never returned.
        /// </remarks>
        internal bool TryGetVerdict(
            string snapshotPath,
            string sourcePath,
            long length,
            long lastWriteTimeUtcTicks,
            out bool matchesSnapshot)
        {
            Debug.Assert(!string.IsNullOrEmpty(snapshotPath), "snapshotPath must not be null or empty.");
            Debug.Assert(!string.IsNullOrEmpty(sourcePath), "sourcePath must not be null or empty.");

            Entry entry;
            lock (_gate)
            {
                if (!_entries.TryGetValue((snapshotPath, sourcePath), out entry))
                {
                    matchesSnapshot = false;
                    return false;
                }
            }

            if (entry.Length != length || entry.LastWriteTimeUtcTicks != lastWriteTimeUtcTicks)
            {
                matchesSnapshot = false;
                return false;
            }

            matchesSnapshot = entry.MatchesSnapshot;
            return true;
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

            lock (_gate)
            {
                _entries[(snapshotPath, sourcePath)] = new Entry(length, lastWriteTimeUtcTicks, matchesSnapshot);
            }
        }

        internal void Clear()
        {
            lock (_gate)
            {
                _entries.Clear();
            }
        }

        internal int Count
        {
            get
            {
                lock (_gate)
                {
                    return _entries.Count;
                }
            }
        }

        private readonly struct Entry
        {
            internal readonly long Length;
            internal readonly long LastWriteTimeUtcTicks;
            internal readonly bool MatchesSnapshot;

            internal Entry(long length, long lastWriteTimeUtcTicks, bool matchesSnapshot)
            {
                Length = length;
                LastWriteTimeUtcTicks = lastWriteTimeUtcTicks;
                MatchesSnapshot = matchesSnapshot;
            }
        }
    }
}
