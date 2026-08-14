using System;
using System.Collections.Generic;
using System.IO;

using Mono.Cecil;
using Mono.Cecil.Cil;

using UnityEditor.Compilation;
using UnityEngine;

using UnityCompilationAssembly = UnityEditor.Compilation.Assembly;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Enumerates compiled call sites that target specified methods across project assemblies.
    /// </summary>
    internal static class HotReloadCallSiteScanner
    {
        /// <summary>
        /// Identity of a compiled method to search for (assembly + type + name + parameter types).
        /// </summary>
        public readonly struct CompiledMethodIdentity
        {
            public readonly string AssemblyName;
            public readonly string TypeMetadataName;
            public readonly string MethodName;
            public readonly string[] ParameterTypeFullNames;

            public CompiledMethodIdentity(
                string assemblyName,
                string typeMetadataName,
                string methodName,
                string[] parameterTypeFullNames)
            {
                Debug.Assert(!string.IsNullOrEmpty(assemblyName), "assemblyName must not be null or empty.");
                Debug.Assert(!string.IsNullOrEmpty(typeMetadataName), "typeMetadataName must not be null or empty.");
                Debug.Assert(!string.IsNullOrEmpty(methodName), "methodName must not be null or empty.");
                Debug.Assert(parameterTypeFullNames != null, "parameterTypeFullNames must not be null.");

                AssemblyName = assemblyName;
                TypeMetadataName = typeMetadataName;
                MethodName = methodName;
                ParameterTypeFullNames = parameterTypeFullNames;
            }
        }

        /// <summary>
        /// One compiled instruction that references a target method, reported under its logical owner.
        /// </summary>
        public sealed class CallSiteHit
        {
            public string CallerAssemblyName;
            public string CallerTypeMetadataName;
            public string CallerMethodName;
            public string[] CallerParameterTypeFullNames;
            public string CallerMethodKey;
        }

        /// <summary>
        /// Finds compiled call / ldftn sites that reference any of <paramref name="targets"/>.
        /// </summary>
        public static List<CallSiteHit> FindCallSites(string projectRoot, CompiledMethodIdentity[] targets)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty.");
            Debug.Assert(targets != null, "targets must not be null.");

            List<CallSiteHit> hits = new List<CallSiteHit>();
            if (targets.Length == 0)
            {
                return hits;
            }

            HashSet<string> scanAssemblyNames = CollectScanAssemblyNames(targets);
            foreach (string assemblyName in scanAssemblyNames)
            {
                string dllPath = Path.Combine(
                    projectRoot,
                    HotReloadConstants.ScriptAssembliesRelativeDirectory,
                    assemblyName + HotReloadConstants.CompiledAssemblyExtension);

                // Why skip (not assert): an assembly that has not been written to ScriptAssemblies
                // cannot contain call sites, so it cannot be a caller. Missing here is "not compiled
                // yet", not a broken invariant.
                if (!File.Exists(dllPath))
                {
                    continue;
                }

                CollectHitsFromAssembly(assemblyName, dllPath, targets, hits);
            }

            return hits;
        }

        private static HashSet<string> CollectScanAssemblyNames(CompiledMethodIdentity[] targets)
        {
            HashSet<string> targetAssemblyNames = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> targetDllFileNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (CompiledMethodIdentity target in targets)
            {
                targetAssemblyNames.Add(target.AssemblyName);
                targetDllFileNames.Add(target.AssemblyName + HotReloadConstants.CompiledAssemblyExtension);
            }

            HashSet<string> scanNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (UnityCompilationAssembly assembly in CompilationPipeline.GetAssemblies())
            {
                if (targetAssemblyNames.Contains(assembly.name)
                    || ReferencesAnyTargetDll(assembly, targetDllFileNames))
                {
                    scanNames.Add(assembly.name);
                }
            }

            return scanNames;
        }

        private static bool ReferencesAnyTargetDll(
            UnityCompilationAssembly assembly,
            HashSet<string> targetDllFileNames)
        {
            if (assembly.allReferences == null)
            {
                return false;
            }

            foreach (string reference in assembly.allReferences)
            {
                if (string.IsNullOrEmpty(reference))
                {
                    continue;
                }

                if (targetDllFileNames.Contains(Path.GetFileName(reference)))
                {
                    return true;
                }
            }

            return false;
        }

        private static void CollectHitsFromAssembly(
            string assemblyName,
            string dllPath,
            CompiledMethodIdentity[] targets,
            List<CallSiteHit> hits)
        {
            // InMemory + no resolver: operand FullName comparison does not require type resolution.
            ReaderParameters readerParameters = new ReaderParameters { InMemory = true };
            using AssemblyDefinition assemblyDefinition = AssemblyDefinition.ReadAssembly(dllPath, readerParameters);
            Dictionary<string, MethodDefinition> logicalOwners =
                BuildLogicalOwnerIndex(assemblyDefinition.MainModule);

            foreach (TypeDefinition type in assemblyDefinition.MainModule.GetTypes())
            {
                foreach (MethodDefinition method in type.Methods)
                {
                    CollectHitsFromMethod(assemblyName, method, targets, logicalOwners, hits);
                }
            }
        }

        private static Dictionary<string, MethodDefinition> BuildLogicalOwnerIndex(ModuleDefinition module)
        {
            Dictionary<string, MethodDefinition> index = new Dictionary<string, MethodDefinition>();
            foreach (TypeDefinition type in module.GetTypes())
            {
                foreach (MethodDefinition method in type.Methods)
                {
                    TryIndexStateMachineOwner(method, index);
                }
            }

            return index;
        }

        private static void TryIndexStateMachineOwner(
            MethodDefinition method,
            Dictionary<string, MethodDefinition> index)
        {
            if (!method.HasCustomAttributes)
            {
                return;
            }

            foreach (CustomAttribute attribute in method.CustomAttributes)
            {
                string attributeName = attribute.AttributeType.Name;
                if (attributeName != HotReloadConstants.AsyncStateMachineAttributeTypeName
                    && attributeName != HotReloadConstants.IteratorStateMachineAttributeTypeName)
                {
                    continue;
                }

                if (!attribute.HasConstructorArguments || attribute.ConstructorArguments.Count == 0)
                {
                    continue;
                }

                TypeReference stateMachineType = attribute.ConstructorArguments[0].Value as TypeReference;
                if (stateMachineType == null)
                {
                    continue;
                }

                index[stateMachineType.FullName] = method;
            }
        }

        private static void CollectHitsFromMethod(
            string assemblyName,
            MethodDefinition method,
            CompiledMethodIdentity[] targets,
            Dictionary<string, MethodDefinition> logicalOwners,
            List<CallSiteHit> hits)
        {
            if (!method.HasBody)
            {
                return;
            }

            foreach (Instruction instruction in method.Body.Instructions)
            {
                if (!IsCallSiteOpcode(instruction.OpCode))
                {
                    continue;
                }

                MethodReference operand = instruction.Operand as MethodReference;
                if (operand == null)
                {
                    continue;
                }

                (bool matched, CompiledMethodIdentity target) = FindMatchingTarget(operand, targets);
                if (!matched)
                {
                    continue;
                }

                MethodDefinition reportedCaller = ResolveReportedCaller(method, logicalOwners);

                // Why resolve first: async/iterator self-recursion lives in MoveNext. Matching
                // the physical caller would miss it, then reporting the logical owner would look
                // like an external caller of the old method.
                if (IsSelfCall(assemblyName, reportedCaller, target))
                {
                    continue;
                }

                hits.Add(CreateHit(assemblyName, reportedCaller));
            }
        }

        private static bool IsCallSiteOpcode(OpCode opCode)
        {
            return opCode == OpCodes.Call
                || opCode == OpCodes.Callvirt
                || opCode == OpCodes.Ldftn
                || opCode == OpCodes.Ldvirtftn;
        }

        private static (bool matched, CompiledMethodIdentity target) FindMatchingTarget(
            MethodReference methodReference,
            CompiledMethodIdentity[] targets)
        {
            foreach (CompiledMethodIdentity target in targets)
            {
                if (MatchesIdentity(
                        methodReference,
                        target.TypeMetadataName,
                        target.MethodName,
                        target.ParameterTypeFullNames))
                {
                    return (true, target);
                }
            }

            return (false, default);
        }

        private static bool IsSelfCall(
            string callerAssemblyName,
            MethodDefinition caller,
            CompiledMethodIdentity target)
        {
            if (callerAssemblyName != target.AssemblyName)
            {
                return false;
            }

            return MatchesIdentity(
                caller,
                target.TypeMetadataName,
                target.MethodName,
                target.ParameterTypeFullNames);
        }

        private static bool MatchesIdentity(
            MethodReference methodReference,
            string typeMetadataName,
            string methodName,
            string[] parameterTypeFullNames)
        {
            MethodReference openMethod = methodReference.GetElementMethod();
            if (openMethod.DeclaringType == null)
            {
                return false;
            }

            // Why not normalize '/' → '+': Cecil FullName and worker typeMetadataName both use
            // '/' for nested types, and BuildMethodKey keeps that form. Converting here would
            // desync CallerMethodKey from the orchestrator key space on nested types.
            if (ToOpenDeclaringTypeFullName(openMethod.DeclaringType) != typeMetadataName)
            {
                return false;
            }

            if (openMethod.Name != methodName)
            {
                return false;
            }

            return ParametersMatch(openMethod, parameterTypeFullNames);
        }

        private static string ToOpenDeclaringTypeFullName(TypeReference declaringType)
        {
            GenericInstanceType genericInstance = declaringType as GenericInstanceType;
            if (genericInstance != null)
            {
                return genericInstance.GetElementType().FullName;
            }

            return declaringType.FullName;
        }

        private static bool ParametersMatch(MethodReference methodReference, string[] parameterTypeFullNames)
        {
            if (methodReference.Parameters.Count != parameterTypeFullNames.Length)
            {
                return false;
            }

            for (int index = 0; index < parameterTypeFullNames.Length; index++)
            {
                TypeReference parameterType = methodReference.Parameters[index].ParameterType;
                if (parameterType.ContainsGenericParameter)
                {
                    // Why treat as match: a type-argument-dependent parameter cannot be compared
                    // to the compiled identity string with certainty. Missing the site would
                    // fail-open the signature-change gate.
                    continue;
                }

                if (parameterType.FullName != parameterTypeFullNames[index])
                {
                    return false;
                }
            }

            return true;
        }

        private static MethodDefinition ResolveReportedCaller(
            MethodDefinition caller,
            Dictionary<string, MethodDefinition> logicalOwners)
        {
            TypeDefinition declaringType = caller.DeclaringType;
            if (!IsCompilerGeneratedType(declaringType))
            {
                // Why leave local functions as mangled names (<M>g__f|…): they are methods on
                // the user type, so the state-machine index does not apply. Cover checks then
                // fail closed (over-Skip) instead of treating an unknown local function as patched.
                return caller;
            }

            // Why keep the compiler-generated identity on a miss: closures such as
            // <>c__DisplayClass have no Async/Iterator attribute link. Cover checks then fail
            // closed (uncovered) instead of treating an unknown owner as already patched.
            if (logicalOwners.TryGetValue(declaringType.FullName, out MethodDefinition logicalOwner))
            {
                return logicalOwner;
            }

            return caller;
        }

        private static bool IsCompilerGeneratedType(TypeDefinition type)
        {
            if (type.Name.IndexOf('<') >= 0)
            {
                return true;
            }

            if (!type.HasCustomAttributes)
            {
                return false;
            }

            foreach (CustomAttribute attribute in type.CustomAttributes)
            {
                if (attribute.AttributeType.Name == HotReloadConstants.CompilerGeneratedAttributeTypeName)
                {
                    return true;
                }
            }

            return false;
        }

        private static CallSiteHit CreateHit(string assemblyName, MethodDefinition caller)
        {
            string[] parameterTypeFullNames = new string[caller.Parameters.Count];
            for (int index = 0; index < caller.Parameters.Count; index++)
            {
                parameterTypeFullNames[index] = caller.Parameters[index].ParameterType.FullName;
            }

            string typeMetadataName = caller.DeclaringType.FullName;

            // Keep in sync with TransformWorkerProgram.BuildMethodKey (out-of-process worker)
            // and HotReloadOrchestrator.BuildMethodKey (Unity package side).
            return new CallSiteHit
            {
                CallerAssemblyName = assemblyName,
                CallerTypeMetadataName = typeMetadataName,
                CallerMethodName = caller.Name,
                CallerParameterTypeFullNames = parameterTypeFullNames,
                CallerMethodKey = typeMetadataName + "::" + caller.Name + "("
                    + string.Join(",", parameterTypeFullNames) + ")"
            };
        }
    }
}
