using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEditor;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Resolves declaring assembly names for a namespace by scanning TypeCache once per Apply.
    /// </summary>
    internal static class CompileMissingReferenceAssemblyLookup
    {
        /// <summary>
        /// Why not scan eagerly: unmatched compiles must stay fail-open and skip TypeCache entirely.
        /// Why TypeCache only: Assembly.GetTypes() needs try-catch for ReflectionTypeLoadException.
        /// </summary>
        internal static Func<string, string[]> CreateLazyFinder()
        {
            Dictionary<string, string[]> index = null;
            return searchName =>
            {
                if (index == null)
                {
                    index = BuildIndex();
                }

                if (searchName == null)
                {
                    return Array.Empty<string>();
                }

                if (index.TryGetValue(searchName, out string[] assemblyNames))
                {
                    return assemblyNames;
                }

                return Array.Empty<string>();
            };
        }

        private static Dictionary<string, string[]> BuildIndex()
        {
            Debug.Assert(
                MainThreadSwitcher.IsMainThread,
                "TypeCache.GetTypesDerivedFrom must run on the Unity main thread.");

            Dictionary<string, SortedSet<string>> grouped =
                new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
            foreach (Type type in TypeCache.GetTypesDerivedFrom(typeof(object)))
            {
                string namespaceName = type.Namespace;
                if (string.IsNullOrEmpty(namespaceName))
                {
                    continue;
                }

                string assemblyName = type.Assembly.GetName().Name;
                if (!grouped.TryGetValue(namespaceName, out SortedSet<string> assemblyNames))
                {
                    assemblyNames = new SortedSet<string>(StringComparer.Ordinal);
                    grouped.Add(namespaceName, assemblyNames);
                }

                assemblyNames.Add(assemblyName);
            }

            Dictionary<string, string[]> index =
                new Dictionary<string, string[]>(grouped.Count, StringComparer.Ordinal);
            foreach (KeyValuePair<string, SortedSet<string>> pair in grouped)
            {
                string[] assemblyNames = new string[pair.Value.Count];
                pair.Value.CopyTo(assemblyNames);
                index.Add(pair.Key, assemblyNames);
            }

            return index;
        }
    }
}
