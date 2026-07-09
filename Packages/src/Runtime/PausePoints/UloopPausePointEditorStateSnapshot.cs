#if UNITY_EDITOR
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.Runtime
{
    /// <summary>
    /// Immutable Unity Editor state attached to pause point evidence.
    /// </summary>
    internal sealed class UloopPausePointEditorStateSnapshot
    {
        public UloopPausePointEditorStateSnapshot(bool isPlaying, bool isPaused, string capturedAt)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(capturedAt), "capturedAt must not be null or empty");

            IsPlaying = isPlaying;
            IsPaused = isPaused;
            CapturedAt = capturedAt ?? string.Empty;
        }

        public bool IsPlaying { get; }
        public bool IsPaused { get; }
        public string CapturedAt { get; }

        public static UloopPausePointEditorStateSnapshot FromController(
            IUloopPausePointPauseController pauseController,
            string capturedAt)
        {
            Debug.Assert(pauseController != null, "pauseController must not be null");

            return new UloopPausePointEditorStateSnapshot(
                pauseController.IsPlaying,
                pauseController.IsPaused,
                capturedAt);
        }
    }
}
#endif
