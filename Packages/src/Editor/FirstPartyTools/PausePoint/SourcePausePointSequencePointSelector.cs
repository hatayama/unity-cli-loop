using System.Collections.Generic;
using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Picks the closest sequence point on or after a requested line after dropping
    /// inverted duplicate-line points whose later same-line partner has an intervening
    /// smaller StartLine.
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
            // Why a same-line partner plus an in-between witness: an outer for-increment
            // SP has a smaller StartLine and a larger offset than an inner header, but it
            // sits after the inner header's partner. Treating that as fake would drop every
            // inner-header SP and round into the body.
            int partnerOffset = FindLargerSameLineOffset(points, candidate);
            if (partnerOffset < 0)
            {
                return false;
            }

            // Why not "same StartLine → max offset": while/for legitimately emit two SPs
            // on one line; the first (loop head) must win, not the later condition SP.
            for (int index = 0; index < points.Count; index++)
            {
                SourcePausePointSequencePointCandidate witness = points[index];
                if (witness.IsHidden || witness.StartLine >= candidate.StartLine)
                {
                    continue;
                }

                if (witness.Offset > candidate.Offset && witness.Offset < partnerOffset)
                {
                    return true;
                }
            }

            return false;
        }

        private static int FindLargerSameLineOffset(
            IReadOnlyList<SourcePausePointSequencePointCandidate> points,
            SourcePausePointSequencePointCandidate candidate)
        {
            int partnerOffset = -1;
            for (int index = 0; index < points.Count; index++)
            {
                SourcePausePointSequencePointCandidate other = points[index];
                if (other.IsHidden
                    || other.StartLine != candidate.StartLine
                    || other.Offset <= candidate.Offset)
                {
                    continue;
                }

                if (partnerOffset < 0 || other.Offset < partnerOffset)
                {
                    partnerOffset = other.Offset;
                }
            }

            return partnerOffset;
        }
    }
}
