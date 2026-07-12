#if UNITY_EDITOR
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.Runtime
{
    /// <summary>
    /// Stores enabled pause point state for the current Editor domain. All members except
    /// IsArmed are main-thread-only by convention; IsArmed is the one entry point an
    /// off-main-thread Harmony-injected Capture call may reach, so Entries is a
    /// ConcurrentDictionary to make that cross-thread read safe.
    /// </summary>
    internal static class UloopPausePointRegistry
    {
        public const int DefaultTimeoutSeconds = 30;

        private static readonly ConcurrentDictionary<string, UloopPausePointEntry> Entries = new();
        private static IUloopPausePointPauseController _pauseController = new UnityEditorPausePointPauseController();
        private static Func<DateTime> _nowProvider = () => DateTime.UtcNow;
        private static int _nextGeneration;
        private static int _nextHitSequence;
        private static UloopPausePointSnapshot _latestHitSnapshot;
        // One input can hit several markers in the same frame; tools need the full list,
        // not just the latest hit, to report every marker that interrupted them.
        private static readonly List<UloopPausePointSnapshot> _hitSnapshots = new();

        // Source pause points are patched into IL by an Editor-only tool assembly this Runtime
        // assembly must not reference directly (patching is an outer/implementation concern; this
        // registry is the inner layer). That tool assembly wires its own Unpatch/UnpatchAll into
        // these hooks the first time it patches a method, so every Clear/ClearAll caller -
        // including the Infrastructure CLI bridge, which also must not reference the tool
        // assembly - removes the underlying Harmony patch without knowing it exists.
        public static Action<string> OnCleared { get; set; }
        public static Action OnClearedAll { get; set; }

        public static UloopPausePointSnapshot Enable(string id, int timeoutSeconds)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(id), "id must not be null or empty");
            Debug.Assert(timeoutSeconds > 0, "timeoutSeconds must be greater than zero");

            DateTime now = NowUtc();
            int generation = ++_nextGeneration;
            UloopPausePointEntry entry = new(id, timeoutSeconds, now, generation);
            Entries[id] = entry;
            ClearLatestHitSnapshotIfMatches(id);
            return entry.ToSnapshot(now, _pauseController);
        }

        public static UloopPausePointSnapshot Clear(string id)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(id), "id must not be null or empty");

            OnCleared?.Invoke(id);

            DateTime now = NowUtc();
            if (!Entries.ContainsKey(id))
            {
                return UloopPausePointSnapshot.NotEnabled(id, _pauseController);
            }

            UloopPausePointEntry entry = Entries[id];
            // Resolve expiry first so a clear after the timeout reports "expired", not a normal clear.
            entry.ExpireIfNeeded(now);
            string message = entry.Status switch
            {
                UloopPausePointStatus.Hit => "Pause point was already hit (auto-disarmed); nothing to clear.",
                UloopPausePointStatus.Expired => "Pause point had already expired before being hit; nothing to clear.",
                UloopPausePointStatus.Cleared => "Pause point was already cleared.",
                _ => "Pause point cleared."
            };
            entry.MarkCleared(UloopPausePointClearedReason.ExplicitClear, message);
            ClearLatestHitSnapshotIfMatches(id);
            return entry.ToSnapshot(now, _pauseController);
        }

        public static UloopPausePointClearAllResult ClearAll(
            string clearedReason = UloopPausePointClearedReason.ClearAll)
        {
            OnClearedAll?.Invoke();

            DateTime now = NowUtc();
            List<string> clearedIds = new();
            foreach (UloopPausePointEntry entry in Entries.Values)
            {
                if (entry.Status == UloopPausePointStatus.Cleared)
                {
                    continue;
                }

                // Resolve expiry first so ClearAll after timeout keeps AfterExpired visibility.
                entry.ExpireIfNeeded(now);
                clearedIds.Add(entry.Id);
                entry.MarkCleared(clearedReason);
            }
            ClearLatestHitSnapshot();

            UloopPausePointEditorStateSnapshot editorState = UloopPausePointEditorStateSnapshot.FromController(
                _pauseController,
                UloopPausePointEditorStateCapturedAt.ClearAll);
            return new UloopPausePointClearAllResult(clearedIds.Count, now, editorState, clearedIds.ToArray());
        }

        public static UloopPausePointSnapshot GetStatus(string id)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(id), "id must not be null or empty");

            DateTime now = NowUtc();
            if (!Entries.ContainsKey(id))
            {
                return UloopPausePointSnapshot.NotEnabled(id, _pauseController);
            }

            UloopPausePointEntry entry = Entries[id];
            entry.ExpireIfNeeded(now);
            return entry.ToSnapshot(now, _pauseController);
        }

        public static UloopPausePointSnapshot Hit(string id)
        {
            return HitCore(id, Array.Empty<UloopCapturedVariable>(), false);
        }

        public static UloopPausePointSnapshot HitWithCapturedVariables(
            string id, IReadOnlyList<UloopCapturedVariable> capturedVariables, bool capturedVariablesTruncated)
        {
            Debug.Assert(capturedVariables != null, "capturedVariables must not be null");

            return HitCore(id, capturedVariables, capturedVariablesTruncated);
        }

        // Returns after a single dictionary lookup when the id is not armed. Harmony-injected
        // Capture calls take this inactive path almost always, so keeping it allocation-free here matters.
        public static bool IsArmed(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            return Entries.TryGetValue(id, out UloopPausePointEntry entry) && entry.IsEnabled;
        }

        private static UloopPausePointSnapshot HitCore(
            string id, IReadOnlyList<UloopCapturedVariable> capturedVariables, bool capturedVariablesTruncated)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                Debug.Assert(false, "id must not be null or empty");
                return UloopPausePointSnapshot.NotEnabled(id ?? string.Empty, _pauseController);
            }

            DateTime now = NowUtc();
            if (!Entries.ContainsKey(id))
            {
                return UloopPausePointSnapshot.NotEnabled(id, _pauseController);
            }

            UloopPausePointEntry entry = Entries[id];
            entry.ExpireIfNeeded(now);
            if (!entry.IsEnabled)
            {
                // Why: delayed main-thread hits can lose the race to Clear/ClearAll; surface that race.
                if (entry.Status == UloopPausePointStatus.Cleared)
                {
                    entry.MarkLateHitDiscardedAfterClear();
                    Debug.LogWarning(
                        $"Pause point '{id}' was hit after it was cleared " +
                        $"(ClearedReason={entry.ClearedReason}, StatusBeforeClear={entry.StatusBeforeClear}). " +
                        "The late hit was discarded.");
                }

                return entry.ToSnapshot(now, _pauseController);
            }

            _pauseController.Pause();
            int hitSequence = ++_nextHitSequence;
            entry.RecordHitWithCapturedVariables(
                now, _pauseController.IsPlaying, _pauseController.IsPaused, hitSequence,
                capturedVariables, capturedVariablesTruncated);
            UloopPausePointSnapshot snapshot = entry.ToSnapshot(now, _pauseController);
            _latestHitSnapshot = snapshot;
            _hitSnapshots.RemoveAll(hitSnapshot => hitSnapshot.Id == id);
            _hitSnapshots.Add(snapshot);
            return snapshot;
        }

        public static IReadOnlyList<UloopPausePointSnapshot> GetHitSnapshots()
        {
            return _hitSnapshots;
        }

        public static UloopPausePointSnapshot GetLatestHitSnapshot()
        {
            return _latestHitSnapshot;
        }

        public static void ClearLatestHitSnapshot()
        {
            _latestHitSnapshot = null;
            _hitSnapshots.Clear();
        }

        private static void ClearLatestHitSnapshotIfMatches(string id)
        {
            _hitSnapshots.RemoveAll(hitSnapshot => hitSnapshot.Id == id);
            if (_latestHitSnapshot == null)
            {
                return;
            }

            if (_latestHitSnapshot.Id != id)
            {
                return;
            }

            _latestHitSnapshot = null;
        }

        public static void ConfigureForTests(IUloopPausePointPauseController pauseController, Func<DateTime> nowProvider)
        {
            Debug.Assert(pauseController != null, "pauseController must not be null");
            Debug.Assert(nowProvider != null, "nowProvider must not be null");

            _pauseController = pauseController;
            _nowProvider = nowProvider;
        }

        public static void ResetForTests()
        {
            Entries.Clear();
            _nextGeneration = 0;
            _nextHitSequence = 0;
            _latestHitSnapshot = null;
            _hitSnapshots.Clear();
            _pauseController = new UnityEditorPausePointPauseController();
            _nowProvider = () => DateTime.UtcNow;
        }

        private static DateTime NowUtc()
        {
            DateTime now = _nowProvider();
            return now.Kind == DateTimeKind.Utc ? now : now.ToUniversalTime();
        }
    }
}
#endif
