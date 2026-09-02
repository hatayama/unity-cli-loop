using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;

using Mono.Cecil;
using Mono.Cecil.Cil;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Resolves a file:line against an active hot-reload shim generation (bytes + PDB),
    /// choosing transplant chain-join vs shim-direct patching.
    /// </summary>
    internal static class SourcePausePointShimResolver
    {
        public static SourcePausePointShimResolution Resolve(
            HotReloadShimFileLookup lookup,
            string normalizedFilePath,
            int line,
            string methodFilter = null,
            SourcePausePointSnapshotTiming snapshotTiming = SourcePausePointSnapshotTiming.PreLine)
        {
            Debug.Assert(lookup != null, "lookup must not be null.");
            Debug.Assert(!string.IsNullOrEmpty(normalizedFilePath), "normalizedFilePath must not be empty.");
            Debug.Assert(line > 0, "line must be a positive 1-based line number.");

            HotReloadShimMethodLookup methodEntry = FindMethodEntryForLine(lookup, line, methodFilter);
            if (methodEntry == null)
            {
                return SourcePausePointShimResolution.NotInPatchedMethod();
            }

            // Why a distinct Kind: fallback compile backends can omit PDB bytes; ReadSymbols
            // against an empty stream throws. Enable still falls through to the compiled
            // resolver, but must not claim patched methods resolve against the edited file.
            if (lookup.PdbBytes == null || lookup.PdbBytes.Length == 0)
            {
                return SourcePausePointShimResolution.PatchedMethodPdbUnavailable(methodEntry.OriginalMethod);
            }

            using MemoryStream assemblyStream = new MemoryStream(lookup.AssemblyBytes, writable: false);
            using MemoryStream pdbStream = new MemoryStream(lookup.PdbBytes, writable: false);

            ReaderParameters readerParameters = new ReaderParameters
            {
                InMemory = true,
                ReadSymbols = true,
                SymbolReaderProvider = new PortablePdbReaderProvider(),
                SymbolStream = pdbStream
            };

            using AssemblyDefinition assemblyDefinition =
                AssemblyDefinition.ReadAssembly(assemblyStream, readerParameters);

            (MethodDefinition containingMethod, SequencePoint sequencePoint) =
                FindClosestSequencePointInMethodRange(
                    assemblyDefinition.MainModule,
                    normalizedFilePath,
                    line,
                    methodEntry.SourceEndLine);

            if (containingMethod == null)
            {
                string displayName =
                    methodEntry.OriginalMethod.DeclaringType?.Name
                    + "."
                    + methodEntry.OriginalMethod.Name;
                return SourcePausePointShimResolution.NoStatementInPatchedMethod(
                    methodEntry.OriginalMethod,
                    "No executable statement at or after line " + line
                    + " inside the hot-reload patched method '" + displayName
                    + "'. The patched body was compiled from the current source, so line numbers "
                    + "match the file on disk.");
            }

            if (!SourcePausePointInjectionSiteLocator.TryLocate(
                    containingMethod,
                    sequencePoint,
                    snapshotTiming,
                    out int instructionIndex,
                    out int scopeOffset))
            {
                return SourcePausePointShimResolution.NoStatementInPatchedMethod(
                    methodEntry.OriginalMethod,
                    string.Format(
                        SourcePausePointConstants.PostLineAlwaysThrowsMessageFormat,
                        sequencePoint.StartLine,
                        normalizedFilePath));
            }

            List<SourcePausePointLocalVariable> locals =
                SourcePausePointCaptureEligibility.CollectCapturableLocals(containingMethod, scopeOffset);
            // Why fallback: observed during PR-3 development — shim PDBs with #line can place the
            // return sequence point after lexical local scopes close, so in-scope collection is
            // empty while named locals still exist in the method debug info.
            if (locals.Count == 0)
            {
                locals = SourcePausePointCaptureEligibility.CollectAllCapturableLocals(containingMethod);
            }

            int containingToken = containingMethod.MetadataToken.ToInt32();
            int shimToken = methodEntry.ShimMethod.MetadataToken;
            bool isShimMethodBody = containingToken == shimToken;

            if (isShimMethodBody && !methodEntry.IsDelegation)
            {
                List<SourcePausePointParameter> parameters =
                    SourcePausePointCaptureEligibility.CollectParametersFromReflection(
                        methodEntry.OriginalMethod,
                        skipFirstParameter: false);
                return SourcePausePointShimResolution.TransplantChainJoin(
                    methodEntry.OriginalMethod,
                    methodEntry.ShimMethod,
                    instructionIndex,
                    snapshotTiming,
                    sequencePoint.StartLine,
                    locals,
                    parameters,
                    methodEntry.SourceStartLine,
                    methodEntry.SourceEndLine);
            }

            MethodBase targetMethod =
                lookup.LoadedAssembly.ManifestModule.ResolveMethod(containingToken);
            bool instanceFromFirstArgument =
                isShimMethodBody
                && methodEntry.IsDelegation
                && !methodEntry.OriginalMethod.IsStatic;
            List<SourcePausePointParameter> shimParameters =
                SourcePausePointCaptureEligibility.CollectParametersFromReflection(
                    targetMethod,
                    skipFirstParameter: instanceFromFirstArgument);

            return SourcePausePointShimResolution.ShimDirect(
                targetMethod,
                methodEntry.OriginalMethod,
                instructionIndex,
                snapshotTiming,
                sequencePoint.StartLine,
                locals,
                shimParameters,
                instanceFromFirstArgument,
                methodEntry.SourceStartLine,
                methodEntry.SourceEndLine);
        }

        private static HotReloadShimMethodLookup FindMethodEntryForLine(
            HotReloadShimFileLookup lookup,
            int line,
            string methodFilter)
        {
            foreach (HotReloadShimMethodLookup method in lookup.Methods)
            {
                if (line < method.SourceStartLine || line > method.SourceEndLine)
                {
                    continue;
                }

                if (!OriginalMethodMatchesFilter(methodFilter, method.OriginalMethod))
                {
                    continue;
                }

                return method;
            }

            return null;
        }

        private static bool OriginalMethodMatchesFilter(string methodFilter, MethodBase originalMethod)
        {
            Debug.Assert(originalMethod != null, "originalMethod must not be null.");
            Type declaringType = originalMethod.DeclaringType;
            string declaringTypeName = declaringType != null ? declaringType.Name : "?";
            string nestedOuterTypeName =
                declaringType != null && declaringType.DeclaringType != null
                    ? declaringType.DeclaringType.Name
                    : null;
            return SourcePausePointResolver.MethodMatchesFilter(
                methodFilter,
                originalMethod.Name,
                declaringTypeName,
                nestedOuterTypeName);
        }

        private static (MethodDefinition method, SequencePoint sequencePoint)
            FindClosestSequencePointInMethodRange(
                ModuleDefinition module,
                string normalizedFilePath,
                int line,
                int sourceEndLine)
        {
            MethodDefinition bestMethod = null;
            SequencePoint bestSequencePoint = null;

            foreach (MethodDefinition method in SourcePausePointResolver.EnumerateMethodsInModule(module))
            {
                SequencePoint sequencePoint = SourcePausePointCecilSequencePointSelector.SelectInMethod(
                    method,
                    normalizedFilePath,
                    line,
                    sourceEndLine);
                if (sequencePoint == null)
                {
                    continue;
                }

                if (bestSequencePoint == null || sequencePoint.StartLine < bestSequencePoint.StartLine)
                {
                    bestMethod = method;
                    bestSequencePoint = sequencePoint;
                }
            }

            return (bestMethod, bestSequencePoint);
        }
    }
}
