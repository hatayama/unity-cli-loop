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
            int line)
        {
            Debug.Assert(lookup != null, "lookup must not be null.");
            Debug.Assert(!string.IsNullOrEmpty(normalizedFilePath), "normalizedFilePath must not be empty.");
            Debug.Assert(line > 0, "line must be a positive 1-based line number.");

            HotReloadShimMethodLookup methodEntry = FindMethodEntryForLine(lookup, line);
            if (methodEntry == null)
            {
                return SourcePausePointShimResolution.NotInPatchedMethod();
            }

            using MemoryStream assemblyStream = new MemoryStream(lookup.AssemblyBytes, writable: false);
            using MemoryStream pdbStream = new MemoryStream(
                lookup.PdbBytes ?? System.Array.Empty<byte>(),
                writable: false);

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
                string displayName = methodEntry.OriginalMethod != null
                    ? methodEntry.OriginalMethod.DeclaringType?.Name + "." + methodEntry.OriginalMethod.Name
                    : "unknown";
                return SourcePausePointShimResolution.NoStatementInPatchedMethod(
                    methodEntry.OriginalMethod,
                    line,
                    "No executable statement at or after line " + line
                    + " inside the hot-reload patched method '" + displayName
                    + "'. The patched body was compiled from the current source, so line numbers "
                    + "match the file on disk.");
            }

            int instructionIndex = SourcePausePointResolver.FindInstructionIndex(
                containingMethod.Body.Instructions,
                sequencePoint.Offset);
            Debug.Assert(
                instructionIndex >= 0,
                "A sequence point's offset must correspond to an instruction in the same method body.");

            List<SourcePausePointLocalVariable> locals =
                SourcePausePointResolver.CollectCapturableLocals(containingMethod, sequencePoint.Offset);
            // Why fallback: shim PDBs (especially with #line) can place the return sequence point
            // at an offset after lexical local scopes close, yielding an empty in-scope set even
            // though the locals are still live for capture on that statement.
            if (locals.Count == 0)
            {
                locals = SourcePausePointResolver.CollectAllCapturableLocals(containingMethod);
            }

            int containingToken = containingMethod.MetadataToken.ToInt32();
            int shimToken = methodEntry.ShimMethod.MetadataToken;
            bool isShimMethodBody = containingToken == shimToken;

            if (isShimMethodBody && !methodEntry.IsDelegation)
            {
                List<SourcePausePointParameter> parameters =
                    SourcePausePointResolver.CollectParametersFromReflection(
                        methodEntry.OriginalMethod,
                        skipFirstParameter: false);
                return SourcePausePointShimResolution.TransplantChainJoin(
                    methodEntry.OriginalMethod,
                    methodEntry.ShimMethod,
                    instructionIndex,
                    sequencePoint.StartLine,
                    locals,
                    parameters);
            }

            MethodBase targetMethod =
                lookup.LoadedAssembly.ManifestModule.ResolveMethod(containingToken);
            bool instanceFromFirstArgument =
                isShimMethodBody
                && methodEntry.IsDelegation
                && methodEntry.OriginalMethod != null
                && !methodEntry.OriginalMethod.IsStatic;
            List<SourcePausePointParameter> shimParameters =
                SourcePausePointResolver.CollectParametersFromReflection(
                    targetMethod,
                    skipFirstParameter: instanceFromFirstArgument);

            return SourcePausePointShimResolution.ShimDirect(
                targetMethod,
                methodEntry.OriginalMethod,
                instructionIndex,
                sequencePoint.StartLine,
                locals,
                shimParameters,
                instanceFromFirstArgument);
        }

        private static HotReloadShimMethodLookup FindMethodEntryForLine(
            HotReloadShimFileLookup lookup,
            int line)
        {
            foreach (HotReloadShimMethodLookup method in lookup.Methods)
            {
                if (line >= method.SourceStartLine && line <= method.SourceEndLine)
                {
                    return method;
                }
            }

            return null;
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
                if (!method.HasBody)
                {
                    continue;
                }

                MethodDebugInformation debugInformation = method.DebugInformation;
                if (debugInformation == null || !debugInformation.HasSequencePoints)
                {
                    continue;
                }

                foreach (SequencePoint sequencePoint in debugInformation.SequencePoints)
                {
                    if (sequencePoint.IsHidden
                        || sequencePoint.StartLine < line
                        || sequencePoint.StartLine > sourceEndLine)
                    {
                        continue;
                    }

                    // Why request-first arg order: shim PDB Document.Url is the #line project-relative
                    // literal while --file may be absolute; PathsReferToSameFile suffixes the first
                    // argument, so the absolute request must be first (opposite of ScriptAssemblies).
                    string documentUrl = SourcePausePointPathNormalizer.ToForwardSlashes(
                        sequencePoint.Document.Url);
                    if (!SourcePausePointPathNormalizer.PathsReferToSameFile(
                            normalizedFilePath,
                            documentUrl))
                    {
                        continue;
                    }

                    if (bestSequencePoint == null || sequencePoint.StartLine < bestSequencePoint.StartLine)
                    {
                        bestMethod = method;
                        bestSequencePoint = sequencePoint;
                    }
                }
            }

            return (bestMethod, bestSequencePoint);
        }
    }
}
