#if UNITY_EDITOR
using System;
using System.Collections.Generic;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.Runtime
{
    /// <summary>
    /// Immutable view of one pause point state returned to tools and the CLI bridge.
    /// </summary>
    internal sealed class UloopPausePointSnapshot
    {
        public UloopPausePointSnapshot(
            string id,
            string status,
            bool isEnabled,
            bool isHit,
            int hitCount,
            int methodEntryCount,
            int timeoutSeconds,
            string mode,
            int maxHistory,
            int maxPreviewElements,
            int maxCallerFrames,
            IReadOnlyList<UloopPausePointCapturedHistoryFrame> capturedVariableHistory,
            int historyDroppedCount,
            bool expired,
            string enabledAtUtc,
            long elapsedMilliseconds,
            long remainingMilliseconds,
            int generation,
            UloopPausePointEditorStateSnapshot editorState,
            string firstHitAtUtc,
            string lastHitAtUtc,
            int firstHitSequence,
            int lastHitSequence,
            string message,
            string recommendedNextAction,
            IReadOnlyList<UloopCapturedVariable> capturedVariables,
            IReadOnlyList<UloopPausePointCallerFrame> callerFrames,
            bool capturedVariablesTruncated,
            IReadOnlyList<string> truncatedVariableNames,
            int truncatedVariableCount,
            string clearedReason,
            string statusBeforeClear,
            bool lateHitDiscardedAfterClear,
            bool suppressedByHotReload,
            string suppressedByHotReloadReason,
            bool retargetedToHotReloadPatch,
            int resolvedLine,
            string resolvedLineText,
            string hitWhen,
            int hitWhenSkippedCount,
            string hitWhenErrorNote)
        {
            Debug.Assert(editorState != null, "editorState must not be null");

            Id = id ?? string.Empty;
            Status = status ?? UloopPausePointStatus.NotEnabled;
            IsEnabled = isEnabled;
            IsHit = isHit;
            HitCount = hitCount;
            MethodEntryCount = methodEntryCount;
            TimeoutSeconds = timeoutSeconds;
            Mode = mode ?? UloopPausePointCaptureMode.SingleShot;
            MaxHistory = maxHistory;
            MaxPreviewElements = maxPreviewElements;
            MaxCallerFrames = maxCallerFrames;
            CapturedVariableHistory = capturedVariableHistory ?? Array.Empty<UloopPausePointCapturedHistoryFrame>();
            HistoryDroppedCount = historyDroppedCount;
            Expired = expired;
            EnabledAtUtc = enabledAtUtc ?? string.Empty;
            ElapsedSinceEnabledMilliseconds = elapsedMilliseconds;
            RemainingMilliseconds = remainingMilliseconds;
            Generation = generation;
            EditorState = editorState;
            FirstHitAtUtc = firstHitAtUtc ?? string.Empty;
            LastHitAtUtc = lastHitAtUtc ?? string.Empty;
            FirstHitSequence = firstHitSequence;
            LastHitSequence = lastHitSequence;
            Message = message ?? string.Empty;
            RecommendedNextAction = recommendedNextAction ?? string.Empty;
            CapturedVariables = capturedVariables ?? Array.Empty<UloopCapturedVariable>();
            CallerFrames = callerFrames ?? Array.Empty<UloopPausePointCallerFrame>();
            CapturedVariablesTruncated = capturedVariablesTruncated;
            TruncatedVariableNames = truncatedVariableNames ?? Array.Empty<string>();
            TruncatedVariableCount = truncatedVariableCount;
            ClearedReason = clearedReason ?? string.Empty;
            StatusBeforeClear = statusBeforeClear ?? string.Empty;
            LateHitDiscardedAfterClear = lateHitDiscardedAfterClear;
            SuppressedByHotReload = suppressedByHotReload;
            SuppressedByHotReloadReason = suppressedByHotReloadReason;
            RetargetedToHotReloadPatch = retargetedToHotReloadPatch;
            ResolvedLine = resolvedLine;
            ResolvedLineText = resolvedLineText;
            HitWhen = hitWhen ?? string.Empty;
            HitWhenSkippedCount = hitWhenSkippedCount;
            HitWhenErrorNote = hitWhenErrorNote ?? string.Empty;
        }

        public string Id { get; }
        public string Status { get; }
        public bool IsEnabled { get; }
        public bool IsHit { get; }
        public int HitCount { get; }
        public int MethodEntryCount { get; }
        public int TimeoutSeconds { get; }
        public string Mode { get; }
        public int MaxHistory { get; }
        public int MaxPreviewElements { get; }
        public int MaxCallerFrames { get; }
        public IReadOnlyList<UloopPausePointCapturedHistoryFrame> CapturedVariableHistory { get; }
        public int HistoryDroppedCount { get; }
        public bool Expired { get; }
        public string EnabledAtUtc { get; }
        public long ElapsedSinceEnabledMilliseconds { get; }
        public long RemainingMilliseconds { get; }
        public int Generation { get; }
        public UloopPausePointEditorStateSnapshot EditorState { get; }
        public string FirstHitAtUtc { get; }
        public string LastHitAtUtc { get; }
        public int FirstHitSequence { get; }
        public int LastHitSequence { get; }
        public string Message { get; }
        public string RecommendedNextAction { get; }
        public IReadOnlyList<UloopCapturedVariable> CapturedVariables { get; }
        public IReadOnlyList<UloopPausePointCallerFrame> CallerFrames { get; }
        public bool CapturedVariablesTruncated { get; }
        public IReadOnlyList<string> TruncatedVariableNames { get; }
        public int TruncatedVariableCount { get; }
        public string ClearedReason { get; }
        public string StatusBeforeClear { get; }
        public bool LateHitDiscardedAfterClear { get; }
        public bool SuppressedByHotReload { get; }
        public string SuppressedByHotReloadReason { get; }
        public bool RetargetedToHotReloadPatch { get; }
        public int ResolvedLine { get; }
        public string ResolvedLineText { get; }
        public string HitWhen { get; }
        public int HitWhenSkippedCount { get; }
        public string HitWhenErrorNote { get; }

        public static UloopPausePointSnapshot NotEnabled(string id, IUloopPausePointPauseController pauseController)
        {
            Debug.Assert(pauseController != null, "pauseController must not be null");

            return new UloopPausePointSnapshot(
                id,
                UloopPausePointStatus.NotEnabled,
                false,
                false,
                0,
                0,
                0,
                UloopPausePointCaptureMode.SingleShot,
                0,
                0,
                0,
                Array.Empty<UloopPausePointCapturedHistoryFrame>(),
                0,
                false,
                string.Empty,
                0,
                0,
                0,
                UloopPausePointEditorStateSnapshot.FromController(
                    pauseController,
                    UloopPausePointEditorStateCapturedAt.Current),
                string.Empty,
                string.Empty,
                0,
                0,
                "Pause point is not enabled.",
                string.Empty,
                Array.Empty<UloopCapturedVariable>(),
                Array.Empty<UloopPausePointCallerFrame>(),
                false,
                Array.Empty<string>(),
                0,
                string.Empty,
                string.Empty,
                false,
                false,
                null,
                false,
                0,
                null,
                string.Empty,
                0,
                string.Empty);
        }
    }
}
#endif
