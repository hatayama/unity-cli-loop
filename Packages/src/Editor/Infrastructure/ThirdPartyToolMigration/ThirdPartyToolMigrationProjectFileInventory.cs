using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    internal sealed class ProjectFileInventory
    {
        private ProjectFileInventory(
            List<string> csharpFilePaths,
            List<string> asmdefFilePaths,
            List<string> asmrefFilePaths)
        {
            CSharpFilePaths = csharpFilePaths;
            AsmdefFilePaths = asmdefFilePaths;
            AsmrefFilePaths = asmrefFilePaths;
        }

        public List<string> CSharpFilePaths { get; }
        public List<string> AsmdefFilePaths { get; }
        public List<string> AsmrefFilePaths { get; }

        public static ProjectFileInventory Create(string projectRoot)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            List<string> csharpFilePaths = new();
            List<string> asmdefFilePaths = new();
            List<string> asmrefFilePaths = new();
            string assetsDirectory = Path.Combine(projectRoot, "Assets");
            if (Directory.Exists(assetsDirectory))
            {
                CollectCandidateFiles(
                    projectRoot,
                    assetsDirectory,
                    csharpFilePaths,
                    asmdefFilePaths,
                    asmrefFilePaths);
            }

            csharpFilePaths.Sort(StringComparer.Ordinal);
            asmdefFilePaths.Sort(StringComparer.Ordinal);
            asmrefFilePaths.Sort(StringComparer.Ordinal);
            return new ProjectFileInventory(csharpFilePaths, asmdefFilePaths, asmrefFilePaths);
        }

        public static async Task<ProjectFileInventory> CreateAsync(
            string projectRoot,
            IProgress<ThirdPartyToolMigrationProgress> progress,
            CancellationToken ct)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            Debug.Assert(progress != null, "progress must not be null");

            List<string> csharpFilePaths = new();
            List<string> asmdefFilePaths = new();
            List<string> asmrefFilePaths = new();
            string assetsDirectory = Path.Combine(projectRoot, "Assets");
            if (Directory.Exists(assetsDirectory))
            {
                await CollectCandidateFilesAsync(
                    projectRoot,
                    assetsDirectory,
                    csharpFilePaths,
                    asmdefFilePaths,
                    asmrefFilePaths,
                    progress,
                    ct);
            }

            csharpFilePaths.Sort(StringComparer.Ordinal);
            asmdefFilePaths.Sort(StringComparer.Ordinal);
            asmrefFilePaths.Sort(StringComparer.Ordinal);
            return new ProjectFileInventory(csharpFilePaths, asmdefFilePaths, asmrefFilePaths);
        }

        private static void CollectCandidateFiles(
            string projectRoot,
            string directoryPath,
            List<string> csharpFilePaths,
            List<string> asmdefFilePaths,
            List<string> asmrefFilePaths)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(directoryPath), "directoryPath must not be null or empty");
            Debug.Assert(csharpFilePaths != null, "csharpFilePaths must not be null");
            Debug.Assert(asmdefFilePaths != null, "asmdefFilePaths must not be null");
            Debug.Assert(asmrefFilePaths != null, "asmrefFilePaths must not be null");

            foreach (string filePath in Directory.EnumerateFiles(directoryPath))
            {
                string extension = Path.GetExtension(filePath);
                if (string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase))
                {
                    csharpFilePaths.Add(filePath);
                    continue;
                }

                if (string.Equals(extension, ".asmdef", StringComparison.OrdinalIgnoreCase))
                {
                    asmdefFilePaths.Add(filePath);
                    continue;
                }

                if (string.Equals(extension, ".asmref", StringComparison.OrdinalIgnoreCase))
                {
                    asmrefFilePaths.Add(filePath);
                }
            }

            foreach (string childDirectoryPath in Directory.EnumerateDirectories(directoryPath))
            {
                if (ShouldExcludeDirectory(projectRoot, childDirectoryPath))
                {
                    continue;
                }

                CollectCandidateFiles(
                    projectRoot,
                    childDirectoryPath,
                    csharpFilePaths,
                    asmdefFilePaths,
                    asmrefFilePaths);
            }
        }

        private static async Task CollectCandidateFilesAsync(
            string projectRoot,
            string assetsDirectory,
            List<string> csharpFilePaths,
            List<string> asmdefFilePaths,
            List<string> asmrefFilePaths,
            IProgress<ThirdPartyToolMigrationProgress> progress,
            CancellationToken ct)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(assetsDirectory), "assetsDirectory must not be null or empty");
            Debug.Assert(csharpFilePaths != null, "csharpFilePaths must not be null");
            Debug.Assert(asmdefFilePaths != null, "asmdefFilePaths must not be null");
            Debug.Assert(asmrefFilePaths != null, "asmrefFilePaths must not be null");
            Debug.Assert(progress != null, "progress must not be null");

            Stack<string> pendingDirectories = new();
            pendingDirectories.Push(assetsDirectory);
            int inspectedEntryCount = 0;
            progress.Report(new ThirdPartyToolMigrationProgress(0, 0));

            while (pendingDirectories.Count > 0)
            {
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                string directoryPath = pendingDirectories.Pop();
                foreach (string filePath in Directory.EnumerateFiles(directoryPath))
                {
                    AddCandidateFilePath(filePath, csharpFilePaths, asmdefFilePaths, asmrefFilePaths);
                    inspectedEntryCount++;
                    if (inspectedEntryCount % ThirdPartyToolMigrationFileServiceConstants.PreviewYieldBatchSize == 0)
                    {
                        progress.Report(new ThirdPartyToolMigrationProgress(0, 0));
                        await Task.Yield();
                    }
                }

                foreach (string childDirectoryPath in Directory.EnumerateDirectories(directoryPath))
                {
                    if (ShouldExcludeDirectory(projectRoot, childDirectoryPath))
                    {
                        continue;
                    }

                    pendingDirectories.Push(childDirectoryPath);
                    inspectedEntryCount++;
                    if (inspectedEntryCount % ThirdPartyToolMigrationFileServiceConstants.PreviewYieldBatchSize == 0)
                    {
                        progress.Report(new ThirdPartyToolMigrationProgress(0, 0));
                        await Task.Yield();
                    }
                }
            }
        }

        private static void AddCandidateFilePath(
            string filePath,
            List<string> csharpFilePaths,
            List<string> asmdefFilePaths,
            List<string> asmrefFilePaths)
        {
            Debug.Assert(!string.IsNullOrEmpty(filePath), "filePath must not be null or empty");
            Debug.Assert(csharpFilePaths != null, "csharpFilePaths must not be null");
            Debug.Assert(asmdefFilePaths != null, "asmdefFilePaths must not be null");
            Debug.Assert(asmrefFilePaths != null, "asmrefFilePaths must not be null");

            string extension = Path.GetExtension(filePath);
            if (string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase))
            {
                csharpFilePaths.Add(filePath);
                return;
            }

            if (string.Equals(extension, ".asmdef", StringComparison.OrdinalIgnoreCase))
            {
                asmdefFilePaths.Add(filePath);
                return;
            }

            if (string.Equals(extension, ".asmref", StringComparison.OrdinalIgnoreCase))
            {
                asmrefFilePaths.Add(filePath);
            }
        }

        internal static bool ShouldExcludeDirectory(string projectRoot, string directoryPath)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(directoryPath), "directoryPath must not be null or empty");

            if (IsProjectRootPackagesDirectory(projectRoot, directoryPath))
            {
                return true;
            }

            string directoryName = Path.GetFileName(directoryPath);
            return ThirdPartyToolMigrationRules.IsExcludedDirectoryName(directoryName);
        }

        private static bool IsProjectRootPackagesDirectory(string projectRoot, string directoryPath)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(directoryPath), "directoryPath must not be null or empty");

            string packagesDirectory = Path.Combine(projectRoot, "Packages");
            string normalizedPackagesDirectory = NormalizeDirectoryPath(packagesDirectory);
            string normalizedDirectoryPath = NormalizeDirectoryPath(directoryPath);
            return string.Equals(
                normalizedDirectoryPath,
                normalizedPackagesDirectory,
                StringComparison.Ordinal);
        }

        private static string NormalizeDirectoryPath(string directoryPath)
        {
            Debug.Assert(!string.IsNullOrEmpty(directoryPath), "directoryPath must not be null or empty");

            return Path.GetFullPath(directoryPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
