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
            DateTime enabledAtUtc,
            int generation)
        {
            Id = id;
            TimeoutSeconds = timeoutSeconds;
            Mode = mode;
            MaxHistory = maxHistory;
            EnabledAtUtc = enabledAtUtc;
            ExpiresAtUtc = enabledAtUtc.AddSeconds(timeoutSeconds);
            Generation = generation;
            Status = UloopPausePointStatus.Enabled;
            IsEnabled = true;
            Message = "Pause point enabled.";
            CapturedVariables = Array.Empty<UloopCapturedVariable>();
            _capturedVariableHistory = new Queue<UloopPausePointCapturedHistoryFrame>(maxHistory);
        }

        public string Id { get; }
        public int TimeoutSeconds { get; }
        public string Mode { get; }
        public int MaxHistory { get; }
        public DateTime EnabledAtUtc { get; }
        public DateTime ExpiresAtUtc { get; }
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
        public int HistoryDroppedCount { get; private set; }
        public string ClearedReason { get; private set; } = string.Empty;
        public string StatusBeforeClear { get; private set; } = string.Empty;
        public bool LateHitDiscardedAfterClear { get; private set; }

        public void ExpireIfNeeded(DateTime nowUtc)
        {
            if (!IsEnabled)
            {
                return;
            }

            if (nowUtc < ExpiresAtUtc)
            {
                return;
            }

            IsEnabled = false;
            Status = UloopPausePointStatus.Expired;
            Message = HitCount == 0
                ? "Pause point expired before it was hit."
                : $"Pause point capture window expired after {HitCount} hit(s); capture history is preserved.";
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
            bool capturedVariablesTruncated)
        {
            Debug.Assert(hitSequence > 0, "hitSequence must be greater than zero");
            Debug.Assert(capturedVariables != null, "capturedVariables must not be null");

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
                ClearedReason,
                StatusBeforeClear,
                LateHitDiscardedAfterClear);
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
