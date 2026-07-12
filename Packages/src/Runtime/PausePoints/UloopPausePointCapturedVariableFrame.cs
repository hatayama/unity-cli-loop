#if UNITY_EDITOR
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.Runtime
{
    /// <summary>
    /// Shared demangled capture output consumed by both the string formatter and raw-ref holder.
    /// </summary>
    internal sealed class UloopPausePointCapturedVariableFrame
    {
        public UloopPausePointCapturedVariableFrame(
            IReadOnlyList<UloopPausePointCapturedVariableEntry> entries, bool truncated)
        {
            Entries = entries;
            Truncated = truncated;
        }

        public IReadOnlyList<UloopPausePointCapturedVariableEntry> Entries { get; }
        public bool Truncated { get; }
    }
}
#endif
