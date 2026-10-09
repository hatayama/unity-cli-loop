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

            /// <summary>
            /// Diagnostic count of the call sites this scan compared against its targets, summed over
            /// the scanned assemblies. Nothing in a run reads it; it shows that a scan visits only the
            /// call sites filed under its targets rather than every call site of an assembly.
            /// </summary>
            public int ExaminedCallSiteCount;

            /// <summary>
            /// Assemblies of the scan set whose MemberRef table names none of the targets, so they
            /// were not read; diagnostic, like ExaminedCallSiteCount.
            /// </summary>
            public List<string> SkippedScanAssemblyNames;

            /// <summary>
            /// Assemblies of the scan set that were not in the call-site cache and that the load
            /// budget did not let this scan read; a result listing any is incomplete, like one with
            /// a missing assembly.
            /// </summary>
            public List<string> UnreadScanAssemblyNames;

            public bool IsIncomplete => MissingScanAssemblyNames.Count > 0 || UnreadScanAssemblyNames.Count > 0;

            public HotReloadCallSiteScanResult(
                List<CallSiteHit> hits,
                List<string> missingScanAssemblyNames,
                int examinedCallSiteCount = 0,
                List<string> skippedScanAssemblyNames = null,
                List<string> unreadScanAssemblyNames = null)
            {
                Hits = hits;
                MissingScanAssemblyNames = missingScanAssemblyNames;
                ExaminedCallSiteCount = examinedCallSiteCount;
                SkippedScanAssemblyNames = skippedScanAssemblyNames ?? new List<string>();
                UnreadScanAssemblyNames = unreadScanAssemblyNames ?? new List<string>();
            }
        }

        /// <summary>
        /// Identity of a compiled method to search for (assembly + type + name + arity + parameter types).
        /// </summary>
        public readonly struct CompiledMethodIdentity
        {
            public readonly string AssemblyName;
            public readonly HotReloadMetadataTypeName TypeMetadataName;
            public readonly string MethodName;
            public readonly string[] ParameterTypeFullNames;
            public readonly int GenericArity;

            public CompiledMethodIdentity(
                string assemblyName,
                HotReloadMetadataTypeName typeMetadataName,
                string methodName,
                string[] parameterTypeFullNames,
                int genericArity)
            {
                Debug.Assert(!string.IsNullOrEmpty(assemblyName), "assemblyName must not be null or empty.");
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
            public HotReloadMetadataTypeName CallerTypeMetadataName;
            public string CallerMethodName;
            public string[] CallerParameterTypeFullNames;
            public int CallerGenericArity;
            public string CallerMethodKey;
            public string TargetMethodKey;
            public bool IsFunctionPointerLoad;
        }

        /// <summary>
        /// Finds compiled call / ldftn sites that reference any of <paramref name="targets"/>.
        /// With a <paramref name="loadBudget"/>, assemblies that are not in the call-site cache are
        /// read only while the budget has a load left; the rest are listed as unread. Null reads
        /// without limit.
        /// </summary>
        public static HotReloadCallSiteScanResult FindCallSites(
            string projectRoot,
            CompiledMethodIdentity[] targets,
            HotReloadCallSiteLoadBudget loadBudget = null)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty.");
            Debug.Assert(targets != null, "targets must not be null.");

            List<CallSiteHit> hits = new List<CallSiteHit>();
            List<string> missingScanAssemblyNames = new List<string>();
            if (targets.Length == 0)
            {
                return new HotReloadCallSiteScanResult(hits, missingScanAssemblyNames);
            }

            HashSet<string> targetAssemblyNames = CollectTargetAssemblyNames(targets);
            HashSet<string> scanAssemblyNames = CollectScanAssemblyNames(targets, targetAssemblyNames);
            HashSet<string> targetKeys = CollectTargetKeys(targets);
            List<string> skippedScanAssemblyNames = new List<string>();
            List<string> unreadScanAssemblyNames = new List<string>();
            int examinedCallSiteCount = 0;
            CompiledAssemblyLayout layout = CompiledAssemblyLayout.Resolve(projectRoot);
            foreach (string assemblyName in scanAssemblyNames)
            {
                string dllPath = layout.DllPath(assemblyName);

                // Why skip (not assert): an assembly that has not been written to ScriptAssemblies
                // cannot contain call sites, so it cannot be a caller. Missing here is "not compiled
                // yet", not a broken invariant.
                if (!File.Exists(dllPath))
                {
                    missingScanAssemblyNames.Add(assemblyName);
                    continue;
                }

                // Why a target assembly is always walked: its own call sites are MethodDef operands
                // with no MemberRef row, so the table cannot vouch for them.
                if (!targetAssemblyNames.Contains(assemblyName)
                    && !HotReloadReferencedMethodIndex.Shared.MentionsAny(dllPath, targetKeys))
                {
                    skippedScanAssemblyNames.Add(assemblyName);
                    continue;
                }

                // Why the budget comes after the MemberRef skip: an assembly that names no target
                // is not read either way, so it must not cost a load or appear as unread. Why the
                // loop goes on after a refusal: the remaining assemblies still sort into skipped
                // and unread, so the unread list names only the dlls the scan could not read.
                if (!TryCollectHitsFromAssembly(assemblyName, dllPath, targets, loadBudget, hits, out int examined))
                {
                    Debug.Assert(loadBudget != null, "a load is refused only when a budget is in place.");
                    unreadScanAssemblyNames.Add(assemblyName);
                    loadBudget.Refuse(assemblyName);
                    continue;
                }

                examinedCallSiteCount += examined;
            }

            return new HotReloadCallSiteScanResult(
                hits,
                missingScanAssemblyNames,
                examinedCallSiteCount,
                skippedScanAssemblyNames,
                unreadScanAssemblyNames);
        }

        private static HashSet<string> CollectTargetAssemblyNames(CompiledMethodIdentity[] targets)
        {
            HashSet<string> targetAssemblyNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (CompiledMethodIdentity target in targets)
            {
                targetAssemblyNames.Add(target.AssemblyName);
            }

            return targetAssemblyNames;
        }

        // Keys the referenced-method index files a MemberRef row under. The type name is the
        // metadata name (nested types joined with '/'), the spelling Cecil's FullName uses.
        private static HashSet<string> CollectTargetKeys(CompiledMethodIdentity[] targets)
        {
            HashSet<string> targetKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (CompiledMethodIdentity target in targets)
            {
                targetKeys.Add(HotReloadReferencedMethodIndex.BuildKey(
                    target.AssemblyName,
                    target.TypeMetadataName.Value,
                    target.MethodName));
            }

            return targetKeys;
        }

        private static HashSet<string> CollectScanAssemblyNames(
            CompiledMethodIdentity[] targets,
            HashSet<string> targetAssemblyNames)
        {
            HashSet<string> targetDllFileNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (CompiledMethodIdentity target in targets)
            {
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

        /// <summary>
        /// The compiled dlls of the other assemblies that reference <paramref name="targetAssemblyName"/>,
        /// the ones a caller scan of its methods would consult the referenced-method index for.
        /// Only dlls that exist are returned. Main thread only: it reads the compilation pipeline.
        /// </summary>
        internal static IReadOnlyList<string> CollectReferencingDllPaths(string projectRoot, string targetAssemblyName)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty.");
            Debug.Assert(!string.IsNullOrEmpty(targetAssemblyName), "targetAssemblyName must not be null or empty.");

            HashSet<string> targetDllFileNames = new HashSet<string>(StringComparer.Ordinal)
            {
                targetAssemblyName + HotReloadConstants.CompiledAssemblyExtension
            };
            CompiledAssemblyLayout layout = CompiledAssemblyLayout.Resolve(projectRoot);
            List<string> dllPaths = new List<string>();
            foreach (KeyValuePair<string, string[]> assembly in GetReferencedDllFileNamesByAssembly())
            {
                if (string.Equals(assembly.Key, targetAssemblyName, StringComparison.Ordinal)
                    || !ReferencesAnyTargetDll(assembly.Value, targetDllFileNames))
                {
                    continue;
                }

                string dllPath = layout.DllPath(assembly.Key);
                if (File.Exists(dllPath))
                {
                    dllPaths.Add(dllPath);
                }
            }

            return dllPaths;
        }

        // Why cache per domain: CompilationPipeline.GetAssemblies() costs ~40 ms on a project with
        // ~100 assemblies, and the reference graph can only change through an asmdef or package
        // change, which recompiles and reloads the domain (resetting this field).
        private static Dictionary<string, string[]> _referencedDllFileNamesByAssembly;

        static HotReloadCallSiteScanner()
        {
            // Why: a compile that fails keeps the domain alive, so without this an asmdef change
            // made during a broken compile would survive in the memo until the next reload.
            CompilationPipeline.compilationStarted += _ => _referencedDllFileNamesByAssembly = null;
        }

        private static Dictionary<string, string[]> GetReferencedDllFileNamesByAssembly()
        {
            if (_referencedDllFileNamesByAssembly != null)
            {
                return _referencedDllFileNamesByAssembly;
            }

            Dictionary<string, string[]> graph = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (UnityCompilationAssembly assembly in HotReloadCompilationAssemblies.Current())
            {
                graph[assembly.name] = CollectReferenceFileNames(assembly.allReferences);
            }

            // Why not memoize an empty graph: GetAssemblies() returns nothing while a compile is in
            // flight, and caching that would shrink the scan set for the rest of the domain's life.
            if (graph.Count > 0)
            {
                _referencedDllFileNamesByAssembly = graph;
            }

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

        // Outputs how many call sites of the assembly it compared against the targets in
        // examinedCallSiteCount; returns false when the dll is not cached and the budget has no
        // load left, in which case nothing was read and no hit was added.
        private static bool TryCollectHitsFromAssembly(
            string assemblyName,
            string dllPath,
            CompiledMethodIdentity[] targets,
            HotReloadCallSiteLoadBudget loadBudget,
            List<CallSiteHit> hits,
            out int examinedCallSiteCount)
        {
            // Why cache: the dll only changes on a compile, which also reloads the domain, so
            // across the runs in between the Cecil read and the instruction walk are pure repeat work.
            bool allowLoad = loadBudget == null || loadBudget.RemainingLoads > 0;
            if (!HotReloadCompiledCallSiteCache.Shared.TryGetOrLoad(dllPath, allowLoad, out HotReloadCompiledCallSiteCache.Entry compiled, out bool loaded))
            {
                examinedCallSiteCount = 0;
                return false;
            }

            // Why consume on loaded rather than on allowLoad: a cached dll costs nothing, so only a
            // read takes a load from the budget.
            if (loaded && loadBudget != null)
            {
                bool consumed = loadBudget.TryConsume();
                Debug.Assert(consumed, "a load was allowed only while the budget had a load left.");
            }

            List<int> positions = CollectCandidatePositions(compiled, targets);
            foreach (int position in positions)
            {
                CollectHitFromCallSite(assemblyName, compiled, compiled.CallSites[position], targets, hits);
            }

            examinedCallSiteCount = positions.Count;
            return true;
        }

        // Narrows the walk to the call sites filed under a target's type and method name; the
        // full identity match still decides each of them.
        private static List<int> CollectCandidatePositions(
            HotReloadCompiledCallSiteCache.Entry compiled,
            CompiledMethodIdentity[] targets)
        {
            // Why skip a repeated key: targets that share a type and a name (overloads, arities)
            // share one bucket, and visiting it twice would report its call sites twice.
            HashSet<(string TypeName, string MethodName)> visitedKeys = new HashSet<(string TypeName, string MethodName)>();
            List<int> positions = new List<int>();
            foreach (CompiledMethodIdentity target in targets)
            {
                string typeName = target.TypeMetadataName.Value;
                if (!visitedKeys.Add((typeName, target.MethodName)))
                {
                    continue;
                }

                positions.AddRange(compiled.LookupCallSiteIndices(typeName, target.MethodName));
            }

            // Why sort: buckets arrive in target order; ascending positions keep the hits in the
            // order of the call sites in the dll, as the walk over every call site reported them.
            positions.Sort();
            return positions;
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

            TypeReference openDeclaringType = HotReloadCompiledCallSiteIndex.GetOpenDeclaringType(openMethod.DeclaringType);
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
            if (openDeclaringType.FullName != target.TypeMetadataName.Value)
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

            HotReloadMetadataTypeName typeMetadataName = new HotReloadMetadataTypeName(caller.DeclaringType.FullName);

            return new CallSiteHit
            {
                CallerAssemblyName = assemblyName,
                CallerTypeMetadataName = typeMetadataName,
                CallerMethodName = caller.Name,
                CallerParameterTypeFullNames = parameterTypeFullNames,
                CallerGenericArity = caller.GenericParameters.Count,
                CallerMethodKey = HotReloadMethodKeys.BuildMethodKeyParts(
                    typeMetadataName.Value,
                    caller.Name,
                    parameterTypeFullNames,
                    caller.GenericParameters.Count),
                TargetMethodKey = HotReloadMethodKeys.BuildMethodKeyParts(
                    target.TypeMetadataName.Value,
                    target.MethodName,
                    target.ParameterTypeFullNames,
                    target.GenericArity),
                IsFunctionPointerLoad = isFunctionPointerLoad
            };
        }
    }
}
