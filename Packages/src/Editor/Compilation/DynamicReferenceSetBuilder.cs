using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using Assembly = System.Reflection.Assembly;

namespace io.github.hatayama.uLoopMCP
{
    internal static class DynamicReferenceSetBuilder
    {
        private static readonly object ReferenceCatalogLock = new();
        private static readonly string[] BaseReferenceAssemblyNames =
        {
            "mscorlib",
            "netstandard",
            "System",
            "System.Core",
            "System.Runtime",
            "System.Collections",
            "System.Net.Http",
            "System.Threading",
            "System.Threading.Tasks",
            "UnityEngine.CoreModule",
            "UnityEditor.CoreModule",
        };

        private static Dictionary<string, string> _cachedAssemblyLocationsByName;
        private static int _cachedAssemblyCount = -1;

        public static List<string> BuildReferenceSet(
            List<string> additionalReferences,
            IReadOnlyCollection<string> resolvedAssemblyReferences,
            ExternalCompilerPaths externalCompilerPaths)
        {
            Dictionary<string, string> assemblyLocationsByName = GetCachedAssemblyLocationsByName();
            List<string> baseReferences = new List<string>();

            foreach (string assemblyName in BaseReferenceAssemblyNames)
            {
                string preferredReferencePath = GetPreferredBaseReferencePath(externalCompilerPaths, assemblyName);
                if (!string.IsNullOrEmpty(preferredReferencePath) && File.Exists(preferredReferencePath))
                {
                    baseReferences.Add(preferredReferencePath);
                    continue;
                }

                if (assemblyLocationsByName.TryGetValue(assemblyName, out string loadedAssemblyPath))
                {
                    baseReferences.Add(loadedAssemblyPath);
                }
            }

            AddLoadedAssemblyReferences(baseReferences, assemblyLocationsByName);

            List<string> mergedAdditionalReferences = new List<string>();
            AddExistingReferences(mergedAdditionalReferences, additionalReferences);
            AddExistingReferences(mergedAdditionalReferences, resolvedAssemblyReferences);

            string[] mergedReferences = MergeReferencesByAssemblyName(
                baseReferences.ToArray(),
                mergedAdditionalReferences);
            return new List<string>(mergedReferences);
        }

        internal static string[] MergeReferencesByAssemblyName(string[] baseReferences, List<string> additionalReferences)
        {
            UnityEngine.Debug.Assert(baseReferences != null, "baseReferences must not be null");
            UnityEngine.Debug.Assert(additionalReferences != null, "additionalReferences must not be null");

            HashSet<string> seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> mergedReferences = new List<string>(baseReferences.Length + additionalReferences.Count);

            foreach (string baseReference in baseReferences)
            {
                string assemblyName = Path.GetFileNameWithoutExtension(baseReference);
                if (seenNames.Add(assemblyName))
                {
                    mergedReferences.Add(baseReference);
                }
            }

            foreach (string additionalReference in additionalReferences)
            {
                if (string.IsNullOrWhiteSpace(additionalReference) || !File.Exists(additionalReference))
                {
                    continue;
                }

                string assemblyName = Path.GetFileNameWithoutExtension(additionalReference);
                if (seenNames.Add(assemblyName))
                {
                    mergedReferences.Add(additionalReference);
                }
            }

            return mergedReferences.ToArray();
        }

        [InitializeOnLoadMethod]
        private static void InvalidateReferenceCacheOnDomainReload()
        {
            lock (ReferenceCatalogLock)
            {
                _cachedAssemblyLocationsByName = null;
                _cachedAssemblyCount = -1;
            }
        }

        private static void AddLoadedAssemblyReferences(
            List<string> destination,
            IReadOnlyDictionary<string, string> assemblyLocationsByName)
        {
            List<string> assemblyNames = new List<string>(assemblyLocationsByName.Keys);
            assemblyNames.Sort(StringComparer.OrdinalIgnoreCase);

            foreach (string assemblyName in assemblyNames)
            {
                if (!assemblyLocationsByName.TryGetValue(assemblyName, out string assemblyPath)
                    || string.IsNullOrEmpty(assemblyPath)
                    || !File.Exists(assemblyPath))
                {
                    continue;
                }

                destination.Add(assemblyPath);
            }
        }

        private static Dictionary<string, string> GetCachedAssemblyLocationsByName()
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

