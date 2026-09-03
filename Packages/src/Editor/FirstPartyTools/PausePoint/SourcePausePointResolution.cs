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
        public string Mvid { get; }
        public int MetadataToken { get; }
        public string MethodDisplayName { get; }
        public bool IsStatic { get; }
        public bool IsDeclaringTypeValueType { get; }
        public int InstructionIndex { get; }
        public int IlOffset { get; }
        public SourcePausePointSnapshotTiming SnapshotTiming { get; }
        public int ResolvedLine { get; }
        // Sequence-point EndLine is internal transport for joining ResolvedLineText.
        // It is not a CLI response field.
        public int ResolvedEndLine { get; }
        // Compiled method span is internal transport for the patched-by-hot-reload
        // failure message. It is not a CLI response field.
        public int CompiledMethodStartLine { get; }
        public int CompiledMethodEndLine { get; }
        public IReadOnlyList<SourcePausePointLocalVariable> Locals { get; }
        public IReadOnlyList<SourcePausePointParameter> Parameters { get; }
        // Parameters left out of Parameters because their type cannot be boxed, each with the
        // reason. Reported so a missing name is explained instead of looking like a capture bug.
        public IReadOnlyList<string> NotCapturableVariables { get; }

        public SourcePausePointResolution(
            string assemblyName,
            string mvid,
            int metadataToken,
            string methodDisplayName,
            bool isStatic,
            bool isDeclaringTypeValueType,
            int instructionIndex,
            int ilOffset,
            SourcePausePointSnapshotTiming snapshotTiming,
            int resolvedLine,
            int resolvedEndLine,
            int compiledMethodStartLine,
            int compiledMethodEndLine,
            IReadOnlyList<SourcePausePointLocalVariable> locals,
            IReadOnlyList<SourcePausePointParameter> parameters,
            IReadOnlyList<string> notCapturableVariables)
        {
            AssemblyName = assemblyName;
            Mvid = mvid;
            MetadataToken = metadataToken;
            MethodDisplayName = methodDisplayName;
            IsStatic = isStatic;
            IsDeclaringTypeValueType = isDeclaringTypeValueType;
            InstructionIndex = instructionIndex;
            IlOffset = ilOffset;
            SnapshotTiming = snapshotTiming;
            ResolvedLine = resolvedLine;
            ResolvedEndLine = resolvedEndLine;
            CompiledMethodStartLine = compiledMethodStartLine;
            CompiledMethodEndLine = compiledMethodEndLine;
            Locals = locals;
            Parameters = parameters;
            NotCapturableVariables = notCapturableVariables;
        }
    }
}
