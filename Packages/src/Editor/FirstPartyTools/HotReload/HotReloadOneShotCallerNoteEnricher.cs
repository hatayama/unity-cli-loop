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
        internal sealed class Candidate
        {
            public HotReloadCallSiteScanner.CompiledMethodIdentity Identity;
            public HotReloadMethodOutcome Outcome;

            public Candidate(
                HotReloadCallSiteScanner.CompiledMethodIdentity identity,
                HotReloadMethodOutcome outcome)
            {
                Identity = identity;
                Outcome = outcome;
            }
        }

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
        internal static bool IsOneShotLifecycleCaller(HotReloadCallSiteScanner.CallSiteHit hit)
        {
            if (hit == null || !IsOneShotLifecycleMethodName(hit.CallerMethodName))
            {
                return false;
            }

            Assembly assembly = FindLoadedAssemblyByName(hit.CallerAssemblyName);
            if (assembly == null)
            {
                return false;
            }

            Type callerType = assembly.GetType(hit.CallerTypeMetadataName.Replace('/', '+'));
            if (callerType == null || !typeof(MonoBehaviour).IsAssignableFrom(callerType))
            {
                return false;
            }

            MethodInfo method = callerType.GetMethod(
                hit.CallerMethodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            return method != null && method.ReturnType == typeof(void);
        }

        internal static void ApplyNotes(
            string projectRoot,
            List<HotReloadMethodOutcome> outcomes,
            IReadOnlyList<Candidate> candidates,
            Func<string, HotReloadCallSiteScanner.CompiledMethodIdentity[], HotReloadCallSiteScanner.HotReloadCallSiteScanResult> scan)
        {
            Dictionary<string, List<Candidate>> candidatesByAssembly =
                new Dictionary<string, List<Candidate>>(StringComparer.Ordinal);
            foreach (Candidate candidate in candidates)
            {
                if (!string.IsNullOrEmpty(candidate.Outcome.LifecycleNote))
                {
                    continue;
                }

                if (!candidatesByAssembly.TryGetValue(candidate.Identity.AssemblyName, out List<Candidate> group))
                {
                    group = new List<Candidate>();
                    candidatesByAssembly.Add(candidate.Identity.AssemblyName, group);
                }

                group.Add(candidate);
            }

            foreach (KeyValuePair<string, List<Candidate>> pair in candidatesByAssembly)
            {
                HotReloadCallSiteScanner.CompiledMethodIdentity[] identities = pair.Value.ConvertAll(
                    candidate => candidate.Identity).ToArray();
                HotReloadCallSiteScanner.HotReloadCallSiteScanResult result = scan(pair.Key, identities);
                if (result.MissingScanAssemblyNames.Count > 0)
                {
                    continue;
                }

                foreach (Candidate candidate in pair.Value)
                {
                    string targetKey = HotReloadWireMethodKeys.BuildMethodKeyParts(
                        candidate.Identity.TypeMetadataName,
                        candidate.Identity.MethodName,
                        candidate.Identity.ParameterTypeFullNames,
                        candidate.Identity.GenericArity);
                    List<OneShotCallerClassification> callers = new List<OneShotCallerClassification>();
                    foreach (HotReloadCallSiteScanner.CallSiteHit hit in result.Hits)
                    {
                        if (hit.TargetMethodKey == targetKey)
                        {
                            callers.Add(new OneShotCallerClassification(hit.CallerMethodName, IsOneShotLifecycleCaller(hit)));
                        }
                    }

                    string note = HotReloadOneShotCallerNoteBuilder.Build(candidate.Outcome.Method, callers);
                    if (note == null)
                    {
                        continue;
                    }

                    int index = outcomes.IndexOf(candidate.Outcome);
                    if (index >= 0)
                    {
                        outcomes[index] = candidate.Outcome.WithLifecycleNote(note);
                    }
                }
            }
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
