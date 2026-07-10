using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// A resolved patch location: the target method, its insertion point, and the
    /// locals/parameters visible there.
    /// </summary>
    internal sealed class SourcePausePointResolution
    {
        public string AssemblyName { get; }
        public int MetadataToken { get; }
        public string MethodDisplayName { get; }
        public bool IsStatic { get; }
        public int InstructionIndex { get; }
        public int IlOffset { get; }
        public int ResolvedLine { get; }
        public IReadOnlyList<SourcePausePointLocalVariable> Locals { get; }
        public IReadOnlyList<SourcePausePointParameter> Parameters { get; }

        public SourcePausePointResolution(
            string assemblyName,
            int metadataToken,
            string methodDisplayName,
            bool isStatic,
            int instructionIndex,
            int ilOffset,
            int resolvedLine,
            IReadOnlyList<SourcePausePointLocalVariable> locals,
            IReadOnlyList<SourcePausePointParameter> parameters)
        {
            AssemblyName = assemblyName;
            MetadataToken = metadataToken;
            MethodDisplayName = methodDisplayName;
            IsStatic = isStatic;
            InstructionIndex = instructionIndex;
            IlOffset = ilOffset;
            ResolvedLine = resolvedLine;
            Locals = locals;
            Parameters = parameters;
        }
    }
}
