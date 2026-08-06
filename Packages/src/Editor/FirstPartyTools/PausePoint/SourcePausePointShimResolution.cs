using System.Collections.Generic;
using System.Reflection;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Shim-path resolution for enabling a pause point on a hot-reload patched method body.
    /// </summary>
    internal sealed class SourcePausePointShimResolution
    {
        public SourcePausePointShimResolveKind Kind { get; }
        public MethodBase TargetMethod { get; }
        public MethodBase LogicalOwner { get; }
        public MethodBase DonorShim { get; }
        public int InstructionIndex { get; }
        public int ResolvedLine { get; }
        public IReadOnlyList<SourcePausePointLocalVariable> Locals { get; }
        public IReadOnlyList<SourcePausePointParameter> Parameters { get; }
        public bool InstanceFromFirstArgument { get; }
        public string MethodDisplayName { get; }
        public string ErrorMessage { get; }

        private SourcePausePointShimResolution(
            SourcePausePointShimResolveKind kind,
            MethodBase targetMethod,
            MethodBase logicalOwner,
            MethodBase donorShim,
            int instructionIndex,
            int resolvedLine,
            IReadOnlyList<SourcePausePointLocalVariable> locals,
            IReadOnlyList<SourcePausePointParameter> parameters,
            bool instanceFromFirstArgument,
            string methodDisplayName,
            string errorMessage)
        {
            Kind = kind;
            TargetMethod = targetMethod;
            LogicalOwner = logicalOwner;
            DonorShim = donorShim;
            InstructionIndex = instructionIndex;
            ResolvedLine = resolvedLine;
            Locals = locals;
            Parameters = parameters;
            InstanceFromFirstArgument = instanceFromFirstArgument;
            MethodDisplayName = methodDisplayName;
            ErrorMessage = errorMessage;
        }

        public static SourcePausePointShimResolution TransplantChainJoin(
            MethodBase originalMethod,
            MethodBase donorShim,
            int instructionIndex,
            int resolvedLine,
            IReadOnlyList<SourcePausePointLocalVariable> locals,
            IReadOnlyList<SourcePausePointParameter> parameters)
        {
            return new SourcePausePointShimResolution(
                SourcePausePointShimResolveKind.TransplantChainJoin,
                originalMethod,
                originalMethod,
                donorShim,
                instructionIndex,
                resolvedLine,
                locals,
                parameters,
                instanceFromFirstArgument: false,
                originalMethod.ToString(),
                errorMessage: null);
        }

        public static SourcePausePointShimResolution ShimDirect(
            MethodBase targetMethod,
            MethodBase logicalOwner,
            int instructionIndex,
            int resolvedLine,
            IReadOnlyList<SourcePausePointLocalVariable> locals,
            IReadOnlyList<SourcePausePointParameter> parameters,
            bool instanceFromFirstArgument)
        {
            return new SourcePausePointShimResolution(
                SourcePausePointShimResolveKind.ShimDirect,
                targetMethod,
                logicalOwner,
                donorShim: null,
                instructionIndex,
                resolvedLine,
                locals,
                parameters,
                instanceFromFirstArgument,
                logicalOwner.ToString(),
                errorMessage: null);
        }

        public static SourcePausePointShimResolution NotInPatchedMethod()
        {
            return new SourcePausePointShimResolution(
                SourcePausePointShimResolveKind.NotInPatchedMethod,
                targetMethod: null,
                logicalOwner: null,
                donorShim: null,
                instructionIndex: -1,
                resolvedLine: 0,
                locals: null,
                parameters: null,
                instanceFromFirstArgument: false,
                methodDisplayName: null,
                errorMessage: null);
        }

        public static SourcePausePointShimResolution NoStatementInPatchedMethod(
            MethodBase logicalOwner,
            int line,
            string errorMessage)
        {
            return new SourcePausePointShimResolution(
                SourcePausePointShimResolveKind.NoStatementInPatchedMethod,
                targetMethod: null,
                logicalOwner,
                donorShim: null,
                instructionIndex: -1,
                resolvedLine: 0,
                locals: null,
                parameters: null,
                instanceFromFirstArgument: false,
                logicalOwner != null ? logicalOwner.ToString() : null,
                errorMessage);
        }
    }
}
