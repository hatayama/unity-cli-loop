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
    /// IsArmed, RecordMethodEntry, GetHitWhenCondition, RecordHitWhenSkip, RecordHitWhenError,
    /// and ResumeEditorPauseForClientDisconnect are main-thread-only by convention; the capture
    /// accessors are Harmony entry points, and the disconnect resume path only sets a pending flag
    /// so thread-pool callers never touch EditorApplication.isPaused.
    /// </summary>
    internal static partial class UloopPausePointRegistry
    {
        public const int DefaultTimeoutSeconds = 30;
        public const int DefaultMaxHistory = 20;
        public const int MaxHistoryLimit = 100;
        public const int DefaultMaxPreviewElements = 10;
        public const int MaxPreviewElementsLimit = 1000;
        public const int DefaultMaxCallerFrames = 2;
        public const int MaxCallerFramesLimit = 8;

        private static readonly ConcurrentDictionary<string, UloopPausePointEntry> Entries = new();
        private static readonly HashSet<string> MethodEntryInstrumentedIds = new();
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
        // Fires once Clear(id) has fully resolved entry state (after MarkCleared), unlike
        // OnCleared above which fires first, before TryExpire/MarkCleared run - too early to read
        // HitCount/StatusBeforeClear. Lets tool-assembly-only diagnostics (physics dispatch
        // misses) subscribe here instead of duplicating that inline check in both Clear callers
        // (PausePointUseCase and the Infrastructure CLI bridge), the same way OnCleared/
        // OnClearedAll already let SourcePausePointPatcher subscribe once instead of every caller
        // referencing it directly.
        public static Action<string, int, string> OnClearResolved { get; set; }

        // Error responses built outside the registry still need a truthful editor state; this
        // exposes the same controller-backed capture the snapshot paths use.
        public static UloopPausePointEditorStateSnapshot CaptureEditorState()
        {
            return UloopPausePointEditorStateSnapshot.FromController(
                _pauseController,
                UloopPausePointEditorStateCapturedAt.Current);
        }

        public static UloopPausePointSnapshot Enable(
            string id,
            int timeoutSeconds,
            string mode = UloopPausePointCaptureMode.SingleShot,
            int maxHistory = DefaultMaxHistory,
            int maxPreviewElements = DefaultMaxPreviewElements,
            int maxCallerFrames = DefaultMaxCallerFrames,
            string hitWhen = "",
            UloopPausePointHitWhenCondition hitWhenCondition = null)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(id), "id must not be null or empty");
            Debug.Assert(timeoutSeconds > 0, "timeoutSeconds must be greater than zero");
            Debug.Assert(IsSupportedMode(mode), "mode must be a supported pause point capture mode");
            Debug.Assert(maxHistory > 0, "maxHistory must be greater than zero");
            Debug.Assert(maxHistory <= MaxHistoryLimit, "maxHistory must not exceed the history limit");
            Debug.Assert(maxPreviewElements > 0, "maxPreviewElements must be greater than zero");
            Debug.Assert(
                maxPreviewElements <= MaxPreviewElementsLimit,
                "maxPreviewElements must not exceed the preview element limit");
            Debug.Assert(maxCallerFrames >= 0, "maxCallerFrames must not be negative");
            Debug.Assert(
                maxCallerFrames <= MaxCallerFramesLimit,
                "maxCallerFrames must not exceed the caller-frame limit");
            Debug.Assert(
                string.IsNullOrEmpty(hitWhen) == (hitWhenCondition == null),
                "hitWhen and hitWhenCondition must both be set or both be empty");

            DateTime now = NowUtc();
            int generation = ++_nextGeneration;
            UloopPausePointEntry entry = new(
                id, timeoutSeconds, mode, maxHistory, maxPreviewElements, maxCallerFrames, now, generation,
                MethodEntryInstrumentedIds.Contains(id), hitWhen, hitWhenCondition);
            Entries[id] = entry;
            // Why not clear the raw capture holder here: a re-enable does not resume Unity, so the
            // paused-window constraint (see UloopPausePointRawCaptureHolder's class comment) is not
            // violated by keeping the previous hit's live references across a same-id re-enable.
            ForgetHitSnapshotForId(id);
            return entry.ToSnapshot(now, _pauseController);
        }

        /// <summary>
        /// Clears one marker. ClearedCount is 1 when this call actually transitions the entry
        /// to Cleared (from Enabled, Hit, or Expired), and 0 for unknown ids or already-cleared
        /// entries. Why not derive it from the snapshot: StatusBeforeClear survives a no-op
        /// second clear, so snapshot-based counting would treat a no-op as 1.
        /// </summary>
        public static (UloopPausePointSnapshot Snapshot, bool ResumedFromPause, int ClearedCount) Clear(
            string id,
            string clearedReason = UloopPausePointClearedReason.ExplicitClear)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(id), "id must not be null or empty");

            OnCleared?.Invoke(id);

            DateTime now = NowUtc();
            if (!Entries.ContainsKey(id))
            {
                return (UloopPausePointSnapshot.NotEnabled(id, _pauseController), false, 0);
            }

            UloopPausePointEntry entry = Entries[id];
            // Resolve expiry first so a clear after the timeout reports "expired", not a normal clear.
            TryExpire(entry, now);
            int clearedCount = entry.Status == UloopPausePointStatus.Cleared ? 0 : 1;
            // Why not claim leftover-patch removal: this Runtime registry cannot know whether a
            // patch exists. Id-only enables never call SourcePausePointPatcher, so OnCleared's
            // Unpatch hook no-ops when MethodById lacks the id.
            string message = !entry.IsEnabled && entry.Status == UloopPausePointStatus.Hit
                ? "Pause point was already auto-disarmed by its hit; this clear marked the record cleared. Capture history is preserved."
                : entry.Status switch
            {
                UloopPausePointStatus.Hit =>
                    $"Pause point cleared after {entry.HitCount} hit(s); capture history is preserved.",
                UloopPausePointStatus.Expired when entry.HitCount > 0 =>
                    "Pause point capture window had already expired; this clear marked the record cleared.",
                UloopPausePointStatus.Expired =>
                    "Pause point had already expired before being hit; this clear marked the record cleared.",
                UloopPausePointStatus.Cleared => "Pause point was already cleared.",
                _ => "Pause point cleared."
            };
            entry.MarkCleared(clearedReason, message);
            OnClearResolved?.Invoke(id, entry.HitCount, entry.StatusBeforeClear);
            ClearHitSnapshotAndRawCaptureForId(id);
            // Why only when this marker owns the pause: a clear must not steal a pause that a
            // different marker's hit is holding (timeout auto-clear of a never-hit second marker
            // used to resume the first marker's inspection pause). It also must not steal a
            // manual pause the user set outside the pause-point workflow (control-play-mode
            // --action Pause or the Editor pause button). Manual pauses leave no open window, so
            // they are left untouched. ClearAll still resumes any pause-point-owned pause.
            // (Client disconnect and expiry still resume unconditionally: those paths must
            // guarantee release even for a manual pause.)
            bool resumedFromPause = ResumeEditorPauseIfOwnedByMarker(id);
            return (entry.ToSnapshot(now, _pauseController), resumedFromPause, clearedCount);
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
            // Why only when pause-point-owned: like Clear, a bulk clear must not resume a manual
            // pause the user set outside the pause-point workflow. It resumes only while a pause
            // window is open (a pause-point hit is holding the Editor paused). Client disconnect
            // and expiry still resume unconditionally to guarantee release.
            bool resumedFromPause = ResumeEditorPauseIfOwnedByPausePoint();

            UloopPausePointEditorStateSnapshot editorState = UloopPausePointEditorStateSnapshot.FromController(
                _pauseController,
                UloopPausePointEditorStateCapturedAt.ClearAll);
            return new UloopPausePointClearAllResult(
                clearedIds.Count, now, editorState, clearedIds.ToArray(), resumedFromPause);
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

        /// <summary>
        /// Returns current snapshots for every registered pause point in deterministic marker-id order.
        /// </summary>
        public static IReadOnlyList<UloopPausePointSnapshot> GetAllStatuses()
        {
            return UloopPausePointStatusSnapshotCollector.Collect(
                Entries.Values,
                NowUtc(),
                _pauseController,
                TryExpire,
                ResumeEditorPause);
        }

        /// <summary>
        /// Marks whether an armed marker could not stay live across a hot-reload patch transition.
        /// When <paramref name="suppressed"/> is false, <paramref name="reason"/> is cleared to null.
        /// No-op when the id is unknown.
        /// </summary>
        public static void SetSuppressedByHotReload(string id, bool suppressed, string reason)
        {
            if (!Entries.TryGetValue(id, out UloopPausePointEntry entry))
            {
                return;
            }

            entry.SuppressedByHotReload = suppressed;
            entry.SuppressedByHotReloadReason = suppressed ? reason : null;
        }

        /// <summary>
        /// Marks whether the marker's instrumentation currently targets a hot-reload shim body.
        /// No-op when the id is unknown.
        /// </summary>
        public static void SetRetargetedToHotReloadPatch(string id, bool value)
        {
            if (!Entries.TryGetValue(id, out UloopPausePointEntry entry))
            {
                return;
            }

            entry.RetargetedToHotReloadPatch = value;
        }

        /// <summary>
        /// Stores the currently resolved source line for status / hot-reload retarget visibility.
        /// Pass 0 / null to clear (unresolved). No-op when the id is unknown.
        /// </summary>
        public static void SetResolvedLine(string id, int resolvedLine, string resolvedLineText)
        {
            if (!Entries.TryGetValue(id, out UloopPausePointEntry entry))
            {
                return;
            }

            entry.ResolvedLine = resolvedLine;
            entry.ResolvedLineText = resolvedLineText;
        }

        /// <summary>
        /// Stores the parameters capture cannot box for the currently resolved method, so status
        /// keeps explaining a missing name after enable and after a hot-reload retarget.
        /// Pass an empty list to clear (the resolution behind it was discarded).
        /// No-op when the id is unknown.
        /// </summary>
        public static void SetNotCapturableVariables(string id, IReadOnlyList<string> notCapturableVariables)
        {
            Debug.Assert(notCapturableVariables != null, "notCapturableVariables must not be null");

            if (!Entries.TryGetValue(id, out UloopPausePointEntry entry))
            {
                return;
            }

            entry.NotCapturableVariables = notCapturableVariables;
        }

        /// <summary>
        /// Extends a marker's capture window to at least minimumRemainingSeconds from now, so a
        /// slow multi-step CLI round trip (enable -&gt; seed state -&gt; await) does not let the marker
        /// expire before await-pause-point even starts observing it. Called once when the wait
        /// begins. A no-op for markers that are already Cleared/Expired or unknown.
        /// </summary>
        public static UloopPausePointSnapshot ExtendExpiryForAwait(string id, int minimumRemainingSeconds)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(id), "id must not be null or empty");
            Debug.Assert(minimumRemainingSeconds > 0, "minimumRemainingSeconds must be greater than zero");

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

            entry.ExtendExpiryToAtLeast(now.AddSeconds(minimumRemainingSeconds));
            return entry.ToSnapshot(now, _pauseController);
        }

        public static UloopPausePointSnapshot Hit(string id)
        {
            return HitCore(
                id, null, Array.Empty<UloopCapturedVariable>(), false, Array.Empty<UloopPausePointCallerFrame>());
        }

        public static UloopPausePointSnapshot HitWithCapturedVariables(
            string id, IReadOnlyList<UloopCapturedVariable> capturedVariables, bool capturedVariablesTruncated)
        {
            Debug.Assert(capturedVariables != null, "capturedVariables must not be null");

            return HitCore(
                id, null, capturedVariables, capturedVariablesTruncated, Array.Empty<UloopPausePointCallerFrame>());
        }

        public static UloopPausePointSnapshot HitWithCapturedFrame(
            string id,
            UloopPausePointCapturedVariableFrame capturedFrame,
            IReadOnlyList<UloopCapturedVariable> capturedVariables,
            bool capturedVariablesTruncated,
            IReadOnlyList<UloopPausePointCallerFrame> callerFrames)
        {
            Debug.Assert(capturedFrame != null, "capturedFrame must not be null");
            Debug.Assert(capturedVariables != null, "capturedVariables must not be null");
            Debug.Assert(callerFrames != null, "callerFrames must not be null");

            return HitCore(id, capturedFrame, capturedVariables, capturedVariablesTruncated, callerFrames);
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

        // Called from injected IL at method entry on whatever thread invokes the method. Entries
        // is a ConcurrentDictionary and the increment is Interlocked, so this is safe off the
        // main thread, like IsArmed. A concurrent clear may permit a few extra entries to be
        // recorded, which is acceptable because strict synchronization is not required here.
        public static void RecordMethodEntry(string id)
        {
            if (Entries.TryGetValue(id, out UloopPausePointEntry entry) && entry.IsEnabled)
            {
                entry.IncrementMethodEntryCount();
            }
        }

        // Returns the immutable condition attached at enable time. Capture calls this only after
        // IsArmed, so an unknown or concurrently-cleared marker simply skips condition evaluation.
        public static UloopPausePointHitWhenCondition GetHitWhenCondition(string id)
        {
            return Entries.TryGetValue(id, out UloopPausePointEntry entry)
                ? entry.HitWhenCondition
                : null;
        }

        // Capture runs in Harmony-injected methods that may be off the main thread, so the entry
        // owns the Interlocked increment and this registry method keeps the same armed gate as
        // RecordMethodEntry.
        public static void RecordHitWhenSkip(string id)
        {
            if (Entries.TryGetValue(id, out UloopPausePointEntry entry) && entry.IsEnabled)
            {
                entry.IncrementHitWhenSkippedCount();
            }
        }

        // Stores the first recoverable evaluation failure while allowing the current capture to
        // proceed, so a typo cannot silently discard every frame from a live investigation.
        public static void RecordHitWhenError(string id, string errorMessage)
        {
            if (Entries.TryGetValue(id, out UloopPausePointEntry entry) && entry.IsEnabled)
            {
                entry.RecordHitWhenError(errorMessage);
            }
        }

        internal static void SetMethodEntryInstrumented(string id)
        {
            MethodEntryInstrumentedIds.Add(id);
        }

        internal static void ClearMethodEntryInstrumented(string id)
        {
            MethodEntryInstrumentedIds.Remove(id);
        }

        // Called from the Harmony Capture entry point right after IsArmed confirms the id is
        // armed, so the per-marker override set at Enable time can size the collection preview
        // for this hit. Falls back to the default when the id is unexpectedly missing (e.g. a
        // race with Clear) rather than asserting, since Capture must never throw off a patched method.
        public static int GetMaxPreviewElements(string id)
        {
            return Entries.TryGetValue(id, out UloopPausePointEntry entry)
                ? entry.MaxPreviewElements
                : DefaultMaxPreviewElements;
        }

        // Called from the Harmony Capture entry point so the per-marker caller-frame cap set at
        // Enable time can size this hit. Falls back to the default when the id is unexpectedly
        // missing (e.g. a race with Clear) rather than asserting, since Capture must never throw.
        public static int GetMaxCallerFrames(string id)
        {
            return Entries.TryGetValue(id, out UloopPausePointEntry entry)
                ? entry.MaxCallerFrames
                : DefaultMaxCallerFrames;
        }

        /// <summary>
        /// Counts entries still armed (IsEnabled), i.e. markers whose Harmony patch is currently
        /// installed and would be lost on the next domain reload.
        /// </summary>
        public static int GetActiveCount()
        {
            return UloopPausePointStatusSnapshotCollector.CountActiveEntries(Entries.Values);
        }

        private static UloopPausePointSnapshot HitCore(
            string id,
            UloopPausePointCapturedVariableFrame capturedFrame,
            IReadOnlyList<UloopCapturedVariable> capturedVariables,
            bool capturedVariablesTruncated,
            IReadOnlyList<UloopPausePointCallerFrame> callerFrames)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                Debug.Assert(false, "id must not be null or empty");
                return UloopPausePointSnapshot.NotEnabled(id ?? string.Empty, _pauseController);
            }

            Debug.Assert(callerFrames != null, "callerFrames must not be null");

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
                _pauseWindowOwnerId = id;
            }

            int hitSequence = ++_nextHitSequence;
            int frameCount = Time.frameCount;
            IReadOnlyList<string> truncatedVariableNames = capturedFrame != null
                ? capturedFrame.TruncatedVariableNames
                : Array.Empty<string>();
            int truncatedVariableCount = capturedFrame != null
                ? capturedFrame.TruncatedVariableCount
                : 0;
            entry.RecordHitWithCapturedVariables(
                now, _pauseController.IsPlaying, _pauseController.IsPaused, hitSequence,
                frameCount, capturedVariables, capturedVariablesTruncated,
                truncatedVariableNames, truncatedVariableCount, callerFrames);
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
            MethodEntryInstrumentedIds.Clear();
            _nextGeneration = 0;
            _nextHitSequence = 0;
            _latestHitSnapshot = null;
            _hitSnapshots.Clear();
            _pauseWindowStartUtc = null;
            _pauseWindowOwnerId = null;
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
