#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.Runtime
{
    /// <summary>
    /// Collects the current pause point statuses after synchronizing any elapsed capture windows.
    /// </summary>
    internal static class UloopPausePointStatusSnapshotCollector
    {
        public static int CountActiveEntries(IEnumerable<UloopPausePointEntry> entries)
        {
            Debug.Assert(entries != null, "entries must not be null");

            int count = 0;
            foreach (UloopPausePointEntry entry in entries)
            {
                if (entry.IsEnabled)
                {
                    count++;
                }
            }

            return count;
        }

        public static IReadOnlyList<UloopPausePointSnapshot> Collect(
            IEnumerable<UloopPausePointEntry> entries,
            DateTime now,
            IUloopPausePointPauseController pauseController,
            Func<UloopPausePointEntry, DateTime, bool> tryExpire,
            Action resumeEditorPause)
        {
            Debug.Assert(entries != null, "entries must not be null");
            Debug.Assert(pauseController != null, "pauseController must not be null");
            Debug.Assert(tryExpire != null, "tryExpire must not be null");
            Debug.Assert(resumeEditorPause != null, "resumeEditorPause must not be null");

            bool anyExpired = false;
            List<UloopPausePointEntry> entriesToSnapshot = new();
            foreach (UloopPausePointEntry entry in entries)
            {
                entriesToSnapshot.Add(entry);
                if (tryExpire(entry, now))
                {
                    anyExpired = true;
                }
            }

            if (anyExpired)
            {
                resumeEditorPause();
            }

            List<UloopPausePointSnapshot> snapshots = new();
            foreach (UloopPausePointEntry entry in entriesToSnapshot)
            {
                snapshots.Add(entry.ToSnapshot(now, pauseController));
            }

            snapshots.Sort((left, right) => string.Compare(left.Id, right.Id, StringComparison.Ordinal));
            return snapshots;
        }
    }
}
#endif