            lock (ReferenceCatalogLock)
            {
                if (_cachedAssemblyLocationsByName != null && _cachedAssemblyCount == assemblies.Length)
                {
                    return _cachedAssemblyLocationsByName;
                }

                Dictionary<string, string> assemblyLocationsByName =
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (Assembly assembly in assemblies)
                {
                    if (assembly.IsDynamic)
                    {
                        continue;
                    }

                    string location;
                    try
                    {
                        location = assembly.Location;
                    }
                    catch (NotSupportedException)
                    {
                        continue;
                    }

                    string assemblyName = assembly.GetName().Name;
                    if (string.IsNullOrEmpty(location)
                        || !File.Exists(location)
                        || string.IsNullOrEmpty(assemblyName)
                        || assemblyLocationsByName.ContainsKey(assemblyName))
                    {
                        continue;
                    }

                    assemblyLocationsByName.Add(assemblyName, location);
                }

                _cachedAssemblyLocationsByName = assemblyLocationsByName;
                _cachedAssemblyCount = assemblies.Length;
                return _cachedAssemblyLocationsByName;
            }
        }

        private static string GetPreferredBaseReferencePath(
            ExternalCompilerPaths externalCompilerPaths,
            string assemblyName)
        {
            if (externalCompilerPaths == null)
            {
                return null;
            }

            return ResolvePreferredBaseReferencePath(
                externalCompilerPaths.EditorContentsPath,
                externalCompilerPaths.ScriptingRootPath,
                assemblyName);
        }

        internal static string ResolvePreferredBaseReferencePath(
            string editorContentsPath,
            string assemblyName)
        {
            return ResolvePreferredBaseReferencePath(editorContentsPath, null, assemblyName);
        }

        internal static string ResolvePreferredBaseReferencePath(
            string editorContentsPath,
            string scriptingRootPath,
            string assemblyName)
        {
            foreach (string referenceRootPath in EnumerateReferenceRootPaths(editorContentsPath, scriptingRootPath))
            {
                string candidatePath = BuildPreferredBaseReferencePath(referenceRootPath, assemblyName);
                if (!string.IsNullOrEmpty(candidatePath) && File.Exists(candidatePath))
                {
                    return candidatePath;
                }
            }

            return null;
        }

        private static IEnumerable<string> EnumerateReferenceRootPaths(
            string editorContentsPath,
            string scriptingRootPath)
        {
            HashSet<string> emittedPaths = new HashSet<string>(StringComparer.Ordinal);

            if (!string.IsNullOrEmpty(scriptingRootPath) && emittedPaths.Add(scriptingRootPath))
            {
                yield return scriptingRootPath;
            }

            if (string.IsNullOrEmpty(editorContentsPath))
            {
                yield break;
            }

            string resourcesScriptingPath = Path.Combine(editorContentsPath, "Resources", "Scripting");
            if (emittedPaths.Add(resourcesScriptingPath))
            {
                yield return resourcesScriptingPath;
            }

            if (emittedPaths.Add(editorContentsPath))
            {
                yield return editorContentsPath;
            }
        }

        private static string BuildPreferredBaseReferencePath(string referenceRootPath, string assemblyName)
        {
            switch (assemblyName)
            {
                case "mscorlib":
                case "netstandard":
                case "System":
                case "System.Core":
                case "System.Net.Http":
                    return Path.Combine(referenceRootPath, "UnityReferenceAssemblies", "unity-4.8-api", $"{assemblyName}.dll");
                case "System.Runtime":
                case "System.Collections":
                case "System.Threading":
                case "System.Threading.Tasks":
                    return Path.Combine(referenceRootPath, "UnityReferenceAssemblies", "unity-4.8-api", "Facades", $"{assemblyName}.dll");
                case "UnityEngine":
                case "UnityEditor":
                    return Path.Combine(referenceRootPath, "Managed", $"{assemblyName}.dll");
                case "UnityEngine.CoreModule":
                case "UnityEditor.CoreModule":
                    return Path.Combine(referenceRootPath, "Managed", "UnityEngine", $"{assemblyName}.dll");
                default:
                    return null;
            }
        }

        private static void AddExistingReferences(
            List<string> destination,
            IReadOnlyCollection<string> source)
        {
            if (source == null)
            {
                return;
            }

            foreach (string reference in source)
            {
                if (string.IsNullOrEmpty(reference) || !File.Exists(reference))
                {
                    continue;
                }

                destination.Add(reference);
            }
        }
    }
}
