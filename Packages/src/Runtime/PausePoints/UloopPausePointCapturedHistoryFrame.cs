#if UNITY_EDITOR
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.Runtime
{
    /// <summary>
    /// Stores the formatted evidence captured by one pause point hit.
    /// </summary>
    internal sealed class UloopPausePointCapturedHistoryFrame
    {
        public UloopPausePointCapturedHistoryFrame(
            int hitSequence,
            int frameCount,
            string hitAtUtc,
            IReadOnlyList<UloopCapturedVariable> capturedVariables,
            bool truncated)
        {
            HitSequence = hitSequence;
            FrameCount = frameCount;
            HitAtUtc = hitAtUtc;
            CapturedVariables = capturedVariables;
            Truncated = truncated;
        }

        public int HitSequence { get; }
        public int FrameCount { get; }
        public string HitAtUtc { get; }
        public IReadOnlyList<UloopCapturedVariable> CapturedVariables { get; }
        public bool Truncated { get; }
    }
}
#endif
