using System.Collections.Generic;
using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Finds where "after line N" lands: the end of the selected statement's own IL range,
    /// never a successor sequence point. A successor is a join point that other paths (an
    /// early return, the else branch) reach too, so injecting there would capture executions
    /// that never ran line N.
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
            int lastIndex;
            int boundaryPointIndex;
            while (true)
            {
                boundaryPointIndex = FindBoundaryPointIndex(points, rangeStartOffset);
                int rangeEndOffset = boundaryPointIndex < 0 ? int.MaxValue : points[boundaryPointIndex].Offset;
                lastIndex = FindLastInstructionIndexBefore(instructions, rangeEndOffset);
                Debug.Assert(lastIndex >= 0, "A sequence point range must contain at least one instruction.");

                if (!ContinuesOnSameLine(instructions[lastIndex], points, boundaryPointIndex, selected.StartLine))
                {
                    break;
                }

                // Why extend: `a = 1; b = 2;` on one line is two sequence points, and "after
                // this line" means after both. A range that ends in a branch (a for-loop
                // initializer jumping to its same-line condition) is never extended.
                rangeStartOffset = points[boundaryPointIndex].Offset;
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
            int selectedLine)
        {
            if (last.Flow != SourcePausePointInstructionFlow.Next || boundaryPointIndex < 0)
            {
                return false;
            }

            SourcePausePointSequencePointCandidate boundary = points[boundaryPointIndex];
            return !boundary.IsHidden && boundary.StartLine == selectedLine;
        }

        // The next sequence point in IL order after the range start, hidden ones included:
        // a hidden point marks compiler-generated code (await continuations, foreach
        // MoveNext) that is not part of the requested statement.
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
