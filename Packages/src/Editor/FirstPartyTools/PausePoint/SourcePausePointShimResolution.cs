using System.Collections.Generic;
using System.Diagnostics;
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
        public SourcePausePointSnapshotTiming SnapshotTiming { get; }
        public int ResolvedLine { get; }
        public IReadOnlyList<SourcePausePointLocalVariable> Locals { get; }
        public IReadOnlyList<SourcePausePointParameter> Parameters { get; }
        public bool InstanceFromFirstArgument { get; }
        public string MethodDisplayName { get; }
        public string ErrorMessage { get; }
        public int SourceStartLine { get; }
        public int SourceEndLine { get; }

        private SourcePausePointShimResolution(
            SourcePausePointShimResolveKind kind,
            MethodBase targetMethod,
            MethodBase logicalOwner,
            MethodBase donorShim,
            int instructionIndex,
            SourcePausePointSnapshotTiming snapshotTiming,
            int resolvedLine,
            IReadOnlyList<SourcePausePointLocalVariable> locals,
            IReadOnlyList<SourcePausePointParameter> parameters,
            bool instanceFromFirstArgument,
            string methodDisplayName,
            string errorMessage,
            int sourceStartLine,
            int sourceEndLine)
        {
            Kind = kind;
            TargetMethod = targetMethod;
            LogicalOwner = logicalOwner;
            DonorShim = donorShim;
            InstructionIndex = instructionIndex;
            SnapshotTiming = snapshotTiming;
            ResolvedLine = resolvedLine;
            Locals = locals;
            Parameters = parameters;
            InstanceFromFirstArgument = instanceFromFirstArgument;
            MethodDisplayName = methodDisplayName;
            ErrorMessage = errorMessage;
            SourceStartLine = sourceStartLine;
            SourceEndLine = sourceEndLine;
        }

        public static SourcePausePointShimResolution TransplantChainJoin(
            MethodBase originalMethod,
            MethodBase donorShim,
            int instructionIndex,
            SourcePausePointSnapshotTiming snapshotTiming,
            int resolvedLine,
            IReadOnlyList<SourcePausePointLocalVariable> locals,
            IReadOnlyList<SourcePausePointParameter> parameters,
            int sourceStartLine,
            int sourceEndLine)
        {
            return new SourcePausePointShimResolution(
                SourcePausePointShimResolveKind.TransplantChainJoin,
                originalMethod,
                originalMethod,
                donorShim,
                instructionIndex,
                snapshotTiming,
                resolvedLine,
                locals,
                parameters,
                instanceFromFirstArgument: false,
                originalMethod.ToString(),
                errorMessage: null,
                sourceStartLine,
                sourceEndLine);
        }

        public static SourcePausePointShimResolution ShimDirect(
            MethodBase targetMethod,
            MethodBase logicalOwner,
            int instructionIndex,
            SourcePausePointSnapshotTiming snapshotTiming,
            int resolvedLine,
            IReadOnlyList<SourcePausePointLocalVariable> locals,
            IReadOnlyList<SourcePausePointParameter> parameters,
            bool instanceFromFirstArgument,
            int sourceStartLine,
            int sourceEndLine)
        {
            return new SourcePausePointShimResolution(
                SourcePausePointShimResolveKind.ShimDirect,
                targetMethod,
                logicalOwner,
                donorShim: null,
                instructionIndex,
                snapshotTiming,
                resolvedLine,
                locals,
                parameters,
                instanceFromFirstArgument,
                logicalOwner.ToString(),
                errorMessage: null,
                sourceStartLine,
                sourceEndLine);
        }

        public static SourcePausePointShimResolution NotInPatchedMethod()
        {
            return new SourcePausePointShimResolution(
                SourcePausePointShimResolveKind.NotInPatchedMethod,
                targetMethod: null,
                logicalOwner: null,
                donorShim: null,
                instructionIndex: -1,
                SourcePausePointSnapshotTiming.PreLine,
                resolvedLine: 0,
                locals: null,
                parameters: null,
                instanceFromFirstArgument: false,
                methodDisplayName: null,
                errorMessage: null,
                sourceStartLine: 0,
                sourceEndLine: 0);
        }

        public static SourcePausePointShimResolution PatchedMethodPdbUnavailable(MethodBase logicalOwner)
        {
            Debug.Assert(logicalOwner != null, "logicalOwner must not be null.");
            string typeName = logicalOwner.DeclaringType != null ? logicalOwner.DeclaringType.Name : "?";
            return new SourcePausePointShimResolution(
                SourcePausePointShimResolveKind.PatchedMethodPdbUnavailable,
                targetMethod: null,
                logicalOwner,
                donorShim: null,
                instructionIndex: -1,
                SourcePausePointSnapshotTiming.PreLine,
                resolvedLine: 0,
                locals: null,
                parameters: null,
                instanceFromFirstArgument: false,
                typeName + "." + logicalOwner.Name,
                errorMessage: null,
                sourceStartLine: 0,
                sourceEndLine: 0);
        }

        public static SourcePausePointShimResolution NoStatementInPatchedMethod(
            MethodBase logicalOwner,
            string errorMessage)
        {
            return new SourcePausePointShimResolution(
                SourcePausePointShimResolveKind.NoStatementInPatchedMethod,
                targetMethod: null,
                logicalOwner,
                donorShim: null,
                instructionIndex: -1,
                SourcePausePointSnapshotTiming.PreLine,
                resolvedLine: 0,
                locals: null,
                parameters: null,
                instanceFromFirstArgument: false,
                logicalOwner.ToString(),
                errorMessage,
                sourceStartLine: 0,
                sourceEndLine: 0);
        }
    }
}
