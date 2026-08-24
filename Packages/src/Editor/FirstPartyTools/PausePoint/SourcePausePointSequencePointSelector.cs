using System.Collections.Generic;
using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Picks the closest sequence point on or after a requested line after dropping
    /// points whose IL offset is inverted relative to a later, smaller StartLine.
    /// </summary>
    internal static class SourcePausePointSequencePointSelector
    {
        public static int SelectIndex(
            IReadOnlyList<SourcePausePointSequencePointCandidate> points,
            int requestedLine,
            int sourceEndLine)
        {
            Debug.Assert(points != null, "points must not be null.");
            Debug.Assert(requestedLine > 0, "requestedLine must be a positive 1-based line.");

            int bestIndex = -1;
            for (int index = 0; index < points.Count; index++)
            {
                SourcePausePointSequencePointCandidate candidate = points[index];
                if (candidate.IsHidden
                    || candidate.StartLine < requestedLine
                    || candidate.StartLine > sourceEndLine)
                {
                    continue;
                }

                if (IsDuplicateLineInverted(points, candidate))
                {
                    continue;
                }

                if (bestIndex < 0 || candidate.StartLine < points[bestIndex].StartLine)
                {
                    bestIndex = index;
                }
            }

            return bestIndex;
        }

        private static bool IsDuplicateLineInverted(
            IReadOnlyList<SourcePausePointSequencePointCandidate> points,
            SourcePausePointSequencePointCandidate candidate)
        {
            // Why duplicate-line only: a for-increment SP has a smaller StartLine and a
            // larger offset than the body. Treating every such pair as fake would drop the
            // body and skip to after the loop, where the loop variable is out of scope.
            // The diagnosed #line fake shares a StartLine with the real statement SP.
            if (!HasDuplicateStartLine(points, candidate.StartLine))
            {
                return false;
            }

            // Why not "same StartLine → max offset": while/for legitimately emit two SPs
            // on one line; the first (loop head) must win, not the later condition SP.
            for (int index = 0; index < points.Count; index++)
            {
                SourcePausePointSequencePointCandidate other = points[index];
                if (other.IsHidden)
                {
                    continue;
                }

                if (other.StartLine < candidate.StartLine && other.Offset > candidate.Offset)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasDuplicateStartLine(
            IReadOnlyList<SourcePausePointSequencePointCandidate> points,
            int startLine)
        {
            int seen = 0;
            for (int index = 0; index < points.Count; index++)
            {
                SourcePausePointSequencePointCandidate other = points[index];
                if (other.IsHidden || other.StartLine != startLine)
                {
                    continue;
                }

                seen++;
                if (seen > 1)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
