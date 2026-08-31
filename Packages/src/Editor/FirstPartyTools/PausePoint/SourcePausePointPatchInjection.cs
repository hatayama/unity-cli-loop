using System.Collections.Generic;
using System.Reflection;

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
        public SourcePausePointPatchInjectionTargetKind TargetKind { get; }
        public MethodBase DonorShim { get; }
        public bool InstanceFromFirstArgument { get; }

        public SourcePausePointPatchInjection(
            string id,
            int instructionIndex,
            bool isStatic,
            bool isDeclaringTypeValueType,
            IReadOnlyList<SourcePausePointParameter> parameters,
            IReadOnlyList<SourcePausePointLocalVariable> locals,
            SourcePausePointPatchInjectionTargetKind targetKind =
                SourcePausePointPatchInjectionTargetKind.OriginalBody,
            MethodBase donorShim = null,
            bool instanceFromFirstArgument = false)
        {
            Id = id;
            InstructionIndex = instructionIndex;
            IsStatic = isStatic;
            IsDeclaringTypeValueType = isDeclaringTypeValueType;
            Parameters = parameters;
            Locals = locals;
            TargetKind = targetKind;
            DonorShim = donorShim;
            InstanceFromFirstArgument = instanceFromFirstArgument;
        }
    }
}
