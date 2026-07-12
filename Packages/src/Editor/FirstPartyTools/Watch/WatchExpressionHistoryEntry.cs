using System;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Represents one frame-indexed watch evaluation retained by the registry.
    /// </summary>
    public sealed class WatchExpressionHistoryEntry
    {
        public WatchExpressionHistoryEntry(int frameCount, DateTime evaluatedAtUtc, WatchEvaluationResult result)
        {
            FrameCount = frameCount;
            EvaluatedAtUtc = evaluatedAtUtc;
            Result = result;
        }

        public int FrameCount { get; }
        public DateTime EvaluatedAtUtc { get; }
        public WatchEvaluationResult Result { get; }
    }
}
