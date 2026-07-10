using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

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
        public static SourcePausePointResolveResult Resolve(string projectRelativeFilePath, int line)
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

            return ResolveFromCompiledAssembly(assemblyName, dllPath, pdbPath, normalizedInputPath, projectRelativeFilePath, line);
        }

        private static SourcePausePointResolveResult ResolveFromCompiledAssembly(
            string assemblyName,
            string dllPath,
            string pdbPath,
            string normalizedInputPath,
            string originalInputPath,
            int line)
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
                assemblyDefinition.MainModule, normalizedInputPath, line);

            if (method == null)
            {
                return SourcePausePointResolveResult.Failure(
                    SourcePausePointResolveFailureReason.NoSequencePointOnOrAfterLine,
                    $"No sequence point found on or after line {line} in '{originalInputPath}'.");
            }

            int instructionIndex = FindInstructionIndex(method.Body.Instructions, sequencePoint.Offset);
            Debug.Assert(instructionIndex >= 0, "A sequence point's offset must correspond to an instruction in the same method body.");

            List<SourcePausePointLocalVariable> locals = CollectCapturableLocals(method, sequencePoint.Offset);
            List<SourcePausePointParameter> parameters = CollectParameters(method);

            SourcePausePointResolution resolution = new SourcePausePointResolution(
                assemblyName,
                method.MetadataToken.ToInt32(),
                method.FullName,
                method.IsStatic,
                instructionIndex,
                sequencePoint.Offset,
                sequencePoint.StartLine,
                locals,
                parameters);

            return SourcePausePointResolveResult.SuccessResult(resolution);
        }

        private static (MethodDefinition method, SequencePoint sequencePoint) FindClosestSequencePointOnOrAfterLine(
            ModuleDefinition module, string normalizedInputPath, int line)
        {
            MethodDefinition bestMethod = null;
            SequencePoint bestSequencePoint = null;

            foreach (MethodDefinition method in EnumerateMethods(module))
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

        private static IEnumerable<MethodDefinition> EnumerateMethods(ModuleDefinition module)
        {
            foreach (TypeDefinition type in module.Types)
            {
                foreach (MethodDefinition method in EnumerateMethods(type))
                {
                    yield return method;
                }
            }
        }

        private static IEnumerable<MethodDefinition> EnumerateMethods(TypeDefinition type)
        {
            foreach (MethodDefinition method in type.Methods)
            {
                yield return method;
            }

            foreach (TypeDefinition nestedType in type.NestedTypes)
            {
                foreach (MethodDefinition method in EnumerateMethods(nestedType))
                {
                    yield return method;
                }
            }
        }

        private static int FindInstructionIndex(Collection<Instruction> instructions, int offset)
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

        private static List<SourcePausePointLocalVariable> CollectCapturableLocals(MethodDefinition method, int offset)
        {
            List<SourcePausePointLocalVariable> results = new List<SourcePausePointLocalVariable>();
            ScopeDebugInformation rootScope = method.DebugInformation.Scope;
            if (rootScope != null)
            {
                CollectInScopeVariables(rootScope, offset, method.Body.Variables, results);
            }

            return results;
        }

        private static void CollectInScopeVariables(
            ScopeDebugInformation scope,
            int offset,
            Collection<VariableDefinition> methodVariables,
            List<SourcePausePointLocalVariable> results)
        {
            if (!IsOffsetWithinScope(scope, offset))
            {
                return;
            }

            if (scope.HasVariables)
            {
                foreach (VariableDebugInformation variableDebugInformation in scope.Variables)
                {
                    AppendLocalIfCapturable(variableDebugInformation, methodVariables, results);
                }
            }

            if (scope.HasScopes)
            {
                foreach (ScopeDebugInformation childScope in scope.Scopes)
                {
                    CollectInScopeVariables(childScope, offset, methodVariables, results);
                }
            }
        }

        private static bool IsOffsetWithinScope(ScopeDebugInformation scope, int offset)
        {
            if (offset < scope.Start.Offset)
            {
                return false;
            }

            return scope.End.IsEndOfMethod || offset < scope.End.Offset;
        }

        private static void AppendLocalIfCapturable(
            VariableDebugInformation variableDebugInformation,
            Collection<VariableDefinition> methodVariables,
            List<SourcePausePointLocalVariable> results)
        {
            string name = variableDebugInformation.Name;
            if (string.IsNullOrEmpty(name) || name.StartsWith("<", StringComparison.Ordinal))
            {
                // Unnamed or compiler-generated hoisted locals (e.g. "<>u__1") carry no source meaning to capture.
                return;
            }

            int slotIndex = variableDebugInformation.Index;
            if (slotIndex < 0 || slotIndex >= methodVariables.Count)
            {
                return;
            }

            TypeReference variableType = methodVariables[slotIndex].VariableType;
            if (IsCaptureExcluded(variableType))
            {
                return;
            }

            results.Add(new SourcePausePointLocalVariable(name, slotIndex, variableType.FullName));
        }

        private static bool IsCaptureExcluded(TypeReference variableType)
        {
            // byref locals and pointers cannot be boxed; ref structs (Span<T> etc.) cannot be boxed either.
            return variableType.IsByReference || variableType.IsPointer || IsKnownRefStructType(variableType);
        }

        private static bool IsKnownRefStructType(TypeReference variableType)
        {
            TypeReference elementType = variableType.IsGenericInstance ? variableType.GetElementType() : variableType;
            return elementType.FullName == "System.Span`1" || elementType.FullName == "System.ReadOnlySpan`1";
        }

        private static List<SourcePausePointParameter> CollectParameters(MethodDefinition method)
        {
            List<SourcePausePointParameter> parameters = new List<SourcePausePointParameter>();
            foreach (ParameterDefinition parameter in method.Parameters)
            {
                parameters.Add(new SourcePausePointParameter(parameter.Name, parameter.Index, parameter.ParameterType.FullName));
            }

            return parameters;
        }
    }
}
