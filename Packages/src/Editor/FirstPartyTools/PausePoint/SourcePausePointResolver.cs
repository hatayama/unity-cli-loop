using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;

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
            ModuleDefinition module, string normalizedInputPath, int line)
        {
            MethodDefinition bestMethod = null;
            SequencePoint bestSequencePoint = null;

            foreach (MethodDefinition method in EnumerateMethodsInModule(module))
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

        internal static List<SourcePausePointLocalVariable> CollectCapturableLocals(
            MethodDefinition method,
            int offset)
        {
            List<SourcePausePointLocalVariable> results = new List<SourcePausePointLocalVariable>();
            ScopeDebugInformation rootScope = method.DebugInformation.Scope;
            if (rootScope != null)
            {
                CollectInScopeVariables(rootScope, offset, method.Body.Variables, results);
            }

            return results;
        }

        /// <summary>
        /// Collects every named capturable local in the method's debug scopes, ignoring IL offset.
        /// Used when a shim sequence point lands on Ret (or similar) after local scopes close.
        /// Keep-first dedupe by slot and by name avoids duplicate capture entries when scopes
        /// reuse slots or nest identically named locals.
        /// </summary>
        internal static List<SourcePausePointLocalVariable> CollectAllCapturableLocals(
            MethodDefinition method)
        {
            List<SourcePausePointLocalVariable> results = new List<SourcePausePointLocalVariable>();
            ScopeDebugInformation rootScope = method.DebugInformation.Scope;
            if (rootScope == null)
            {
                return results;
            }

            HashSet<int> seenSlots = new HashSet<int>();
            HashSet<string> seenNames = new HashSet<string>(StringComparer.Ordinal);
            CollectAllScopeVariables(rootScope, method.Body.Variables, results, seenSlots, seenNames);
            return results;
        }

        private static void CollectAllScopeVariables(
            ScopeDebugInformation scope,
            Collection<VariableDefinition> methodVariables,
            List<SourcePausePointLocalVariable> results,
            HashSet<int> seenSlots,
            HashSet<string> seenNames)
        {
            if (scope.HasVariables)
            {
                foreach (VariableDebugInformation variableDebugInformation in scope.Variables)
                {
                    int beforeCount = results.Count;
                    AppendLocalIfCapturable(variableDebugInformation, methodVariables, results);
                    if (results.Count == beforeCount)
                    {
                        continue;
                    }

                    SourcePausePointLocalVariable added = results[results.Count - 1];
                    if (seenSlots.Contains(added.SlotIndex) || seenNames.Contains(added.Name))
                    {
                        results.RemoveAt(results.Count - 1);
                    }
                    else
                    {
                        seenSlots.Add(added.SlotIndex);
                        seenNames.Add(added.Name);
                    }
                }
            }

            if (!scope.HasScopes)
            {
                return;
            }

            foreach (ScopeDebugInformation childScope in scope.Scopes)
            {
                CollectAllScopeVariables(childScope, methodVariables, results, seenSlots, seenNames);
            }
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

            results.Add(new SourcePausePointLocalVariable(name, slotIndex, variableType.FullName, variableType.IsValueType));
        }

        private static bool IsCaptureExcluded(TypeReference type)
        {
            // byref locals/parameters (ref, out, in) and pointers cannot be boxed; ref structs
            // (Span<T>, and any user-defined "ref struct") cannot be boxed either.
            return type.IsByReference || type.IsPointer || IsRefStructType(type);
        }

        private static bool IsRefStructType(TypeReference type)
        {
            if (IsKnownFrameworkRefStructType(type))
            {
                return true;
            }

            // A generic instance (e.g. MyRefStruct<int>) must be unwrapped to its open generic
            // definition before the TypeDefinition check below, the same way the framework check
            // above unwraps Span<T>/ReadOnlySpan<T>.
            TypeReference elementType = type.IsGenericInstance ? type.GetElementType() : type;

            // Only inspect user-defined ref structs when the reference already points directly at
            // a TypeDefinition, i.e. it is declared in the very assembly being read. Resolving an
            // external TypeReference (e.g. any corlib value type such as System.Int32) requires
            // Mono.Cecil's assembly resolver to locate that assembly, which is not reliably
            // resolvable against the Unity Editor's Mono/.NET layout and would throw for ordinary
            // primitives that are not ref structs at all.
            if (!(elementType is TypeDefinition typeDefinition) || !typeDefinition.IsValueType || !typeDefinition.HasCustomAttributes)
            {
                return false;
            }

            foreach (CustomAttribute attribute in typeDefinition.CustomAttributes)
            {
                if (attribute.AttributeType.FullName == SourcePausePointConstants.IsByRefLikeAttributeFullName)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsKnownFrameworkRefStructType(TypeReference type)
        {
            // Span<T>/ReadOnlySpan<T> are ref structs defined in corlib; checking their FullName
            // avoids forcing assembly resolution for a framework type on the hot path.
            TypeReference elementType = type.IsGenericInstance ? type.GetElementType() : type;
            return elementType.FullName == "System.Span`1" || elementType.FullName == "System.ReadOnlySpan`1";
        }

        internal static List<SourcePausePointParameter> CollectParameters(MethodDefinition method)
        {
            List<SourcePausePointParameter> parameters = new List<SourcePausePointParameter>();
            foreach (ParameterDefinition parameter in method.Parameters)
            {
                if (IsCaptureExcluded(parameter.ParameterType))
                {
                    continue;
                }

                parameters.Add(new SourcePausePointParameter(
                    parameter.Name, parameter.Index, parameter.ParameterType.FullName, parameter.ParameterType.IsValueType));
            }

            return parameters;
        }

        /// <summary>
        /// Collects capturable parameters from a reflection MethodBase (transplant chain-join
        /// uses the original method; shim-direct uses the resolved shim-side method).
        /// </summary>
        internal static List<SourcePausePointParameter> CollectParametersFromReflection(
            MethodBase method,
            bool skipFirstParameter)
        {
            Debug.Assert(method != null, "method must not be null.");

            // Fully qualify: ToolContracts also defines ParameterInfo (tool schema DTO).
            System.Reflection.ParameterInfo[] runtimeParameters = method.GetParameters();
            List<SourcePausePointParameter> parameters = new List<SourcePausePointParameter>();
            int startIndex = skipFirstParameter ? 1 : 0;
            for (int index = startIndex; index < runtimeParameters.Length; index++)
            {
                System.Reflection.ParameterInfo parameter = runtimeParameters[index];
                Type parameterType = parameter.ParameterType;
                if (IsCaptureExcludedReflection(parameterType))
                {
                    continue;
                }

                // Keep GetParameters() position so LoadArgument hits the right slot even when
                // earlier parameters were excluded from capture (same as Cecil Parameter.Index).
                parameters.Add(
                    new SourcePausePointParameter(
                        parameter.Name,
                        index,
                        parameterType.FullName,
                        parameterType.IsValueType));
            }

            return parameters;
        }

        private static bool IsCaptureExcludedReflection(Type type)
        {
            return type.IsByRef || type.IsPointer || IsByRefLikeTypeReflection(type);
        }

        private static bool IsByRefLikeTypeReflection(Type type)
        {
            Type elementType = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
            if (elementType.FullName == "System.Span`1" || elementType.FullName == "System.ReadOnlySpan`1")
            {
                return true;
            }

            foreach (object attribute in type.GetCustomAttributes(inherit: false))
            {
                if (attribute.GetType().FullName == SourcePausePointConstants.IsByRefLikeAttributeFullName)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
