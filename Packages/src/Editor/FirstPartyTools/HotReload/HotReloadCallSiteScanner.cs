using System;
using System.Collections.Generic;
using System.IO;

using Mono.Cecil;

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
        /// Compiled call-site findings and assemblies whose absence makes the findings incomplete.
        /// </summary>
        internal sealed class HotReloadCallSiteScanResult
        {
            public List<CallSiteHit> Hits;
            public List<string> MissingScanAssemblyNames;

            public HotReloadCallSiteScanResult(
                List<CallSiteHit> hits,
                List<string> missingScanAssemblyNames)
            {
                Hits = hits;
                MissingScanAssemblyNames = missingScanAssemblyNames;
            }
        }

        /// <summary>
        /// Identity of a compiled method to search for (assembly + type + name + arity + parameter types).
        /// </summary>
        public readonly struct CompiledMethodIdentity
        {
            public readonly string AssemblyName;
            public readonly string TypeMetadataName;
            public readonly string MethodName;
            public readonly string[] ParameterTypeFullNames;
            public readonly int GenericArity;

            public CompiledMethodIdentity(
                string assemblyName,
                string typeMetadataName,
                string methodName,
                string[] parameterTypeFullNames,
                int genericArity)
            {
                Debug.Assert(!string.IsNullOrEmpty(assemblyName), "assemblyName must not be null or empty.");
                Debug.Assert(!string.IsNullOrEmpty(typeMetadataName), "typeMetadataName must not be null or empty.");
                Debug.Assert(!string.IsNullOrEmpty(methodName), "methodName must not be null or empty.");
                Debug.Assert(parameterTypeFullNames != null, "parameterTypeFullNames must not be null.");
                Debug.Assert(genericArity >= 0, "genericArity must not be negative.");

                AssemblyName = assemblyName;
                TypeMetadataName = typeMetadataName;
                MethodName = methodName;
                ParameterTypeFullNames = parameterTypeFullNames;
                GenericArity = genericArity;
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
            public int CallerGenericArity;
            public string CallerMethodKey;
            public string TargetMethodKey;
            public bool IsFunctionPointerLoad;
        }

        /// <summary>
        /// Finds compiled call / ldftn sites that reference any of <paramref name="targets"/>.
        /// </summary>
        public static HotReloadCallSiteScanResult FindCallSites(
            string projectRoot,
            CompiledMethodIdentity[] targets)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty.");
            Debug.Assert(targets != null, "targets must not be null.");

            List<CallSiteHit> hits = new List<CallSiteHit>();
            List<string> missingScanAssemblyNames = new List<string>();
            if (targets.Length == 0)
            {
                return new HotReloadCallSiteScanResult(hits, missingScanAssemblyNames);
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
                    missingScanAssemblyNames.Add(assemblyName);
                    continue;
                }

                CollectHitsFromAssembly(assemblyName, dllPath, targets, hits);
            }

            return new HotReloadCallSiteScanResult(hits, missingScanAssemblyNames);
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
            foreach (string targetAssemblyName in targetAssemblyNames)
            {
                scanNames.Add(targetAssemblyName);
            }
            foreach (KeyValuePair<string, string[]> assembly in GetReferencedDllFileNamesByAssembly())
            {
                if (targetAssemblyNames.Contains(assembly.Key)
                    || ReferencesAnyTargetDll(assembly.Value, targetDllFileNames))
                {
                    scanNames.Add(assembly.Key);
                }
            }

            return scanNames;
        }

        // Why cache per domain: CompilationPipeline.GetAssemblies() costs ~40 ms on a project with
        // ~100 assemblies, and the reference graph can only change through an asmdef or package
        // change, which recompiles and reloads the domain (resetting this field).
        private static Dictionary<string, string[]> _referencedDllFileNamesByAssembly;

        private static Dictionary<string, string[]> GetReferencedDllFileNamesByAssembly()
        {
            if (_referencedDllFileNamesByAssembly != null)
            {
                return _referencedDllFileNamesByAssembly;
            }

            Dictionary<string, string[]> graph = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (UnityCompilationAssembly assembly in CompilationPipeline.GetAssemblies())
            {
                graph[assembly.name] = CollectReferenceFileNames(assembly.allReferences);
            }

            _referencedDllFileNamesByAssembly = graph;
            return graph;
        }

        private static string[] CollectReferenceFileNames(string[] allReferences)
        {
            if (allReferences == null)
            {
                return Array.Empty<string>();
            }

            List<string> fileNames = new List<string>(allReferences.Length);
            foreach (string reference in allReferences)
            {
                if (string.IsNullOrEmpty(reference))
                {
                    continue;
                }

                fileNames.Add(Path.GetFileName(reference));
            }

            return fileNames.ToArray();
        }

        private static bool ReferencesAnyTargetDll(
            string[] referencedDllFileNames,
            HashSet<string> targetDllFileNames)
        {
            foreach (string fileName in referencedDllFileNames)
            {
                if (targetDllFileNames.Contains(fileName))
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
            // Why cache: the dll only changes on a compile, which also reloads the domain, so
            // across the runs in between the Cecil read and the instruction walk are pure repeat work.
            HotReloadCompiledCallSiteCache.Entry compiled = HotReloadCompiledCallSiteCache.Shared.GetOrLoad(dllPath);
            foreach (HotReloadCompiledCallSiteCache.CompiledCallSite callSite in compiled.CallSites)
            {
                CollectHitFromCallSite(assemblyName, compiled, callSite, targets, hits);
            }
        }

        private static void CollectHitFromCallSite(
            string assemblyName,
            HotReloadCompiledCallSiteCache.Entry compiled,
            HotReloadCompiledCallSiteCache.CompiledCallSite callSite,
            CompiledMethodIdentity[] targets,
            List<CallSiteHit> hits)
        {
            (bool matched, CompiledMethodIdentity target) = FindMatchingTarget(
                callSite.Operand,
                assemblyName,
                compiled.Module,
                targets);
            if (!matched)
            {
                return;
            }

            MethodDefinition reportedCaller = ResolveReportedCaller(callSite.Caller, compiled.LogicalOwners);

            // Why resolve first: async/iterator self-recursion lives in MoveNext. Matching
            // the physical caller would miss it, then reporting the logical owner would look
            // like an external caller of the old method.
            if (IsSelfCall(assemblyName, compiled.Module, reportedCaller, target))
            {
                return;
            }

            hits.Add(
                CreateHit(
                    assemblyName,
                    reportedCaller,
                    target,
                    callSite.IsFunctionPointerLoad));
        }

        private static (bool matched, CompiledMethodIdentity target) FindMatchingTarget(
            MethodReference methodReference,
            string scannedAssemblyName,
            ModuleDefinition scannedModule,
            CompiledMethodIdentity[] targets)
        {
            foreach (CompiledMethodIdentity target in targets)
            {
                if (MatchesIdentity(
                        methodReference,
                        target,
                        scannedAssemblyName,
                        scannedModule))
                {
                    return (true, target);
                }
            }

            return (false, default);
        }

        private static bool IsSelfCall(
            string callerAssemblyName,
            ModuleDefinition callerModule,
            MethodDefinition caller,
            CompiledMethodIdentity target)
        {
            if (callerAssemblyName != target.AssemblyName)
            {
                return false;
            }

            return MatchesIdentity(caller, target, callerAssemblyName, callerModule);
        }

        private static bool MatchesIdentity(
            MethodReference methodReference,
            CompiledMethodIdentity target,
            string scannedAssemblyName,
            ModuleDefinition scannedModule)
        {
            MethodReference openMethod = methodReference.GetElementMethod();
            if (openMethod.DeclaringType == null)
            {
                return false;
            }

            TypeReference openDeclaringType = GetOpenDeclaringType(openMethod.DeclaringType);
            if (!DeclaringTypeScopeMatchesTarget(
                    openDeclaringType,
                    target.AssemblyName,
                    scannedAssemblyName,
                    scannedModule))
            {
                return false;
            }

            // Why not normalize '/' → '+': Cecil FullName and worker typeMetadataName both use
            // '/' for nested types, and BuildMethodKey keeps that form. Converting here would
            // desync CallerMethodKey from the orchestrator key space on nested types.
            if (openDeclaringType.FullName != target.TypeMetadataName)
            {
                return false;
            }

            if (openMethod.Name != target.MethodName)
            {
                return false;
            }

            // Why compare arity: Caller(int) and Caller<T>(int) share name and parameters.
            // Treating them as the same identity fail-opens the signature-change gate.
            if (openMethod.GenericParameters.Count != target.GenericArity)
            {
                return false;
            }

            return ParametersMatch(openMethod, target.ParameterTypeFullNames);
        }

        private static TypeReference GetOpenDeclaringType(TypeReference declaringType)
        {
            GenericInstanceType genericInstance = declaringType as GenericInstanceType;
            if (genericInstance != null)
            {
                return genericInstance.GetElementType();
            }

            return declaringType;
        }

        private static bool DeclaringTypeScopeMatchesTarget(
            TypeReference declaringType,
            string targetAssemblyName,
            string scannedAssemblyName,
            ModuleDefinition scannedModule)
        {
            AssemblyNameReference assemblyReference = declaringType.Scope as AssemblyNameReference;
            if (assemblyReference != null)
            {
                return assemblyReference.Name == targetAssemblyName;
            }

            // A non-assembly scope could name a same-shaped type from another module. Treating it
            // as the target would overstate caller coverage and lifecycle certainty.
            if (!ReferenceEquals(declaringType.Scope, scannedModule))
            {
                return false;
            }

            return scannedAssemblyName == targetAssemblyName;
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

        private static CallSiteHit CreateHit(
            string assemblyName,
            MethodDefinition caller,
            CompiledMethodIdentity target,
            bool isFunctionPointerLoad)
        {
            string[] parameterTypeFullNames = new string[caller.Parameters.Count];
            for (int index = 0; index < caller.Parameters.Count; index++)
            {
                parameterTypeFullNames[index] = caller.Parameters[index].ParameterType.FullName;
            }

            string typeMetadataName = caller.DeclaringType.FullName;

            return new CallSiteHit
            {
                CallerAssemblyName = assemblyName,
                CallerTypeMetadataName = typeMetadataName,
                CallerMethodName = caller.Name,
                CallerParameterTypeFullNames = parameterTypeFullNames,
                CallerGenericArity = caller.GenericParameters.Count,
                CallerMethodKey = HotReloadMethodKeys.BuildMethodKeyParts(
                    typeMetadataName,
                    caller.Name,
                    parameterTypeFullNames,
                    caller.GenericParameters.Count),
                TargetMethodKey = HotReloadMethodKeys.BuildMethodKeyParts(
                    target.TypeMetadataName,
                    target.MethodName,
                    target.ParameterTypeFullNames,
                    target.GenericArity),
                IsFunctionPointerLoad = isFunctionPointerLoad
            };
        }
    }
}
