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
        public const int DefaultMaxPreviewElements = 10;
        public const int MaxPreviewElementsLimit = 1000;
        public const int DefaultMaxCallerFrames = 2;
        public const int MaxCallerFramesLimit = 8;

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
        // The id of the marker whose hit is actually holding the Editor paused. Kept separate
        // from _latestHitSnapshot, which every hit overwrites (including Trace-mode hits that
        // never pause): using _latestHitSnapshot here would let an unrelated Trace hit that
        // occurs while already paused (e.g. via execute-dynamic-code or Step) misattribute the
        // pause to itself. Overwritten by a later non-Trace hit, since a second marker hitting
        // while already paused becomes the new (only) reason the Editor stays paused.
        private static string _pauseWindowOwnerId;
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
            int maxCallerFrames = DefaultMaxCallerFrames)
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

            DateTime now = NowUtc();
            int generation = ++_nextGeneration;
            UloopPausePointEntry entry = new(
                id, timeoutSeconds, mode, maxHistory, maxPreviewElements, maxCallerFrames, now, generation);
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
            entry.MarkCleared(clearedReason, message);
            OnClearResolved?.Invoke(id, entry.HitCount, entry.StatusBeforeClear);
            ClearHitSnapshotAndRawCaptureForId(id);
            // Why only when pause-point-owned: a clear must not steal ownership of a manual pause
            // the user set outside the pause-point workflow (control-play-mode --action Pause or
            // the Editor pause button). It resumes only while a pause window is open - i.e. while
            // a pause-point hit is what is holding the Editor paused. Manual pauses leave no open
            // window, so they are left untouched. (Client disconnect and expiry still resume
            // unconditionally: those paths must guarantee release even for a manual pause.)
            bool resumedFromPause = ResumeEditorPauseIfOwnedByPausePoint();
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
            int count = 0;
            foreach (UloopPausePointEntry entry in Entries.Values)
            {
                if (entry.IsEnabled)
                {
                    count++;
                }
            }
            return count;
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

        /// <summary>
        /// Read-only signal for callers outside this registry (e.g. execute-dynamic-code) that
        /// need to know whether the Editor is currently paused because of a pause-point hit, and
        /// which marker's hit is responsible. Reads _pauseWindowOwnerId rather than
        /// _latestHitSnapshot, which every hit overwrites (including Trace-mode hits that never
        /// pause) and would otherwise misattribute an open pause window to an unrelated marker.
        /// Returns empty when no window is open, even if the Editor happens to be paused for an
        /// unrelated reason.
        /// </summary>
        public static string GetActivePausePointId()
        {
            return _pauseWindowStartUtc.HasValue ? _pauseWindowOwnerId ?? string.Empty : string.Empty;
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

        // Resumes the Editor only when a pause-point hit currently owns the pause (a pause window
        // is open) and reports whether it did. Clear/ClearAll use this so they never resume a
        // manual pause that no pause-point hit is responsible for. Disconnect and expiry paths
        // deliberately call ResumeEditorPause directly instead, because they must release the
        // Editor even when the pause was manual.
        private static bool ResumeEditorPauseIfOwnedByPausePoint()
        {
            // A manual unpause (Editor pause button / control-play-mode) may not have been observed
            // yet, because the external-resume sync runs on the Editor update tick. Reconcile first
            // so a window left open by that unobserved unpause is credited and closed here instead
            // of being misreported as a resume this clear performed (it would be a no-op resume of
            // an already-unpaused Editor) and instead of leaving the stale window freezing expiry.
            ClosePauseWindowIfEditorResumedExternally();
            if (!_pauseWindowStartUtc.HasValue)
            {
                return false;
            }

            ResumeEditorPause();
            return true;
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
            _pauseWindowOwnerId = null;
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
