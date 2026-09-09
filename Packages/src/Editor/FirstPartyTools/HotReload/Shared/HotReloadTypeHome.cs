using System;
using System.IO;
using System.Reflection;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Where the types a hot-reload patch targets live.
    /// </summary>
    internal enum HotReloadTypeHomeKind
    {
        /// <summary>A Unity project assembly under Library/ScriptAssemblies.</summary>
        ScriptAssemblies,

        /// <summary>An introduced-type artifact assembly retained by this domain.</summary>
        RetainedArtifact
    }

    /// <summary>
    /// One immutable answer to "where do the patch target's types live": the assembly simple name,
    /// the compiled image on disk, and how to find the matching assembly in the AppDomain.
    /// Why one value: the patch-target resolver, the Mvid guard, the method matcher, the
    /// publicizer, and the worker input all used to rebuild that answer independently, so a
    /// target that is not a ScriptAssemblies dll had to be taught to each of them separately.
    /// </summary>
    internal sealed class HotReloadTypeHome
    {
        public HotReloadTypeHomeKind Kind { get; }

        /// <summary>
        /// The assembly simple name: the Unity assembly name for ScriptAssemblies, the generated
        /// artifact name for a retained artifact.
        /// </summary>
        public string AssemblyName { get; }

        /// <summary>The compiled image on disk, as a full path.</summary>
        public string DllPath { get; }

        // Non-null only for RetainedArtifact: that assembly is not discoverable by simple name
        // in the AppDomain, so the home carries the instance it was built from.
        private readonly Assembly retainedAssembly;

        private HotReloadTypeHome(
            HotReloadTypeHomeKind kind,
            string assemblyName,
            string dllPath,
            Assembly retainedAssembly)
        {
            Kind = kind;
            AssemblyName = assemblyName;
            DllPath = dllPath;
            this.retainedAssembly = retainedAssembly;
        }

        /// <summary>
        /// A Unity project assembly compiled into Library/ScriptAssemblies.
        /// </summary>
        public static HotReloadTypeHome ScriptAssemblies(string assemblyName, string dllPath)
        {
            Debug.Assert(!string.IsNullOrEmpty(assemblyName), "assemblyName must not be null or empty.");
            Debug.Assert(!string.IsNullOrEmpty(dllPath), "dllPath must not be null or empty.");

            return new HotReloadTypeHome(
                HotReloadTypeHomeKind.ScriptAssemblies,
                assemblyName,
                dllPath,
                null);
        }

        /// <summary>
        /// A Unity project assembly, named by the project root it is compiled under.
        /// </summary>
        public static HotReloadTypeHome ScriptAssembliesUnderProject(string projectRoot, string assemblyName)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty.");
            Debug.Assert(!string.IsNullOrEmpty(assemblyName), "assemblyName must not be null or empty.");

            string dllPath = Path.Combine(
                projectRoot,
                HotReloadConstants.ScriptAssembliesRelativeDirectory,
                assemblyName + HotReloadConstants.CompiledAssemblyExtension);
            return ScriptAssemblies(assemblyName, dllPath);
        }

        /// <summary>
        /// An introduced-type artifact assembly this domain still holds loaded.
        /// </summary>
        public static HotReloadTypeHome RetainedArtifact(
            string assemblyName,
            string dllPath,
            Assembly loadedAssembly)
        {
            Debug.Assert(!string.IsNullOrEmpty(assemblyName), "assemblyName must not be null or empty.");
            Debug.Assert(!string.IsNullOrEmpty(dllPath), "dllPath must not be null or empty.");
            Debug.Assert(loadedAssembly != null, "loadedAssembly must not be null.");
            Debug.Assert(
                string.Equals(loadedAssembly?.GetName().Name, assemblyName, StringComparison.Ordinal),
                "loadedAssembly must be the assembly assemblyName names.");

            return new HotReloadTypeHome(
                HotReloadTypeHomeKind.RetainedArtifact,
                assemblyName,
                dllPath,
                loadedAssembly);
        }

        /// <summary>
        /// Finds the live assembly for this home and reports whether it still matches
        /// <paramref name="compiledMvid"/>, the Mvid read from the compiled image.
        /// </summary>
        public HotReloadLoadedAssemblyResolution ResolveLoadedAssembly(string compiledMvid)
        {
            Debug.Assert(!string.IsNullOrEmpty(compiledMvid), "compiledMvid must not be null or empty.");

            if (Kind == HotReloadTypeHomeKind.RetainedArtifact)
            {
                return MatchMvid(retainedAssembly, compiledMvid);
            }

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!string.Equals(assembly.GetName().Name, AssemblyName, StringComparison.Ordinal))
                {
                    continue;
                }

                return MatchMvid(assembly, compiledMvid);
            }

            return HotReloadLoadedAssemblyResolution.NotLoaded();
        }

        // The Mvid stays a string: it is compared against the "D"-format Guid the Cecil reader
        // produced, and the callers pass that string straight through.
        private static HotReloadLoadedAssemblyResolution MatchMvid(Assembly assembly, string compiledMvid)
        {
            if (!string.Equals(
                    assembly.ManifestModule.ModuleVersionId.ToString(),
                    compiledMvid,
                    StringComparison.Ordinal))
            {
                return HotReloadLoadedAssemblyResolution.Stale();
            }

            return HotReloadLoadedAssemblyResolution.Loaded(assembly);
        }
    }
}
