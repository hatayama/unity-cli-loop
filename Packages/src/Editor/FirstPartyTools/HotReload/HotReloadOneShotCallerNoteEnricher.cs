using System;
using System.Collections.Generic;
using System.Reflection;

using UnityEngine;

using Assembly = System.Reflection.Assembly;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Classifies compiled callers that can be proven to be Unity one-shot lifecycle messages.
    /// </summary>
    internal static class HotReloadOneShotCallerNoteEnricher
    {
        // Keep in sync with LifecycleNotes.OneShotLifecycleMethodNames in the transform worker.
        private static readonly string[] OneShotLifecycleMethodNames =
        {
            "Awake",
            "Start",
            "OnEnable",
            "OnDisable",
            "OnDestroy"
        };

        /// <summary>
        /// Determines whether a compiled caller can be proven to be a one-shot lifecycle message.
        /// </summary>
        internal static bool IsOneShotLifecycleCaller(HotReloadCallSiteHit hit)
        {
            if (hit == null || !IsOneShotLifecycleMethodName(hit.CallerMethodName))
            {
                return false;
            }

            if (hit.CallerParameterTypeFullNames == null
                || hit.CallerParameterTypeFullNames.Length != 0
                || hit.CallerGenericArity != 0)
            {
                return false;
            }

            Assembly assembly = FindLoadedAssemblyByName(hit.CallerAssemblyName);
            if (assembly == null)
            {
                return false;
            }

            Type callerType = assembly.GetType(hit.CallerTypeMetadataName.ToReflectionName().Value);
            if (callerType == null || !typeof(MonoBehaviour).IsAssignableFrom(callerType))
            {
                return false;
            }

            MethodInfo[] methods = callerType.GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            foreach (MethodInfo method in methods)
            {
                if (method.Name != hit.CallerMethodName
                    || method.IsGenericMethodDefinition
                    || method.GetParameters().Length != 0
                    || method.ReturnType != typeof(void))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// Builds the one-shot lifecycle note of each request; the result has one entry per request,
        /// in the same order, null where no note can be proven.
        /// </summary>
        internal static string[] BuildNotes(
            IReadOnlyList<HotReloadOneShotCallerNoteRequest> requests,
            Func<string, HotReloadCompiledMethodIdentity[], HotReloadCallSiteScanner.HotReloadCallSiteScanResult> scan)
        {
            Debug.Assert(requests != null, "requests must not be null.");
            Debug.Assert(scan != null, "scan must not be null.");

            string[] notes = new string[requests.Count];
            // Why grouped in request order: the scans run in this order, and the load budget is spent in it.
            Dictionary<string, List<int>> indexesByAssembly =
                new Dictionary<string, List<int>>(StringComparer.Ordinal);
            for (int index = 0; index < requests.Count; index++)
            {
                string assemblyName = requests[index].Identity.AssemblyName;
                if (!indexesByAssembly.TryGetValue(assemblyName, out List<int> group))
                {
                    group = new List<int>();
                    indexesByAssembly.Add(assemblyName, group);
                }

                group.Add(index);
            }

            foreach (KeyValuePair<string, List<int>> pair in indexesByAssembly)
            {
                HotReloadCompiledMethodIdentity[] identities = pair.Value.ConvertAll(
                    index => requests[index].Identity).ToArray();
                HotReloadCallSiteScanner.HotReloadCallSiteScanResult result = scan(pair.Key, identities);
                if (result.IsIncomplete)
                {
                    continue;
                }

                foreach (int index in pair.Value)
                {
                    HotReloadOneShotCallerNoteRequest request = requests[index];
                    string targetKey = HotReloadMethodKeys.BuildMethodKeyParts(
                        request.Identity.TypeMetadataName.Value,
                        request.Identity.MethodName,
                        request.Identity.ParameterTypeFullNames,
                        request.Identity.GenericArity);
                    List<HotReloadCallSiteHit> targetHits =
                        new List<HotReloadCallSiteHit>();
                    foreach (HotReloadCallSiteHit hit in result.Hits)
                    {
                        if (hit.TargetMethodKey != targetKey)
                        {
                            continue;
                        }

                        targetHits.Add(hit);
                    }

                    List<OneShotCallerClassification> callers = HotReloadOneShotCallerClosure.Resolve(
                        targetHits,
                        scan,
                        IsOneShotLifecycleCaller);
                    if (callers == null)
                    {
                        continue;
                    }

                    notes[index] = HotReloadOneShotCallerNoteBuilder.Build(request.Method, callers);
                }
            }

            return notes;
        }

        private static bool IsOneShotLifecycleMethodName(string methodName)
        {
            foreach (string oneShotMethodName in OneShotLifecycleMethodNames)
            {
                if (oneShotMethodName == methodName)
                {
                    return true;
                }
            }

            return false;
        }

        private static Assembly FindLoadedAssemblyByName(string assemblyName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name == assemblyName)
                {
                    return assembly;
                }
            }

            return null;
        }
    }
}
