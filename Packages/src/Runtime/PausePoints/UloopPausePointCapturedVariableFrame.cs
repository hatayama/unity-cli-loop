#if UNITY_EDITOR
using System;
using System.Collections.Generic;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.Runtime
{
    /// <summary>
    /// Shared demangled capture output consumed by both the string formatter and raw-ref holder.
    /// </summary>
    internal sealed class UloopPausePointCapturedVariableFrame
    {
        public UloopPausePointCapturedVariableFrame(
            IReadOnlyList<UloopPausePointCapturedVariableEntry> entries,
            bool truncated,
            IReadOnlyList<string> truncatedVariableNames,
            int truncatedVariableCount)
        {
            IReadOnlyList<string> names = truncatedVariableNames ?? Array.Empty<string>();
            Debug.Assert(truncated == (truncatedVariableCount > 0), "truncated must match a positive count");
            Debug.Assert(
                truncatedVariableCount >= names.Count,
                "truncatedVariableCount must be at least the reported name count");

            Entries = entries;
            Truncated = truncated;
            TruncatedVariableNames = names;
            TruncatedVariableCount = truncatedVariableCount;
        }

        public IReadOnlyList<UloopPausePointCapturedVariableEntry> Entries { get; }
        public bool Truncated { get; }

        // Names dropped by the variable-count cap or whose preview was clipped
        // (at most MaxTruncatedVariableNamesReported), in capture order.
        public IReadOnlyList<string> TruncatedVariableNames { get; }

        // Exact number of variables dropped by the count cap or whose preview was
        // clipped (not capped at the names list length).
        public int TruncatedVariableCount { get; }
    }
}
#endif
