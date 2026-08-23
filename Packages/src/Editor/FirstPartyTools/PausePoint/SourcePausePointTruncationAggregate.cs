using System;
using System.Collections.Generic;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.Runtime;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Merges count-cap drops with per-entry preview clipping so TruncatedVariableCount
    /// and TruncatedVariableNames match CapturedVariablesTruncated.
    /// </summary>
    internal static class SourcePausePointTruncationAggregate
    {
        public static UloopPausePointCapturedVariableFrame Merge(
            UloopPausePointCapturedVariableFrame frame,
            IReadOnlyList<UloopCapturedVariable> variables)
        {
            Debug.Assert(frame != null, "frame must not be null");
            Debug.Assert(variables != null, "variables must not be null");

            List<string> names = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            int previewClippedCount = 0;

            for (int index = 0; index < variables.Count; index++)
            {
                UloopCapturedVariable variable = variables[index];
                if (!variable.Truncated)
                {
                    continue;
                }

                // Why count before name checks: CapturedVariablesTruncated==(Count>0) must
                // stay true even when a clipped entry has a null or empty name.
                previewClippedCount++;
                if (string.IsNullOrEmpty(variable.Name) || !seen.Add(variable.Name))
                {
                    continue;
                }

                TryAddReportedName(names, variable.Name);
            }

            IReadOnlyList<string> droppedNames = frame.TruncatedVariableNames;
            for (int index = 0; index < droppedNames.Count; index++)
            {
                string droppedName = droppedNames[index];
                if (string.IsNullOrEmpty(droppedName) || !seen.Add(droppedName))
                {
                    continue;
                }

                TryAddReportedName(names, droppedName);
            }

            // Why add the collector count as a whole: TruncatedVariableNames is capped at 20,
            // so walking that list cannot recover the exact dropped-whole total.
            int count = previewClippedCount + frame.TruncatedVariableCount;
            bool truncated = count > 0;
            return new UloopPausePointCapturedVariableFrame(
                frame.Entries,
                truncated,
                names,
                count);
        }

        private static void TryAddReportedName(List<string> names, string name)
        {
            if (names.Count < SourcePausePointConstants.MaxTruncatedVariableNamesReported)
            {
                names.Add(name);
            }
        }
    }
}
