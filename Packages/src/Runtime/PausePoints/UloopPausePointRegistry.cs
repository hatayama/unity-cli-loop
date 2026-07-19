#if UNITY_EDITOR
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.Runtime
{
    /// <summary>
    /// Stores enabled pause point state for the current Editor domain. All members except
    /// IsArmed and ResumeEditorPauseForClientDisconnect are main-thread-only by convention;
    /// IsArmed is the Harmony Capture entry point, and the disconnect resume path only sets a
    /// pending flag so thread-pool callers never touch EditorApplication.isPaused.
    /// </summary>
    internal static class UloopPausePointRegistry
    {
        public const int DefaultTimeoutSeconds = 30;
        public const int DefaultMaxHistory = 20;
        public const int MaxHistoryLimit = 100;

        private static readonly ConcurrentDictionary<string, UloopPausePointEntry> Entries = new();
        private static IUloopPausePointPauseController _pauseController = new UnityEditorPausePointPauseController();
        private static Func<DateTime> _nowProvider = () => DateTime.UtcNow;
        private static int _nextGeneration;
        private static int _nextHitSequence;
        // Why Interlocked: disconnect monitor / DisconnectAllClients run on the thread pool.
        private static int _pendingClientDisconnectResume;
        private static UloopPausePointSnapshot _latestHitSnapshot;
        // Set when a hit pauses the Editor (see HitCore), cleared when ResumeEditorPause runs.
        // While set, no entry's capture window is allowed to expire (see TryExpire): a marker's
        // own hit freezes every marker's countdown for the duration of the inspection pause,
        // and the frozen duration is credited back to each entry's ExpiresAtUtc on resume.
        private static DateTime? _pauseWindowStartUtc;
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

        public static UloopPausePointSnapshot Enable(
            string id,
            int timeoutSeconds,
            string mode = UloopPausePointCaptureMode.SingleShot,
            int maxHistory = DefaultMaxHistory)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(id), "id must not be null or empty");
            Debug.Assert(timeoutSeconds > 0, "timeoutSeconds must be greater than zero");
            Debug.Assert(IsSupportedMode(mode), "mode must be a supported pause point capture mode");
            Debug.Assert(maxHistory > 0, "maxHistory must be greater than zero");
            Debug.Assert(maxHistory <= MaxHistoryLimit, "maxHistory must not exceed the history limit");

            DateTime now = NowUtc();
            int generation = ++_nextGeneration;
            UloopPausePointEntry entry = new(id, timeoutSeconds, mode, maxHistory, now, generation);
            Entries[id] = entry;
            // Why not clear the raw capture holder here: a re-enable does not resume Unity, so the
            // paused-window constraint (see UloopPausePointRawCaptureHolder's class comment) is not
            // violated by keeping the previous hit's live references across a same-id re-enable.
            ForgetHitSnapshotForId(id);
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
            TryExpire(entry, now);
            string message = !entry.IsEnabled && entry.Status == UloopPausePointStatus.Hit
                ? "Pause point was already hit (auto-disarmed); nothing to clear."
                : entry.Status switch
            {
                UloopPausePointStatus.Hit =>
                    $"Pause point cleared after {entry.HitCount} hit(s); capture history is preserved.",
                UloopPausePointStatus.Expired when entry.HitCount > 0 =>
                    "Pause point capture window had already expired; nothing to clear.",
                UloopPausePointStatus.Expired =>
                    "Pause point had already expired before being hit; nothing to clear.",
                UloopPausePointStatus.Cleared => "Pause point was already cleared.",
                _ => "Pause point cleared."
            };
            entry.MarkCleared(UloopPausePointClearedReason.ExplicitClear, message);
            ClearHitSnapshotAndRawCaptureForId(id);
            // Why always Resume: Option B does not distinguish manual vs pause-point pause.
            ResumeEditorPause();
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
                TryExpire(entry, now);
                clearedIds.Add(entry.Id);
                entry.MarkCleared(clearedReason);
            }
            ClearLatestHitSnapshot();
            UloopPausePointRawCaptureHolder.Clear();
            // Why always Resume: Option B does not distinguish manual vs pause-point pause.
            ResumeEditorPause();

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
            if (TryExpire(entry, now))
            {
                ResumeEditorPause();
            }

            return entry.ToSnapshot(now, _pauseController);
        }

        public static UloopPausePointSnapshot Hit(string id)
        {
            return HitCore(id, null, Array.Empty<UloopCapturedVariable>(), false);
        }

        public static UloopPausePointSnapshot HitWithCapturedVariables(
            string id, IReadOnlyList<UloopCapturedVariable> capturedVariables, bool capturedVariablesTruncated)
        {
            Debug.Assert(capturedVariables != null, "capturedVariables must not be null");

            return HitCore(id, null, capturedVariables, capturedVariablesTruncated);
        }

        public static UloopPausePointSnapshot HitWithCapturedFrame(
            string id,
            UloopPausePointCapturedVariableFrame capturedFrame,
            IReadOnlyList<UloopCapturedVariable> capturedVariables,
            bool capturedVariablesTruncated)
        {
            Debug.Assert(capturedFrame != null, "capturedFrame must not be null");
            Debug.Assert(capturedVariables != null, "capturedVariables must not be null");

            return HitCore(id, capturedFrame, capturedVariables, capturedVariablesTruncated);
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
            string id,
            UloopPausePointCapturedVariableFrame capturedFrame,
            IReadOnlyList<UloopCapturedVariable> capturedVariables,
            bool capturedVariablesTruncated)
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
            if (TryExpire(entry, now))
            {
                ResumeEditorPause();
                return entry.ToSnapshot(now, _pauseController);
            }

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

            if (entry.Mode != UloopPausePointCaptureMode.Trace)
            {
                _pauseController.Pause();
                _pauseWindowStartUtc ??= now;
            }

            int hitSequence = ++_nextHitSequence;
            int frameCount = Time.frameCount;
            entry.RecordHitWithCapturedVariables(
                now, _pauseController.IsPlaying, _pauseController.IsPaused, hitSequence,
                frameCount, capturedVariables, capturedVariablesTruncated);
            UloopPausePointSnapshot snapshot = entry.ToSnapshot(now, _pauseController);
            _latestHitSnapshot = snapshot;
            _hitSnapshots.RemoveAll(hitSnapshot => hitSnapshot.Id == id);
            _hitSnapshots.Add(snapshot);
            if (capturedFrame != null)
            {
                UloopPausePointRawCaptureHolder.Store(capturedFrame, id);
            }

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
            UloopPausePointRawCaptureHolder.Clear();
        }

        /// <summary>
        /// Expires capture windows that have elapsed and resumes the Editor when any expire.
        /// Used while paused so expiry still runs without a CLI status poll.
        /// </summary>
        public static void ApplyCaptureWindowExpirations()
        {
            DateTime now = NowUtc();
            bool anyExpired = false;
            foreach (UloopPausePointEntry entry in Entries.Values)
            {
                if (TryExpire(entry, now))
                {
                    anyExpired = true;
                }
            }

            if (anyExpired)
            {
                ResumeEditorPause();
            }
        }

        /// <summary>
        /// Requests Editor resume when a CLI client drops mid-request or the bridge disconnects all clients.
        /// Why flag only: callers run on the thread pool where Unity Editor APIs are unsafe; the main-thread
        /// Editor update pump applies the resume via ApplyPendingClientDisconnectResume.
        /// Why not on every short-lived command close: that would resume immediately after await-pause-point
        /// returns a Hit and break the paused inspection workflow.
        /// </summary>
        public static void ResumeEditorPauseForClientDisconnect()
        {
            Interlocked.Exchange(ref _pendingClientDisconnectResume, 1);
        }

        /// <summary>
        /// Consumes a pending client-disconnect resume on the Editor main thread.
        /// </summary>
        public static void ApplyPendingClientDisconnectResume()
        {
            if (Interlocked.Exchange(ref _pendingClientDisconnectResume, 0) == 0)
            {
                return;
            }

            // Why discard when already running: Option B still clears a stale request without
            // calling Resume on an Editor that is not paused.
            if (!_pauseController.IsPaused)
            {
                return;
            }

            ResumeEditorPause();
        }

        private static void ResumeEditorPause()
        {
            CreditPauseWindowAndClose(NowUtc());
            _pauseController.Resume();
        }

        /// <summary>
        /// Closes an open pause window when the Editor was unpaused through a path that never
        /// calls back into this registry - control-play-mode's Play/Stop
        /// (<c>ControlPlayModeUseCase</c> sets <c>EditorApplication.isPaused</c> directly) or the
        /// Editor's own pause button. Without this, a window left open by such an external resume
        /// would freeze every marker's countdown forever and later over-credit the elapsed
        /// wall-clock time back onto ExpiresAtUtc. Call every main-thread Editor update.
        /// </summary>
        public static void ClosePauseWindowIfEditorResumedExternally()
        {
            if (!_pauseWindowStartUtc.HasValue || _pauseController.IsPaused)
            {
                return;
            }

            CreditPauseWindowAndClose(NowUtc());
        }

        // Credits the frozen duration (now - max(pauseWindowStart, EnabledAtUtc)) back onto every
        // entry's ExpiresAtUtc and closes the window. A no-op when no window is open.
        private static void CreditPauseWindowAndClose(DateTime now)
        {
            if (!_pauseWindowStartUtc.HasValue)
            {
                return;
            }

            DateTime pauseWindowStart = _pauseWindowStartUtc.Value;
            _pauseWindowStartUtc = null;
            foreach (UloopPausePointEntry entry in Entries.Values)
            {
                entry.ExtendExpiryForPause(pauseWindowStart, now);
            }
        }

        // While a hit has the Editor paused, no entry may expire: the countdown is frozen for
        // everyone until the open window is closed and credits the frozen duration back (see
        // CreditPauseWindowAndClose). Without this gate, TryExpire callers would still expire a
        // different marker mid-inspection even though wall-clock time during the pause should
        // not count against it.
        private static bool TryExpire(UloopPausePointEntry entry, DateTime now)
        {
            if (_pauseWindowStartUtc.HasValue)
            {
                return false;
            }

            return entry.ExpireIfNeeded(now);
        }

        // Drops the id's own hit-history entry and, if it currently owns the latest hit, that
        // pointer too - but never touches the raw capture holder. Enable() uses this so a same-id
        // re-enable while paused only resets the entry's generation bookkeeping.
        private static void ForgetHitSnapshotForId(string id)
        {
            _hitSnapshots.RemoveAll(hitSnapshot => hitSnapshot.Id == id);
            if (_latestHitSnapshot != null && _latestHitSnapshot.Id == id)
            {
                _latestHitSnapshot = null;
            }
        }

        // Same as ForgetHitSnapshotForId, plus clears the raw capture holder when the holder's
        // captured snapshot belongs to the cleared id. Clear(id) uses this because an explicit
        // clear is documented to drop captures. Ownership is checked against the holder itself
        // (not _latestHitSnapshot) because Enable()'s ForgetHitSnapshotForId already nulls out
        // _latestHitSnapshot on a same-id re-enable, which would otherwise make a later Clear(id)
        // think it no longer owns a holder it actually still does.
        private static void ClearHitSnapshotAndRawCaptureForId(string id)
        {
            ForgetHitSnapshotForId(id);
            if (UloopPausePointRawCaptureHolder.GetCapturedPausePointId() == id)
            {
                UloopPausePointRawCaptureHolder.Clear();
            }
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
            _pauseWindowStartUtc = null;
            Interlocked.Exchange(ref _pendingClientDisconnectResume, 0);
            _pauseController = new UnityEditorPausePointPauseController();
            _nowProvider = () => DateTime.UtcNow;
            UloopPausePointRawCaptureHolder.Clear();
        }

        private static DateTime NowUtc()
        {
            DateTime now = _nowProvider();
            return now.Kind == DateTimeKind.Utc ? now : now.ToUniversalTime();
        }

        private static bool IsSupportedMode(string mode)
        {
            return mode == UloopPausePointCaptureMode.SingleShot ||
                   mode == UloopPausePointCaptureMode.Continuous ||
                   mode == UloopPausePointCaptureMode.Trace;
        }
    }
}
#endif
