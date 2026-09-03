using System.Collections.Generic;
using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Finds where "after line N" lands: the end of the selected statement's own IL range,
    /// extending across same-line fallthrough and compiler-hidden branch ranges when their
    /// conditionals jump forward beyond the same-line run. If the body exits, or another
    /// successor can skip line N, the capture remains before the relevant control transfer.
    /// </summary>
    internal static class SourcePausePointPostLineSiteLocator
    {
        public static SourcePausePointPostLineSite Locate(
            IReadOnlyList<SourcePausePointInstructionCandidate> instructions,
            IReadOnlyList<SourcePausePointSequencePointCandidate> points,
            int selectedPointIndex)
        {
            Debug.Assert(instructions != null && instructions.Count > 0, "instructions must not be empty.");
            Debug.Assert(points != null, "points must not be null.");
            Debug.Assert(
                selectedPointIndex >= 0 && selectedPointIndex < points.Count,
                "selectedPointIndex must address a sequence point.");

            SourcePausePointSequencePointCandidate selected = points[selectedPointIndex];
            int rangeStartOffset = selected.Offset;
            int sameLineRunEndOffset = FindSameLineRunEndOffset(points, selected);
            int lastIndex;
            int boundaryPointIndex;
            int firstCrossedConditionalBranchIndex = -1;
            while (true)
            {
                boundaryPointIndex = FindBoundaryPointIndex(points, rangeStartOffset);
                int rangeEndOffset = boundaryPointIndex < 0 ? int.MaxValue : points[boundaryPointIndex].Offset;
                lastIndex = FindLastInstructionIndexBefore(instructions, rangeEndOffset);
                Debug.Assert(lastIndex >= 0, "A sequence point range must contain at least one instruction.");

                if (!ContinuesOnSameLine(
                        instructions[lastIndex],
                        points,
                        boundaryPointIndex,
                        selected.StartLine,
                        sameLineRunEndOffset))
                {
                    break;
                }

                if (firstCrossedConditionalBranchIndex < 0 &&
                    instructions[lastIndex].Flow == SourcePausePointInstructionFlow.ConditionalBranch)
                {
                    firstCrossedConditionalBranchIndex = lastIndex;
                }

                // Same-line statements and compiler-hidden if branches extend through their
                // continuation. The first crossed branch remains the safe fallback when that
                // continuation exits instead of reaching the join point.
                rangeStartOffset = points[boundaryPointIndex].Offset;
            }

            if (firstCrossedConditionalBranchIndex >= 0 &&
                instructions[lastIndex].Flow != SourcePausePointInstructionFlow.Next)
            {
                int conditionalBranchOffset = instructions[firstCrossedConditionalBranchIndex].Offset;
                return new SourcePausePointPostLineSite(
                    SourcePausePointPostLineSiteKind.BeforeControlTransfer,
                    firstCrossedConditionalBranchIndex,
                    conditionalBranchOffset);
            }

            int scopeOffset = instructions[lastIndex].Offset;
            switch (instructions[lastIndex].Flow)
            {
                case SourcePausePointInstructionFlow.Throw:
                    return SourcePausePointPostLineSite.AlwaysThrows();
                case SourcePausePointInstructionFlow.Branch:
                case SourcePausePointInstructionFlow.ConditionalBranch:
                case SourcePausePointInstructionFlow.Return:
                    return new SourcePausePointPostLineSite(
                        SourcePausePointPostLineSiteKind.BeforeControlTransfer,
                        lastIndex,
                        scopeOffset);
                default:
                    Debug.Assert(
                        lastIndex + 1 < instructions.Count,
                        "A method body cannot fall through past its last instruction.");
                    return new SourcePausePointPostLineSite(
                        SourcePausePointPostLineSiteKind.Fallthrough,
                        lastIndex + 1,
                        scopeOffset);
            }
        }

        private static bool ContinuesOnSameLine(
            SourcePausePointInstructionCandidate last,
            IReadOnlyList<SourcePausePointSequencePointCandidate> points,
            int boundaryPointIndex,
            int selectedLine,
            int sameLineRunEndOffset)
        {
            if (boundaryPointIndex < 0)
            {
                return false;
            }

            SourcePausePointSequencePointCandidate boundary = points[boundaryPointIndex];
            if (boundary.Offset >= sameLineRunEndOffset)
            {
                return false;
            }

            if (last.Flow == SourcePausePointInstructionFlow.Next)
            {
                return true;
            }

            return last.Flow == SourcePausePointInstructionFlow.ConditionalBranch &&
                   last.BranchTargetOffset >= sameLineRunEndOffset;
        }

        private static int FindSameLineRunEndOffset(
            IReadOnlyList<SourcePausePointSequencePointCandidate> points,
            SourcePausePointSequencePointCandidate selected)
        {
            int rangeStartOffset = selected.Offset;
            while (true)
            {
                int boundaryPointIndex = FindBoundaryPointIndex(points, rangeStartOffset);
                if (boundaryPointIndex < 0)
                {
                    return int.MaxValue;
                }

                SourcePausePointSequencePointCandidate boundary = points[boundaryPointIndex];
                if (!IsPartOfSameLineRun(points, boundaryPointIndex, selected.StartLine))
                {
                    return boundary.Offset;
                }

                rangeStartOffset = boundary.Offset;
            }
        }

        private static bool IsPartOfSameLineRun(
            IReadOnlyList<SourcePausePointSequencePointCandidate> points,
            int pointIndex,
            int selectedLine)
        {
            SourcePausePointSequencePointCandidate point = points[pointIndex];
            if (!point.IsHidden)
            {
                return point.StartLine == selectedLine;
            }

            int nextPointIndex = FindBoundaryPointIndex(points, point.Offset);
            if (nextPointIndex < 0)
            {
                return false;
            }

            SourcePausePointSequencePointCandidate nextPoint = points[nextPointIndex];
            return !nextPoint.IsHidden && nextPoint.StartLine == selectedLine;
        }

        // The next sequence point in IL order after the range start, hidden ones included.
        // A hidden point belongs to a same-line run only when its immediate successor is a
        // visible point on that line; await and foreach continuations therefore remain bounds.
        private static int FindBoundaryPointIndex(
            IReadOnlyList<SourcePausePointSequencePointCandidate> points,
            int rangeStartOffset)
        {
            int boundaryIndex = -1;
            for (int index = 0; index < points.Count; index++)
            {
                int offset = points[index].Offset;
                if (offset <= rangeStartOffset)
                {
                    continue;
                }

                if (boundaryIndex < 0 || offset < points[boundaryIndex].Offset)
                {
                    boundaryIndex = index;
                }
            }

            return boundaryIndex;
        }

        private static int FindLastInstructionIndexBefore(
            IReadOnlyList<SourcePausePointInstructionCandidate> instructions,
            int rangeEndOffset)
        {
            int lastIndex = -1;
            for (int index = 0; index < instructions.Count; index++)
            {
                if (instructions[index].Offset >= rangeEndOffset)
                {
                    break;
                }

                lastIndex = index;
            }

            return lastIndex;
        }
    }
}
