using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Discovers .asmdef/.asmref directory structure only (no C# source files), so the compile-error-
    /// driven auto-scan can resolve seed files to assembly directories without the full project file
    /// walk that ThirdPartyToolMigrationProjectFileInventory performs.
    /// </summary>
    internal static class ThirdPartyToolMigrationLightweightAssemblyWalker
    {
        private const string AssetsDirectoryName = "Assets";
        private const string AsmdefSearchPattern = "*.asmdef";
        private const string AsmrefSearchPattern = "*.asmref";

        internal static (List<string> AsmdefDirectories, List<AssemblyReferenceDirectory> AssemblyReferenceDirectories)
            DiscoverAssemblyStructure(string projectRoot)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            string assetsDirectory = Path.Combine(projectRoot, AssetsDirectoryName);
            if (!Directory.Exists(assetsDirectory))
            {
                return (new List<string>(), new List<AssemblyReferenceDirectory>());
            }

            List<string> asmdefFilePaths = Directory
                .GetFiles(assetsDirectory, AsmdefSearchPattern, SearchOption.AllDirectories)
                .ToList();
            List<string> asmrefFilePaths = Directory
                .GetFiles(assetsDirectory, AsmrefSearchPattern, SearchOption.AllDirectories)
                .ToList();

            List<string> asmdefDirectories = asmdefFilePaths
                .Select(filePath => Path.GetDirectoryName(filePath) ?? string.Empty)
                .Where(directory => directory.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            List<AssemblyReferenceDirectory> assemblyReferenceDirectories =
                ThirdPartyToolMigrationAssemblyReferenceResolver.CreateAssemblyReferenceDirectories(
                    asmdefFilePaths,
                    asmrefFilePaths);

            return (asmdefDirectories, assemblyReferenceDirectories);
        }
    }
}
