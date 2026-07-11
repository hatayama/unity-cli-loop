using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// One armed pause point's worth of context needed to emit its Capture call site, keyed
    /// by the method it was patched into (a method may hold several of these at once).
    /// </summary>
    internal sealed class SourcePausePointPatchInjection
    {
        public string Id { get; }
        public int InstructionIndex { get; }
        public bool IsStatic { get; }
        public bool IsDeclaringTypeValueType { get; }
        public IReadOnlyList<SourcePausePointParameter> Parameters { get; }
        public IReadOnlyList<SourcePausePointLocalVariable> Locals { get; }

        public SourcePausePointPatchInjection(
            string id,
            int instructionIndex,
            bool isStatic,
            bool isDeclaringTypeValueType,
            IReadOnlyList<SourcePausePointParameter> parameters,
            IReadOnlyList<SourcePausePointLocalVariable> locals)
        {
            Id = id;
            InstructionIndex = instructionIndex;
            IsStatic = isStatic;
            IsDeclaringTypeValueType = isDeclaringTypeValueType;
            Parameters = parameters;
            Locals = locals;
        }
    }
}
