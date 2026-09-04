#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.Runtime
{
    /// <summary>
    /// Owns mutable state for one enabled pause point id.
    /// </summary>
    internal sealed class UloopPausePointEntry
    {
        // Incremented from patched method bodies on arbitrary threads, so this counter must use
        // Interlocked instead of main-thread-only entry state.
        private int _methodEntryCount;
        // Capture can evaluate conditions off the main thread, so its counter and first error use
        // interlocked operations instead of coupling the patched method to Unity main-thread state.
        private int _hitWhenSkippedCount;
        // Frozen at ExpireIfNeeded so Message and RecommendedNextAction share one measurement.
        // In-flight increments can still raise the live counters after that compose.
        private int _expiredMethodEntryCount;
        private int _expiredHitWhenSkippedCount;
        private string _hitWhenErrorNote = string.Empty;

        public UloopPausePointEntry(
            string id,
            int timeoutSeconds,
            string mode,
            int maxHistory,
            int maxPreviewElements,
            int maxCallerFrames,
            DateTime enabledAtUtc,
            int generation,
            bool hasMethodEntryInstrumentation,
            string hitWhen,
            UloopPausePointHitWhenCondition hitWhenCondition,
            bool patchDispatchMayBypass)
        {
            Id = id;
            TimeoutSeconds = timeoutSeconds;
            Mode = mode;
            MaxHistory = maxHistory;
            MaxPreviewElements = maxPreviewElements;
            MaxCallerFrames = maxCallerFrames;
            EnabledAtUtc = enabledAtUtc;
            ExpiresAtUtc = enabledAtUtc.AddSeconds(timeoutSeconds);
            Generation = generation;
            HasMethodEntryInstrumentation = hasMethodEntryInstrumentation;
            HitWhen = hitWhen ?? string.Empty;
            HitWhenCondition = hitWhenCondition;
            PatchDispatchMayBypass = patchDispatchMayBypass;
            Status = UloopPausePointStatus.Enabled;
            IsEnabled = true;
            Message = "Pause point enabled.";
            CapturedVariables = Array.Empty<UloopCapturedVariable>();
            CallerFrames = Array.Empty<UloopPausePointCallerFrame>();
            TruncatedVariableNames = Array.Empty<string>();
            NotCapturableVariables = Array.Empty<string>();
            _capturedVariableHistory = new Queue<UloopPausePointCapturedHistoryFrame>(maxHistory);
        }

        public string Id { get; }
        public int TimeoutSeconds { get; }
        public string Mode { get; }
        public int MaxHistory { get; }
        public int MaxPreviewElements { get; }
        public int MaxCallerFrames { get; }
        public DateTime EnabledAtUtc { get; }
        public DateTime ExpiresAtUtc { get; private set; }
        public int Generation { get; }
        public bool HasMethodEntryInstrumentation { get; }
        // Why keep this on the entry: Unity's cached physics-message dispatch can run the
        // original body without the armed patch, so MethodEntryCount 0 is not proof the
        // method never ran. Expire wording must see the same flag Enable recorded.
        public bool PatchDispatchMayBypass { get; }
        public string Status { get; private set; }
        public bool IsEnabled { get; private set; }
        public int HitCount { get; private set; }
        public int MethodEntryCount => System.Threading.Volatile.Read(ref _methodEntryCount);
        public string HitWhen { get; }
        public UloopPausePointHitWhenCondition HitWhenCondition { get; }
        public int HitWhenSkippedCount => System.Threading.Volatile.Read(ref _hitWhenSkippedCount);
        public string HitWhenErrorNote => System.Threading.Volatile.Read(ref _hitWhenErrorNote);
        public DateTime FirstHitAtUtc { get; private set; }
        public DateTime HitAtUtc { get; private set; }
        public int FirstHitSequence { get; private set; }
        public int LastHitSequence { get; private set; }
        public bool IsPlayingAtHit { get; private set; }
        public bool IsPausedAtHit { get; private set; }
        public string Message { get; private set; }
        public IReadOnlyList<UloopCapturedVariable> CapturedVariables { get; private set; }
        public IReadOnlyList<UloopPausePointCallerFrame> CallerFrames { get; private set; }
        public bool CapturedVariablesTruncated { get; private set; }
        public IReadOnlyList<string> TruncatedVariableNames { get; private set; }
        public int TruncatedVariableCount { get; private set; }
        public int HistoryDroppedCount { get; private set; }
        public string ClearedReason { get; private set; } = string.Empty;
        public string StatusBeforeClear { get; private set; } = string.Empty;
        public bool LateHitDiscardedAfterClear { get; private set; }
        public bool SuppressedByHotReload { get; set; }
        public string SuppressedByHotReloadReason { get; set; }
        public bool RetargetedToHotReloadPatch { get; set; }
        // 0 / null-or-empty means unresolved (not yet written by enable or retarget).
        public int ResolvedLine { get; set; }
        public string ResolvedLineText { get; set; }
        // Parameters the resolved method has that capture cannot box, each with the reason.
        // Written by enable and by hot-reload retarget alongside ResolvedLine, and emptied
        // whenever the resolution behind it is discarded.
        public IReadOnlyList<string> NotCapturableVariables { get; set; }

        public void IncrementMethodEntryCount()
        {
            System.Threading.Interlocked.Increment(ref _methodEntryCount);
        }

        public void IncrementHitWhenSkippedCount()
        {
            System.Threading.Interlocked.Increment(ref _hitWhenSkippedCount);
        }

        public void RecordHitWhenError(string errorMessage)
        {
            if (string.IsNullOrEmpty(errorMessage))
            {
                return;
            }

            // Why first only: later frames can repeat or change an error, but the first one is the
            // closest evidence of why the condition began failing while leaving the hot path lock-free.
            System.Threading.Interlocked.CompareExchange(ref _hitWhenErrorNote, errorMessage, string.Empty);
        }

        public bool ExpireIfNeeded(DateTime nowUtc)
        {
            if (Status == UloopPausePointStatus.Cleared || Status == UloopPausePointStatus.Expired)
            {
                return false;
            }

            if (nowUtc < ExpiresAtUtc)
            {
                return false;
            }

            // Why also Hit: SingleShot disarms on hit (IsEnabled=false) but the capture window
            // still ends at ExpiresAtUtc; without this, an abandoned pause after hit never expires.
            if (!IsEnabled && Status != UloopPausePointStatus.Hit)
            {
                return false;
            }

            IsEnabled = false;
            Status = UloopPausePointStatus.Expired;
            // Increments are gated on IsEnabled, which is false here. An in-flight increment that
            // already passed that gate can still reach the snapshot after this message is composed.
            int methodEntryCount = MethodEntryCount;
            int hitWhenSkippedCount = HitWhenSkippedCount;
            _expiredMethodEntryCount = methodEntryCount;
            _expiredHitWhenSkippedCount = hitWhenSkippedCount;
            Message = CreateExpiredMessage(methodEntryCount, hitWhenSkippedCount);
            return true;
        }

        // Pushes ExpiresAtUtc forward by the length of an Editor-paused window, so the countdown
        // does not keep running while a hit has stopped the Editor for inspection. Clamps the
        // window start to EnabledAtUtc so a pause that began before this entry existed does not
        // over-credit it.
        public void ExtendExpiryForPause(DateTime pauseWindowStartUtc, DateTime pauseWindowEndUtc)
        {
            if (Status == UloopPausePointStatus.Cleared || Status == UloopPausePointStatus.Expired)
            {
                return;
            }

            DateTime effectiveStart = pauseWindowStartUtc > EnabledAtUtc ? pauseWindowStartUtc : EnabledAtUtc;
            if (pauseWindowEndUtc <= effectiveStart)
            {
                return;
            }

            ExpiresAtUtc += pauseWindowEndUtc - effectiveStart;
        }

        // Called once when await-pause-point starts waiting, so a marker enabled well before a
        // slow multi-step CLI round trip (enable -> seed state -> await) does not expire before
        // the await itself even gets a chance to observe a hit. Only ever moves ExpiresAtUtc
        // forward: it cannot un-expire a marker or shorten a window an earlier call already set.
        public void ExtendExpiryToAtLeast(DateTime minimumExpiresAtUtc)
        {
            if (Status == UloopPausePointStatus.Cleared || Status == UloopPausePointStatus.Expired)
            {
                return;
            }

            if (minimumExpiresAtUtc > ExpiresAtUtc)
            {
                ExpiresAtUtc = minimumExpiresAtUtc;
            }
        }

        public void MarkCleared(string clearedReason, string message = "Pause point cleared.")
        {
            // Why: a second clear must not erase the first reason (e.g. RunTestsAutoClear).
            if (Status == UloopPausePointStatus.Cleared)
            {
                IsEnabled = false;
                Message = message;
                return;
            }

            // Why: keep the pre-clear status so agents can still see Expired/Hit after Cleared overwrites Status.
            StatusBeforeClear = Status;
            if (Status == UloopPausePointStatus.Expired)
            {
                ClearedReason = UloopPausePointClearedReason.AfterExpired;
            }
            else if (Status == UloopPausePointStatus.Hit &&
                !IsEnabled &&
                clearedReason == UloopPausePointClearedReason.ExplicitClear)
            {
                ClearedReason = UloopPausePointClearedReason.AlreadyHit;
            }
            else
            {
                ClearedReason = string.IsNullOrEmpty(clearedReason)
                    ? UloopPausePointClearedReason.ExplicitClear
                    : clearedReason;
            }

            IsEnabled = false;
            Status = UloopPausePointStatus.Cleared;
            Message = message;
        }

        public void MarkLateHitDiscardedAfterClear()
        {
            LateHitDiscardedAfterClear = true;
        }

        public void RecordHitWithCapturedVariables(
            DateTime nowUtc,
            bool isPlaying,
            bool isPaused,
            int hitSequence,
            int frameCount,
            IReadOnlyList<UloopCapturedVariable> capturedVariables,
            bool capturedVariablesTruncated,
            IReadOnlyList<string> truncatedVariableNames,
            int truncatedVariableCount,
            IReadOnlyList<UloopPausePointCallerFrame> callerFrames)
        {
            Debug.Assert(hitSequence > 0, "hitSequence must be greater than zero");
            Debug.Assert(capturedVariables != null, "capturedVariables must not be null");
            Debug.Assert(truncatedVariableNames != null, "truncatedVariableNames must not be null");
            Debug.Assert(truncatedVariableCount >= 0, "truncatedVariableCount must not be negative");
            Debug.Assert(callerFrames != null, "callerFrames must not be null");

            if (HitCount == 0)
            {
                FirstHitAtUtc = nowUtc;
                FirstHitSequence = hitSequence;
            }

            HitCount++;
            HitAtUtc = nowUtc;
            LastHitSequence = hitSequence;
            IsPlayingAtHit = isPlaying;
            IsPausedAtHit = isPaused;
            IsEnabled = Mode != UloopPausePointCaptureMode.SingleShot;
            Status = UloopPausePointStatus.Hit;
            Message = Mode == UloopPausePointCaptureMode.Trace
                ? "Pause point hit; recorded to history without pausing (trace mode)."
                : "Pause point hit; Unity pause was requested.";
            CapturedVariables = capturedVariables;
            CallerFrames = callerFrames;
            CapturedVariablesTruncated = capturedVariablesTruncated;
            TruncatedVariableNames = truncatedVariableNames;
            TruncatedVariableCount = truncatedVariableCount;

            if (_capturedVariableHistory.Count == MaxHistory)
            {
                _capturedVariableHistory.Dequeue();
                HistoryDroppedCount++;
            }

            _capturedVariableHistory.Enqueue(
                new UloopPausePointCapturedHistoryFrame(
                    hitSequence,
                    frameCount,
                    FormatUtc(nowUtc),
                    capturedVariables,
                    capturedVariablesTruncated,
                    callerFrames));
        }

        public UloopPausePointSnapshot ToSnapshot(DateTime nowUtc, IUloopPausePointPauseController pauseController)
        {
            Debug.Assert(pauseController != null, "pauseController must not be null");

            bool isHit = Status == UloopPausePointStatus.Hit;
            bool expired = Status == UloopPausePointStatus.Expired;
            UloopPausePointEditorStateSnapshot editorState = isHit
                ? new UloopPausePointEditorStateSnapshot(
                    IsPlayingAtHit,
                    IsPausedAtHit,
                    UloopPausePointEditorStateCapturedAt.PausePointHit)
                : UloopPausePointEditorStateSnapshot.FromController(
                    pauseController,
                    UloopPausePointEditorStateCapturedAt.Current);
            long elapsedMilliseconds = Math.Max(0, (long)(nowUtc - EnabledAtUtc).TotalMilliseconds);
            long remainingMilliseconds = CalculateRemainingMilliseconds(nowUtc);
            int hitWhenSkippedCount = HitWhenSkippedCount;
            string recommendedNextAction = expired
                ? CreateExpiredRecommendedNextAction(_expiredMethodEntryCount, _expiredHitWhenSkippedCount)
                : string.Empty;
            string firstHitAtUtc = HitCount > 0 ? FormatUtc(FirstHitAtUtc) : string.Empty;
            string lastHitAtUtc = HitCount > 0 ? FormatUtc(HitAtUtc) : string.Empty;

            return new UloopPausePointSnapshot(
                Id,
                Status,
                IsEnabled,
                isHit,
                HitCount,
                MethodEntryCount,
                TimeoutSeconds,
                Mode,
                MaxHistory,
                MaxPreviewElements,
                MaxCallerFrames,
                new List<UloopPausePointCapturedHistoryFrame>(_capturedVariableHistory),
                HistoryDroppedCount,
                expired,
                FormatUtc(EnabledAtUtc),
                elapsedMilliseconds,
                remainingMilliseconds,
                Generation,
                editorState,
                firstHitAtUtc,
                lastHitAtUtc,
                FirstHitSequence,
                LastHitSequence,
                Message,
                recommendedNextAction,
                CapturedVariables,
                CallerFrames,
                CapturedVariablesTruncated,
                TruncatedVariableNames,
                TruncatedVariableCount,
                NotCapturableVariables,
                ClearedReason,
                StatusBeforeClear,
                LateHitDiscardedAfterClear,
                SuppressedByHotReload,
                SuppressedByHotReloadReason,
                RetargetedToHotReloadPatch,
                ResolvedLine,
                ResolvedLineText,
                HitWhen,
                hitWhenSkippedCount,
                HitWhenErrorNote);
        }

        private long CalculateRemainingMilliseconds(DateTime nowUtc)
        {
            if (!IsEnabled)
            {
                return 0;
            }

            long remainingMilliseconds = (long)(ExpiresAtUtc - nowUtc).TotalMilliseconds;
            return Math.Max(0, remainingMilliseconds);
        }

        // Why not nest these in ExpireIfNeeded: adding the physics-dispatch case on top of
        // the existing hit-when / never-invoked / branch-not-taken split would deepen the
        // ternary chain past the complexity budget.
        private string CreateExpiredMessage(int methodEntryCount, int hitWhenSkippedCount)
        {
            if (HitCount > 0)
            {
                return $"Pause point capture window expired after {HitCount} hit(s); capture history is preserved.";
            }

            if (!HasMethodEntryInstrumentation)
            {
                return "Pause point expired before it was hit.";
            }

            if (hitWhenSkippedCount > 0)
            {
                return $"Pause point expired before any hit matched --hit-when. The method entered {methodEntryCount} time(s); {hitWhenSkippedCount} hit(s) were skipped by the condition.";
            }

            if (methodEntryCount > 0)
            {
                return $"Pause point expired before it was hit. The armed method ran {methodEntryCount} time(s) but the armed line was never reached (branch not taken).";
            }

            if (PatchDispatchMayBypass)
            {
                return "Pause point expired before it was hit. No entry through the armed patch was recorded. This marker sits in (or is called from) a Unity physics message method, and Unity's cached message dispatch may have bypassed the patch even though the method body ran; MethodEntryCount 0 does not prove the method was never invoked. Destroy and recreate the target GameObject after enabling, or embed UloopPausePoint.Pause(\"id\") in the method body and arm it with enable-pause-point --id.";
            }

            return "Pause point expired before it was hit. The armed method was never invoked.";
        }

        // Why not a single timeout hint: a recorded hit already proves the line ran, skipped
        // --hit-when hits prove the line ran without matching, and method-entry evidence tells
        // whether a longer window can help. Repeating timeout advice after those cases sends
        // the agent down a retry that cannot succeed. --hit-when skips are recorded even on
        // id-only markers, so that branch must not require method-entry instrumentation.
        // Physics dispatch is last among the zero-hit cases because MethodEntryCount 0 is
        // inconclusive when Unity may have skipped the patch.
        private string CreateExpiredRecommendedNextAction(int methodEntryCount, int hitWhenSkippedCount)
        {
            const string defaultAction =
                "Re-enable the marker with a longer --timeout-seconds and trigger the code path again; clearing the expired marker first is not required.";
            if (HitCount > 0)
            {
                return "The marker was hit before its --timeout-seconds window closed, so this is not a missed code path. Read the recorded hit with pause-point-status --id <marker-id> (HitCount, CapturedVariables, CapturedVariableHistory survive expiry); re-enable the marker if you need to capture another hit.";
            }

            if (hitWhenSkippedCount > 0)
            {
                return $"The armed line executed {hitWhenSkippedCount} time(s) but no hit matched --hit-when. Re-enable the marker, then adjust the --hit-when condition or the trigger input so a hit matches; clearing the expired marker first is not required.";
            }

            if (HasMethodEntryInstrumentation && methodEntryCount > 0)
            {
                return "The armed method ran but the armed line was never reached, so a longer --timeout-seconds alone will not help. Check the condition that guards the armed line (the trigger may have fired while it was false), then re-enable the marker and trigger the code path again once the precondition holds; --mode continuous keeps the marker armed across repeated attempts. Clearing the expired marker first is not required.";
            }

            if (PatchDispatchMayBypass)
            {
                return "Confirm whether the method body actually ran (a log inside it, or a pause point on a plain method it calls). If it did, the patch was bypassed by cached physics dispatch: destroy and recreate the GameObject after enabling, or switch to UloopPausePoint.Pause(\"id\") with --id. Only raise --timeout-seconds if the body never ran.";
            }

            return defaultAction;
        }

        private static string FormatUtc(DateTime value)
        {
            DateTime utcValue = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
            return utcValue.ToString("O");
        }

        private readonly Queue<UloopPausePointCapturedHistoryFrame> _capturedVariableHistory;
    }
}
#endif
