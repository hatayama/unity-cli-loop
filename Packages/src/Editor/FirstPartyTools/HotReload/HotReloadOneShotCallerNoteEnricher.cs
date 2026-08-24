using System;
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
