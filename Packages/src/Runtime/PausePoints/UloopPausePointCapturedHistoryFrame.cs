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
            bool truncated,
            IReadOnlyList<UloopPausePointCallerFrame> callerFrames)
        {
            HitSequence = hitSequence;
            FrameCount = frameCount;
            HitAtUtc = hitAtUtc;
            CapturedVariables = capturedVariables;
            Truncated = truncated;
            CallerFrames = callerFrames;
        }

        public int HitSequence { get; }
        public int FrameCount { get; }
        public string HitAtUtc { get; }
        public IReadOnlyList<UloopCapturedVariable> CapturedVariables { get; }
        public bool Truncated { get; }
        public IReadOnlyList<UloopPausePointCallerFrame> CallerFrames { get; }
    }
}
#endif
