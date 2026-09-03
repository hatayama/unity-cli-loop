using System.Diagnostics;

using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Turns a selected sequence point plus the requested snapshot timing into the instruction
    /// index to inject before and the IL offset whose scope decides the capturable locals.
    /// Pre-line injects before the sequence point's first instruction; post-line asks the
    /// post-line site locator where the statement's own IL range ends.
    /// </summary>
    internal static class SourcePausePointInjectionSiteLocator
    {
        // Returns false only when the post-line range always throws.
        public static bool TryLocate(
            MethodDefinition method,
            SequencePoint sequencePoint,
            SourcePausePointSnapshotTiming snapshotTiming,
            out int instructionIndex,
            out int scopeOffset)
        {
            if (snapshotTiming == SourcePausePointSnapshotTiming.PreLine)
            {
                instructionIndex = FindInstructionIndex(method.Body.Instructions, sequencePoint.Offset);
                Debug.Assert(instructionIndex >= 0, "A sequence point's offset must correspond to an instruction in the same method body.");
                scopeOffset = sequencePoint.Offset;
                return true;
            }

            SourcePausePointPostLineSite site = SourcePausePointCecilPostLineSiteLocator.Locate(method, sequencePoint);
            if (site.Kind == SourcePausePointPostLineSiteKind.AlwaysThrows)
            {
                instructionIndex = -1;
                scopeOffset = -1;
                return false;
            }

            instructionIndex = site.InstructionIndex;
            scopeOffset = site.ScopeOffset;
            return true;
        }

        public static int FindInstructionIndex(Collection<Instruction> instructions, int offset)
        {
            for (int i = 0; i < instructions.Count; i++)
            {
                if (instructions[i].Offset == offset)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
