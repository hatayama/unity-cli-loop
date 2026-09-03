using System.Collections.Generic;
using System.Diagnostics;

using Mono.Cecil;
using Mono.Cecil.Cil;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Applies post-line site selection to one Cecil method body and its PDB sequence points.
    /// </summary>
    internal static class SourcePausePointCecilPostLineSiteLocator
    {
        public static SourcePausePointPostLineSite Locate(MethodDefinition method, SequencePoint selected)
        {
            Debug.Assert(method != null && method.HasBody, "method must have a body.");
            Debug.Assert(selected != null, "selected must not be null.");

            List<SourcePausePointInstructionCandidate> instructions = new List<SourcePausePointInstructionCandidate>();
            foreach (Instruction instruction in method.Body.Instructions)
            {
                instructions.Add(
                    new SourcePausePointInstructionCandidate(instruction.Offset, ToFlow(instruction.OpCode)));
            }

            List<SourcePausePointSequencePointCandidate> points = new List<SourcePausePointSequencePointCandidate>();
            int selectedIndex = -1;
            foreach (SequencePoint sequencePoint in method.DebugInformation.SequencePoints)
            {
                if (ReferenceEquals(sequencePoint, selected))
                {
                    selectedIndex = points.Count;
                }

                points.Add(
                    new SourcePausePointSequencePointCandidate(
                        sequencePoint.StartLine,
                        sequencePoint.Offset,
                        sequencePoint.IsHidden));
            }

            Debug.Assert(selectedIndex >= 0, "selected must be one of the method's own sequence points.");
            return SourcePausePointPostLineSiteLocator.Locate(instructions, points, selectedIndex);
        }

        private static SourcePausePointInstructionFlow ToFlow(OpCode opCode)
        {
            switch (opCode.FlowControl)
            {
                case FlowControl.Branch:
                    return SourcePausePointInstructionFlow.Branch;
                case FlowControl.Cond_Branch:
                    return SourcePausePointInstructionFlow.ConditionalBranch;
                case FlowControl.Return:
                    return SourcePausePointInstructionFlow.Return;
                case FlowControl.Throw:
                    return SourcePausePointInstructionFlow.Throw;
                default:
                    return SourcePausePointInstructionFlow.Next;
            }
        }
    }
}
