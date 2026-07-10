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
        public UloopPausePointEntry(string id, int timeoutSeconds, DateTime enabledAtUtc, int generation)
        {
            Id = id;
            TimeoutSeconds = timeoutSeconds;
            EnabledAtUtc = enabledAtUtc;
            ExpiresAtUtc = enabledAtUtc.AddSeconds(timeoutSeconds);
            Generation = generation;
            Status = UloopPausePointStatus.Enabled;
            IsEnabled = true;
            Message = "Pause point enabled.";
            CapturedVariables = Array.Empty<UloopCapturedVariable>();
        }

        public string Id { get; }
        public int TimeoutSeconds { get; }
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
            Message = "Pause point expired before it was hit.";
        }

        public void MarkCleared(string message = "Pause point cleared.")
        {
            IsEnabled = false;
            Status = UloopPausePointStatus.Cleared;
            Message = message;
        }

        public void RecordHit(DateTime nowUtc, bool isPlaying, bool isPaused, int hitSequence)
        {
            RecordHitWithCapturedVariables(
                nowUtc, isPlaying, isPaused, hitSequence, Array.Empty<UloopCapturedVariable>(), false);
        }

        public void RecordHitWithCapturedVariables(
            DateTime nowUtc,
            bool isPlaying,
            bool isPaused,
            int hitSequence,
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
            IsEnabled = false;
            Status = UloopPausePointStatus.Hit;
            Message = "Pause point hit; Unity pause was requested.";
            CapturedVariables = capturedVariables;
            CapturedVariablesTruncated = capturedVariablesTruncated;
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
                CapturedVariablesTruncated);
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
    }
}
#endif
