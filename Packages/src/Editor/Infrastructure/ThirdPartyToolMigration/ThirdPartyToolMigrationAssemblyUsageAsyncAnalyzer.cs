using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationAssemblyReferenceResolver;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Builds assembly usage facts for asynchronous migration planning.
    /// </summary>
    internal static class ThirdPartyToolMigrationAssemblyUsageAsyncAnalyzer
    {
        internal static async Task<MigrationAssemblyUsage> FindMigrationAssemblyUsageAsync(
            string projectRoot,
            List<string> csharpFilePaths,
            List<string> asmdefFilePaths,
            List<string> asmrefFilePaths,
            ThirdPartyToolMigrationSourceFileCache sourceFileCache,
            MigrationProgressCounter progressCounter,
            CancellationToken ct)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            Debug.Assert(csharpFilePaths != null, "csharpFilePaths must not be null");
            Debug.Assert(asmdefFilePaths != null, "asmdefFilePaths must not be null");
            Debug.Assert(asmrefFilePaths != null, "asmrefFilePaths must not be null");
            Debug.Assert(sourceFileCache != null, "sourceFileCache must not be null");
            Debug.Assert(progressCounter != null, "progressCounter must not be null");

            List<string> asmdefDirectories = asmdefFilePaths
                .Select(path => Path.GetDirectoryName(path) ?? string.Empty)
                .Where(path => !string.IsNullOrEmpty(path))
                .OrderByDescending(path => path.Length)
                .ToList();
            List<AssemblyReferenceDirectory> assemblyReferenceDirectories =
                await CreateAssemblyReferenceDirectoriesAsync(
                    asmdefFilePaths,
                    asmrefFilePaths,
                    sourceFileCache,
                    progressCounter,
                    ct);
            ThirdPartyToolMigrationAssemblyUsageScanState scanState =
                new(projectRoot, asmdefDirectories, assemblyReferenceDirectories);

            foreach (string csharpFilePath in csharpFilePaths)
            {
                if (ct.IsCancellationRequested)
                {
                    return scanState.CreateUsage();
                }

                string source = sourceFileCache.ReadAllText(csharpFilePath);
                await progressCounter.ReportProcessedItemAsync(ct);
                scanState.RecordInitialSourceFacts(source, csharpFilePath);
            }

            foreach (string csharpFilePath in csharpFilePaths)
            {
                if (ct.IsCancellationRequested)
                {
                    return scanState.CreateUsage();
                }

                string source = sourceFileCache.ReadAllText(csharpFilePath);
                await progressCounter.ReportProcessedItemAsync(ct);
                scanState.RecordReferenceRequirements(source, csharpFilePath);
            }

            return scanState.CreateUsage();
        }
    }
}
