#if UNITY_EDITOR
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
            int timeoutSeconds,
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
            string recommendedNextAction)
        {
            Debug.Assert(editorState != null, "editorState must not be null");

            Id = id ?? string.Empty;
            Status = status ?? UloopPausePointStatus.NotEnabled;
            IsEnabled = isEnabled;
            IsHit = isHit;
            HitCount = hitCount;
            TimeoutSeconds = timeoutSeconds;
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
        }

        public string Id { get; }
        public string Status { get; }
        public bool IsEnabled { get; }
        public bool IsHit { get; }
        public int HitCount { get; }
        public int TimeoutSeconds { get; }
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
                string.Empty);
        }
    }
}
#endif
