using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

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
        /// <summary>
        /// A seed file with no real asmdef/asmref ancestor resolves to a synthetic implicit-assembly
        /// marker path (see ThirdPartyToolMigrationFileServiceConstants) that never exists on disk.
        /// That marker must never be treated as a real scan directory: a scoped file-tree walk skips
        /// nonexistent directories, so silently including it would make a legitimate implicit-assembly
        /// migration target vanish from the scoped scan instead of falling back to a full scan.
        /// HasImplicitAssemblySeeds tells the caller that AssemblyDirectories alone is not a complete,
        /// safe scope in that case.
        /// </summary>
        internal static (List<string> AssemblyDirectories, bool HasImplicitAssemblySeeds) ResolveScopeAssemblyDirectories(
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
            bool hasImplicitAssemblySeeds = false;
            foreach (string seedFilePath in seedFilePaths)
            {
                string assemblyDirectory = ThirdPartyToolMigrationAssemblyReferenceResolver.FindNearestAssemblyDirectory(
                    seedFilePath,
                    asmdefDirectories,
                    assemblyReferenceDirectories,
                    projectRoot);

                if (IsImplicitAssemblyDirectory(assemblyDirectory))
                {
                    hasImplicitAssemblySeeds = true;
                    continue;
                }

                if (seenAssemblyDirectories.Add(assemblyDirectory))
                {
                    scopeAssemblyDirectories.Add(assemblyDirectory);
                }
            }

            return (scopeAssemblyDirectories, hasImplicitAssemblySeeds);
        }

        private static bool IsImplicitAssemblyDirectory(string assemblyDirectory)
        {
            Debug.Assert(!string.IsNullOrEmpty(assemblyDirectory), "assemblyDirectory must not be null or empty");

            string directoryName = Path.GetFileName(assemblyDirectory);
            return string.Equals(
                    directoryName,
                    ThirdPartyToolMigrationFileServiceConstants.ImplicitEditorAssemblyDirectoryName,
                    StringComparison.Ordinal) ||
                string.Equals(
                    directoryName,
                    ThirdPartyToolMigrationFileServiceConstants.ImplicitRuntimeAssemblyDirectoryName,
                    StringComparison.Ordinal) ||
                string.Equals(
                    directoryName,
                    ThirdPartyToolMigrationFileServiceConstants.ImplicitFirstPassEditorAssemblyDirectoryName,
                    StringComparison.Ordinal) ||
                string.Equals(
                    directoryName,
                    ThirdPartyToolMigrationFileServiceConstants.ImplicitFirstPassRuntimeAssemblyDirectoryName,
                    StringComparison.Ordinal);
        }
    }
}
