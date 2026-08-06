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
        public UloopPausePointEntry(
            string id,
            int timeoutSeconds,
            string mode,
            int maxHistory,
            int maxPreviewElements,
            DateTime enabledAtUtc,
            int generation)
        {
            Id = id;
            TimeoutSeconds = timeoutSeconds;
            Mode = mode;
            MaxHistory = maxHistory;
            MaxPreviewElements = maxPreviewElements;
            EnabledAtUtc = enabledAtUtc;
            ExpiresAtUtc = enabledAtUtc.AddSeconds(timeoutSeconds);
            Generation = generation;
            Status = UloopPausePointStatus.Enabled;
            IsEnabled = true;
            Message = "Pause point enabled.";
            CapturedVariables = Array.Empty<UloopCapturedVariable>();
            TruncatedVariableNames = Array.Empty<string>();
            _capturedVariableHistory = new Queue<UloopPausePointCapturedHistoryFrame>(maxHistory);
        }

        public string Id { get; }
        public int TimeoutSeconds { get; }
        public string Mode { get; }
        public int MaxHistory { get; }
        public int MaxPreviewElements { get; }
        public DateTime EnabledAtUtc { get; }
        public DateTime ExpiresAtUtc { get; private set; }
        public int Generation { get; }
        public string Status { get; private set; }
        public bool IsEnabled { get; private set; }
        public int HitCount { get; private set; }
        public DateTime FirstHitAtUtc { get; private set; }
        public DateTime HitAtUtc { get; private set; }
        public int FirstHitSequence { get; private set; }
        public int LastHitSequence { get; private set; }
        public bool IsPlayingAtHit { get; private set; }
        public bool IsPausedAtHit { get; private set; }
        public string Message { get; private set; }
        public IReadOnlyList<UloopCapturedVariable> CapturedVariables { get; private set; }
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
            Message = HitCount == 0
                ? "Pause point expired before it was hit."
                : $"Pause point capture window expired after {HitCount} hit(s); capture history is preserved.";
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
            int truncatedVariableCount)
        {
            Debug.Assert(hitSequence > 0, "hitSequence must be greater than zero");
            Debug.Assert(capturedVariables != null, "capturedVariables must not be null");
            Debug.Assert(truncatedVariableNames != null, "truncatedVariableNames must not be null");
            Debug.Assert(truncatedVariableCount >= 0, "truncatedVariableCount must not be negative");

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
                    capturedVariablesTruncated));
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
            string recommendedNextAction = expired ? CreateExpiredRecommendedNextAction() : string.Empty;
            string firstHitAtUtc = HitCount > 0 ? FormatUtc(FirstHitAtUtc) : string.Empty;
            string lastHitAtUtc = HitCount > 0 ? FormatUtc(HitAtUtc) : string.Empty;

            return new UloopPausePointSnapshot(
                Id,
                Status,
                IsEnabled,
                isHit,
                HitCount,
                TimeoutSeconds,
                Mode,
                MaxHistory,
                MaxPreviewElements,
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
                CapturedVariablesTruncated,
                TruncatedVariableNames,
                TruncatedVariableCount,
                ClearedReason,
                StatusBeforeClear,
                LateHitDiscardedAfterClear,
                SuppressedByHotReload,
                SuppressedByHotReloadReason,
                RetargetedToHotReloadPatch);
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

        private string CreateExpiredRecommendedNextAction()
        {
            return "Clear this marker, then re-enable it with the same Id and TimeoutSeconds values.";
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
