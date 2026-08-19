using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;

using UnityEditor.Compilation;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Resolves a project-relative file:line into a patchable method, instruction index,
    /// and the locals/parameters visible there, by reading the compiled assembly's portable PDB.
    /// </summary>
    internal static class SourcePausePointResolver
    {
        public static SourcePausePointResolveResult Resolve(
            string projectRelativeFilePath,
            int line,
            string methodFilter = null)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRelativeFilePath), "projectRelativeFilePath must not be null or empty.");
            Debug.Assert(line > 0, "line must be a positive 1-based line number.");

            string normalizedInputPath = SourcePausePointPathNormalizer.ToForwardSlashes(projectRelativeFilePath);

            string rawAssemblyName = CompilationPipeline.GetAssemblyNameFromScriptPath(normalizedInputPath);
            if (string.IsNullOrEmpty(rawAssemblyName))
            {
                return SourcePausePointResolveResult.Failure(
                    SourcePausePointResolveFailureReason.ScriptNotInAnyAssembly,
                    $"'{projectRelativeFilePath}' does not belong to any compiled assembly.");
            }

            // CompilationPipeline.GetAssemblyNameFromScriptPath returns the TargetAssembly's file name,
            // which already carries a ".dll" suffix (see Unity's EditorBuildRules.TargetAssembly).
            string assemblyName = Path.GetFileNameWithoutExtension(rawAssemblyName);

            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            string dllPath = Path.Combine(projectRoot, SourcePausePointConstants.ScriptAssembliesRelativeDirectory, assemblyName + SourcePausePointConstants.CompiledAssemblyExtension);
            string pdbPath = Path.Combine(projectRoot, SourcePausePointConstants.ScriptAssembliesRelativeDirectory, assemblyName + SourcePausePointConstants.DebugSymbolsExtension);

            if (!File.Exists(dllPath))
            {
                return SourcePausePointResolveResult.Failure(
                    SourcePausePointResolveFailureReason.CompiledAssemblyNotFound,
                    $"Compiled assembly not found at '{dllPath}'. Compile the project first.");
            }

            if (!File.Exists(pdbPath))
            {
                return SourcePausePointResolveResult.Failure(
                    SourcePausePointResolveFailureReason.SymbolsUnavailable,
                    $"Debug symbols not found at '{pdbPath}'. Ensure the project uses Debug code optimization.");
            }

            return ResolveFromCompiledAssembly(
                assemblyName,
                dllPath,
                pdbPath,
                normalizedInputPath,
                projectRelativeFilePath,
                line,
                methodFilter);
        }

        private static SourcePausePointResolveResult ResolveFromCompiledAssembly(
            string assemblyName,
            string dllPath,
            string pdbPath,
            string normalizedInputPath,
            string originalInputPath,
            int line,
            string methodFilter)
        {
            using FileStream dllStream = File.Open(dllPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using FileStream pdbStream = File.Open(pdbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            ReaderParameters readerParameters = new ReaderParameters
            {
                InMemory = true,
                ReadSymbols = true,
                SymbolReaderProvider = new PortablePdbReaderProvider(),
                SymbolStream = pdbStream
            };

            using AssemblyDefinition assemblyDefinition = AssemblyDefinition.ReadAssembly(dllStream, readerParameters);

            (MethodDefinition method, SequencePoint sequencePoint) = FindClosestSequencePointOnOrAfterLine(
                assemblyDefinition.MainModule, normalizedInputPath, line, methodFilter);

            if (method == null)
            {
                IReadOnlyList<SourcePausePointNearbyCompiledMethod> nearbyCompiledMethods =
                    FindNearbyCompiledMethods(assemblyDefinition.MainModule, normalizedInputPath, line);
                string errorMessage = string.IsNullOrEmpty(methodFilter)
                    ? $"No sequence point found on or after line {line} in '{originalInputPath}'."
                    : string.Format(
                        SourcePausePointConstants.NoMethodNamedWithSequencePointMessageFormat,
                        methodFilter,
                        line);
                return SourcePausePointResolveResult.Failure(
                    SourcePausePointResolveFailureReason.NoSequencePointOnOrAfterLine,
                    errorMessage,
                    nearbyCompiledMethods);
            }

            int instructionIndex = FindInstructionIndex(method.Body.Instructions, sequencePoint.Offset);
            Debug.Assert(instructionIndex >= 0, "A sequence point's offset must correspond to an instruction in the same method body.");

            List<SourcePausePointLocalVariable> locals = SourcePausePointCaptureEligibility.CollectCapturableLocals(method, sequencePoint.Offset);
            List<SourcePausePointParameter> parameters = SourcePausePointCaptureEligibility.CollectParameters(method);
            (int compiledMethodStartLine, int compiledMethodEndLine) = CollectCompiledMethodSpan(
                method,
                normalizedInputPath);

            SourcePausePointResolution resolution = new SourcePausePointResolution(
                assemblyName,
                assemblyDefinition.MainModule.Mvid.ToString(),
                method.MetadataToken.ToInt32(),
                method.FullName,
                method.IsStatic,
                method.DeclaringType.IsValueType,
                instructionIndex,
                sequencePoint.Offset,
                sequencePoint.StartLine,
                sequencePoint.EndLine,
                compiledMethodStartLine,
                compiledMethodEndLine,
                locals,
                parameters);

            return SourcePausePointResolveResult.SuccessResult(resolution);
        }

        private static (MethodDefinition method, SequencePoint sequencePoint) FindClosestSequencePointOnOrAfterLine(
            ModuleDefinition module, string normalizedInputPath, int line, string methodFilter)
        {
            MethodDefinition bestMethod = null;
            SequencePoint bestSequencePoint = null;

            foreach (MethodDefinition method in EnumerateMethodsInModule(module))
            {
                if (!method.HasBody)
                {
                    continue;
                }

                if (!CompiledMethodMatchesFilter(methodFilter, method))
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
                    if (sequencePoint.IsHidden || sequencePoint.StartLine < line)
                    {
                        continue;
                    }

                    if (!SourcePausePointPathNormalizer.PathsReferToSameFile(sequencePoint.Document.Url, normalizedInputPath))
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

        private static bool CompiledMethodMatchesFilter(string methodFilter, MethodDefinition method)
        {
            string declaringTypeName = method.DeclaringType != null ? method.DeclaringType.Name : "?";
            string nestedOuterTypeName =
                method.DeclaringType != null && method.DeclaringType.DeclaringType != null
                    ? method.DeclaringType.DeclaringType.Name
                    : null;
            return MethodMatchesFilter(methodFilter, method.Name, declaringTypeName, nestedOuterTypeName);
        }

        // Why Type.Name only: PR-2 short names keep the last type segment, so `Type.Method`
        // matches the same label agents already see in skip reasons.
        internal static bool MethodMatchesFilter(
            string methodFilter,
            string methodName,
            string declaringTypeName,
            string nestedOuterTypeName = null)
        {
            if (string.IsNullOrEmpty(methodFilter))
            {
                return true;
            }

            Debug.Assert(!string.IsNullOrEmpty(methodName), "methodName must not be empty.");
            (string logicalMethodName, string logicalTypeName) = ToLogicalFilterNames(
                methodName,
                declaringTypeName,
                nestedOuterTypeName);
            if (methodFilter.IndexOf('.') >= 0)
            {
                return string.Equals(
                    logicalTypeName + "." + logicalMethodName,
                    methodFilter,
                    StringComparison.Ordinal);
            }

            return string.Equals(logicalMethodName, methodFilter, StringComparison.Ordinal);
        }

        internal static (string MethodName, string DeclaringTypeName) ToLogicalFilterNames(
            string methodName,
            string declaringTypeName,
            string nestedOuterTypeName)
        {
            Debug.Assert(!string.IsNullOrEmpty(methodName), "methodName must not be empty.");
            string typeName = string.IsNullOrEmpty(declaringTypeName) ? "?" : declaringTypeName;
            Match stateMachine = SourcePausePointConstants.StateMachineTypeNamePattern.Match(typeName);
            if (stateMachine.Success)
            {
                string outerTypeName = string.IsNullOrEmpty(nestedOuterTypeName) ? "?" : nestedOuterTypeName;
                return (stateMachine.Groups[1].Value, outerTypeName);
            }

            Match localFunction = SourcePausePointConstants.LocalFunctionMethodNamePattern.Match(methodName);
            if (localFunction.Success)
            {
                return (localFunction.Groups[1].Value, typeName);
            }

            return (methodName, typeName);
        }

        // Why after bestMethod is chosen: the candidate loop walks every method in the
        // document and also skips StartLine < requested line, so min/max there would be
        // the whole file or a truncated body. Hidden points are 0xFEEFEE (16707566).
        private static (int startLine, int endLine) CollectCompiledMethodSpan(
            MethodDefinition method,
            string normalizedInputPath)
        {
            Debug.Assert(method != null, "method must not be null.");
            Debug.Assert(!string.IsNullOrEmpty(normalizedInputPath), "normalizedInputPath must not be empty.");

            MethodDebugInformation debugInformation = method.DebugInformation;
            if (debugInformation == null || !debugInformation.HasSequencePoints)
            {
                return (0, 0);
            }

            int startLine = 0;
            int endLine = 0;
            foreach (SequencePoint sequencePoint in debugInformation.SequencePoints)
            {
                if (sequencePoint.IsHidden)
                {
                    continue;
                }

                if (!SourcePausePointPathNormalizer.PathsReferToSameFile(
                    sequencePoint.Document.Url,
                    normalizedInputPath))
                {
                    continue;
                }

                if (startLine == 0 || sequencePoint.StartLine < startLine)
                {
                    startLine = sequencePoint.StartLine;
                }

                if (sequencePoint.EndLine > endLine)
                {
                    endLine = sequencePoint.EndLine;
                }
            }

            return (startLine, endLine);
        }

        // Why a file:line entry: the resolver test assembly cannot take a Cecil
        // ModuleDefinition dependency, but TakeAtMostTwo only runs on this walk.
        internal static IReadOnlyList<SourcePausePointNearbyCompiledMethod> FindNearbyCompiledMethodsInFile(
            string projectRelativeFilePath,
            int line)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRelativeFilePath), "projectRelativeFilePath must not be null or empty.");
            Debug.Assert(line > 0, "line must be a positive 1-based line number.");

            string normalizedInputPath = SourcePausePointPathNormalizer.ToForwardSlashes(projectRelativeFilePath);
            string rawAssemblyName = CompilationPipeline.GetAssemblyNameFromScriptPath(normalizedInputPath);
            if (string.IsNullOrEmpty(rawAssemblyName))
            {
                return Array.Empty<SourcePausePointNearbyCompiledMethod>();
            }

            string assemblyName = Path.GetFileNameWithoutExtension(rawAssemblyName);
            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            string dllPath = Path.Combine(
                projectRoot,
                SourcePausePointConstants.ScriptAssembliesRelativeDirectory,
                assemblyName + SourcePausePointConstants.CompiledAssemblyExtension);
            string pdbPath = Path.Combine(
                projectRoot,
                SourcePausePointConstants.ScriptAssembliesRelativeDirectory,
                assemblyName + SourcePausePointConstants.DebugSymbolsExtension);
            if (!File.Exists(dllPath) || !File.Exists(pdbPath))
            {
                return Array.Empty<SourcePausePointNearbyCompiledMethod>();
            }

            using FileStream dllStream = File.Open(dllPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using FileStream pdbStream = File.Open(pdbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            ReaderParameters readerParameters = new ReaderParameters
            {
                InMemory = true,
                ReadSymbols = true,
                SymbolReaderProvider = new PortablePdbReaderProvider(),
                SymbolStream = pdbStream
            };
            using AssemblyDefinition assemblyDefinition =
                AssemblyDefinition.ReadAssembly(dllStream, readerParameters);
            return FindNearbyCompiledMethods(assemblyDefinition.MainModule, normalizedInputPath, line);
        }

        // Why a separate walk from FindClosestSequencePointOnOrAfterLine: that search only
        // looks forward, so a miss past the last statement would otherwise leave the caller
        // with no compiled span to retarget against.
        internal static IReadOnlyList<SourcePausePointNearbyCompiledMethod> FindNearbyCompiledMethods(
            ModuleDefinition module,
            string normalizedInputPath,
            int line)
        {
            Debug.Assert(module != null, "module must not be null.");
            Debug.Assert(!string.IsNullOrEmpty(normalizedInputPath), "normalizedInputPath must not be empty.");
            Debug.Assert(line > 0, "line must be a positive 1-based line number.");

            List<SourcePausePointNearbyCompiledMethod> containing = new List<SourcePausePointNearbyCompiledMethod>();
            SourcePausePointNearbyCompiledMethod nearestBefore = null;
            SourcePausePointNearbyCompiledMethod nearestAfter = null;

            foreach (MethodDefinition method in EnumerateMethodsInModule(module))
            {
                SourcePausePointNearbyCompiledMethod candidate =
                    TryCreateNearbyCompiledMethod(method, normalizedInputPath);
                if (candidate == null)
                {
                    continue;
                }

                if (candidate.StartLine <= line && line <= candidate.EndLine)
                {
                    containing.Add(candidate);
                    continue;
                }

                if (candidate.EndLine < line
                    && (nearestBefore == null || candidate.EndLine > nearestBefore.EndLine))
                {
                    nearestBefore = candidate;
                }

                if (candidate.StartLine > line
                    && (nearestAfter == null || candidate.StartLine < nearestAfter.StartLine))
                {
                    nearestAfter = candidate;
                }
            }

            if (containing.Count > 0)
            {
                return TakeAtMostTwo(containing);
            }

            List<SourcePausePointNearbyCompiledMethod> nearby = new List<SourcePausePointNearbyCompiledMethod>();
            if (nearestBefore != null)
            {
                nearby.Add(nearestBefore);
            }

            if (nearestAfter != null)
            {
                nearby.Add(nearestAfter);
            }

            return nearby;
        }

        private static SourcePausePointNearbyCompiledMethod TryCreateNearbyCompiledMethod(
            MethodDefinition method,
            string normalizedInputPath)
        {
            if (!method.HasBody)
            {
                return null;
            }

            (int startLine, int endLine) = CollectCompiledMethodSpan(method, normalizedInputPath);
            if (startLine <= 0 || endLine <= 0)
            {
                return null;
            }

            string typeName = method.DeclaringType != null ? method.DeclaringType.Name : "?";
            return new SourcePausePointNearbyCompiledMethod(
                typeName + "." + method.Name,
                startLine,
                endLine);
        }

        private static IReadOnlyList<SourcePausePointNearbyCompiledMethod> TakeAtMostTwo(
            List<SourcePausePointNearbyCompiledMethod> methods)
        {
            if (methods.Count <= 2)
            {
                return methods;
            }

            return new[] { methods[0], methods[1] };
        }

        // Shared with SourcePausePointShimResolver so shim-assembly sequence-point walks use the
        // same nested-type enumeration and capturable-local / parameter exclusion rules.
        internal static IEnumerable<MethodDefinition> EnumerateMethodsInModule(ModuleDefinition module)
        {
            foreach (TypeDefinition type in module.Types)
            {
                foreach (MethodDefinition method in EnumerateMethodsInType(type))
                {
                    yield return method;
                }
            }
        }

        private static IEnumerable<MethodDefinition> EnumerateMethodsInType(TypeDefinition type)
        {
            foreach (MethodDefinition method in type.Methods)
            {
                yield return method;
            }

            foreach (TypeDefinition nestedType in type.NestedTypes)
            {
                foreach (MethodDefinition method in EnumerateMethodsInType(nestedType))
                {
                    yield return method;
                }
            }
        }

        internal static int FindInstructionIndex(Collection<Instruction> instructions, int offset)
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
