#if UNITY_EDITOR
using System;
using System.Threading;

namespace io.github.hatayama.UnityCliLoop.Runtime
{
    /// <summary>
    /// The registry's Editor pause window: which marker's hit is holding the Editor paused, and
    /// every path that opens, credits back, or closes that pause. Split from the main registry
    /// file so each stays inside the repository file-length limit.
    /// </summary>
    internal static partial class UloopPausePointRegistry
    {
        // Why Interlocked: disconnect monitor / DisconnectAllClients run on the thread pool.
        private static int _pendingClientDisconnectResume;
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

        // Clear(id) must resume only when this id is the pause-window owner. Clearing a different
        // marker (including AwaitTimeoutAutoClear of a never-hit wait) must leave the owner's
        // inspection pause in place.
        private static bool ResumeEditorPauseIfOwnedByMarker(string id)
        {
            if (_pauseWindowOwnerId != id)
            {
                return false;
            }

            return ResumeEditorPauseIfOwnedByPausePoint();
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
    }
}
#endif
