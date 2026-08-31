using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationAssemblyReferenceResolver;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Builds assembly usage facts for synchronous migration planning.
    /// </summary>
    internal static class ThirdPartyToolMigrationAssemblyUsageAnalyzer
    {
        internal static MigrationAssemblyUsage FindMigrationAssemblyUsage(
            string projectRoot,
            List<string> csharpFilePaths,
            List<string> asmdefFilePaths,
            List<string> asmrefFilePaths)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            Debug.Assert(csharpFilePaths != null, "csharpFilePaths must not be null");
            Debug.Assert(asmdefFilePaths != null, "asmdefFilePaths must not be null");
            Debug.Assert(asmrefFilePaths != null, "asmrefFilePaths must not be null");

            List<string> asmdefDirectories = asmdefFilePaths
                .Select(path => Path.GetDirectoryName(path) ?? string.Empty)
                .Where(path => !string.IsNullOrEmpty(path))
                .OrderByDescending(path => path.Length)
                .ToList();
            List<AssemblyReferenceDirectory> assemblyReferenceDirectories =
                CreateAssemblyReferenceDirectories(asmdefFilePaths, asmrefFilePaths);
            ThirdPartyToolMigrationAssemblyUsageScanState scanState =
                new(projectRoot, asmdefDirectories, assemblyReferenceDirectories);
            Dictionary<string, string> sourceByCSharpFilePath = new(StringComparer.Ordinal);

            foreach (string csharpFilePath in csharpFilePaths)
            {
                string source = ThirdPartyToolMigrationFileAccess.ReadAllText(csharpFilePath);
                sourceByCSharpFilePath.Add(csharpFilePath, source);
                scanState.RecordInitialSourceFacts(source, csharpFilePath);
            }

            foreach (string csharpFilePath in csharpFilePaths)
            {
                string source = sourceByCSharpFilePath[csharpFilePath];
                scanState.RecordReferenceRequirements(source, csharpFilePath);
            }

            return scanState.CreateUsage();
        }
    }
}
