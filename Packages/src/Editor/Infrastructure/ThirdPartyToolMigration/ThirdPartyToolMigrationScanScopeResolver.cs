using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Resolves the assembly directories that a set of seed files (e.g. compile-error-matched
    /// migration target files) belong to, so plan construction can scan only those assemblies
    /// instead of the whole project. Reuses
    /// ThirdPartyToolMigrationAssemblyReferenceResolver.FindNearestAssemblyDirectory per seed file
    /// and deduplicates the results.
    /// </summary>
    internal static class ThirdPartyToolMigrationScanScopeResolver
    {
        internal static List<string> ResolveScopeAssemblyDirectories(
            List<string> seedFilePaths,
            List<string> asmdefDirectories,
            List<AssemblyReferenceDirectory> assemblyReferenceDirectories,
            string projectRoot)
        {
            Debug.Assert(seedFilePaths != null, "seedFilePaths must not be null");
            Debug.Assert(asmdefDirectories != null, "asmdefDirectories must not be null");
            Debug.Assert(
                assemblyReferenceDirectories != null,
                "assemblyReferenceDirectories must not be null");
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            if (seedFilePaths == null)
            {
                throw new ArgumentNullException(nameof(seedFilePaths));
            }

            HashSet<string> seenAssemblyDirectories = new HashSet<string>(StringComparer.Ordinal);
            List<string> scopeAssemblyDirectories = new List<string>();
            foreach (string seedFilePath in seedFilePaths)
            {
                string assemblyDirectory = ThirdPartyToolMigrationAssemblyReferenceResolver.FindNearestAssemblyDirectory(
                    seedFilePath,
                    asmdefDirectories,
                    assemblyReferenceDirectories,
                    projectRoot);

                if (seenAssemblyDirectories.Add(assemblyDirectory))
                {
                    scopeAssemblyDirectories.Add(assemblyDirectory);
                }
            }

            return scopeAssemblyDirectories;
        }
    }
}
