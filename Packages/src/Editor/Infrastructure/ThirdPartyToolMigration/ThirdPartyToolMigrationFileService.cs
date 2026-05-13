using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Scans Unity project files and rewrites V2 custom tool source to the V3 public contract API.
    /// </summary>
    public sealed class ThirdPartyToolMigrationFileService : IThirdPartyToolMigrationPort
    {
        public ThirdPartyToolMigrationPreview PreviewMigration(string projectRoot)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            MigrationPlan plan = CreateMigrationPlan(projectRoot);
            return new ThirdPartyToolMigrationPreview(
                plan.ChangedFilePaths.Count,
                plan.ReplacementCount,
                plan.ChangedFilePaths.ToArray());
        }

        public ThirdPartyToolMigrationResult ApplyMigration(string projectRoot)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            MigrationPlan plan = CreateMigrationPlan(projectRoot);
            foreach (MigrationFileChange change in plan.Changes)
            {
                AtomicFileWriter.Write(change.FilePath, change.Content);
                AtomicFileWriter.CleanupBackup(change.FilePath + ".bak");
            }

            return new ThirdPartyToolMigrationResult(
                plan.ChangedFilePaths.Count,
                plan.ReplacementCount,
                plan.ChangedFilePaths.ToArray());
        }

        private static MigrationPlan CreateMigrationPlan(string projectRoot)
        {
            if (!Directory.Exists(projectRoot))
            {
                throw new DirectoryNotFoundException(projectRoot);
            }

            ProjectFileInventory inventory = ProjectFileInventory.Create(projectRoot);
            MigrationAssemblyUsage assemblyUsage = FindMigrationAssemblyUsage(
                inventory.CSharpFilePaths,
                inventory.AsmdefFilePaths);
            List<MigrationFileChange> changes = new();
            int replacementCount = 0;

            foreach (string csharpFilePath in inventory.CSharpFilePaths)
            {
                string source = File.ReadAllText(csharpFilePath);
                ThirdPartyToolMigrationContentResult result =
                    ThirdPartyToolMigrationRules.MigrateCSharpSource(source);
                if (!result.Changed)
                {
                    continue;
                }

                replacementCount += result.ReplacementCount;
                changes.Add(new MigrationFileChange(csharpFilePath, result.Content));
            }

            foreach (string asmdefFilePath in inventory.AsmdefFilePaths)
            {
                string source = File.ReadAllText(asmdefFilePath);
                string asmdefDirectory = Path.GetDirectoryName(asmdefFilePath) ?? projectRoot;
                bool hasLegacyCSharpSource = assemblyUsage.LegacyAssemblyDirectories.Contains(asmdefDirectory);
                bool requiresApplicationReference =
                    assemblyUsage.ApplicationReferenceAssemblyDirectories.Contains(asmdefDirectory);
                ThirdPartyToolMigrationContentResult result =
                    ThirdPartyToolMigrationRules.MigrateAsmdefSource(
                        source,
                        hasLegacyCSharpSource,
                        requiresApplicationReference);
                if (!result.Changed)
                {
                    continue;
                }

                replacementCount += result.ReplacementCount;
                changes.Add(new MigrationFileChange(asmdefFilePath, result.Content));
            }

            return new MigrationPlan(changes, replacementCount);
        }

        private static MigrationAssemblyUsage FindMigrationAssemblyUsage(
            List<string> csharpFilePaths,
            List<string> asmdefFilePaths)
        {
            Debug.Assert(csharpFilePaths != null, "csharpFilePaths must not be null");
            Debug.Assert(asmdefFilePaths != null, "asmdefFilePaths must not be null");

            List<string> asmdefDirectories = asmdefFilePaths
                .Select(path => Path.GetDirectoryName(path) ?? string.Empty)
                .Where(path => !string.IsNullOrEmpty(path))
                .OrderByDescending(path => path.Length)
                .ToList();
            HashSet<string> legacyAssemblyDirectories = new(StringComparer.Ordinal);
            HashSet<string> applicationReferenceAssemblyDirectories = new(StringComparer.Ordinal);

            foreach (string csharpFilePath in csharpFilePaths)
            {
                string source = File.ReadAllText(csharpFilePath);
                if (!ThirdPartyToolMigrationRules.ContainsLegacyCSharpApi(source))
                {
                    continue;
                }

                string assemblyDirectory = FindNearestAssemblyDirectory(csharpFilePath, asmdefDirectories);
                if (!string.IsNullOrEmpty(assemblyDirectory))
                {
                    legacyAssemblyDirectories.Add(assemblyDirectory);
                    if (ThirdPartyToolMigrationRules.ContainsLegacyRegistrarApi(source))
                    {
                        applicationReferenceAssemblyDirectories.Add(assemblyDirectory);
                    }
                }
            }

            return new MigrationAssemblyUsage(
                legacyAssemblyDirectories,
                applicationReferenceAssemblyDirectories);
        }

        private static string FindNearestAssemblyDirectory(
            string csharpFilePath,
            List<string> asmdefDirectories)
        {
            Debug.Assert(!string.IsNullOrEmpty(csharpFilePath), "csharpFilePath must not be null or empty");
            Debug.Assert(asmdefDirectories != null, "asmdefDirectories must not be null");

            string csharpDirectory = Path.GetDirectoryName(csharpFilePath) ?? string.Empty;
            foreach (string asmdefDirectory in asmdefDirectories)
            {
                if (IsSameOrChildPath(csharpDirectory, asmdefDirectory))
                {
                    return asmdefDirectory;
                }
            }

            return string.Empty;
        }

        private static bool IsSameOrChildPath(string childPath, string parentPath)
        {
            Debug.Assert(childPath != null, "childPath must not be null");
            Debug.Assert(parentPath != null, "parentPath must not be null");

            if (string.Equals(childPath, parentPath, StringComparison.Ordinal))
            {
                return true;
            }

            string parentWithSeparator = parentPath.TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return childPath.StartsWith(parentWithSeparator, StringComparison.Ordinal);
        }

        private readonly struct MigrationFileChange
        {
            public MigrationFileChange(string filePath, string content)
            {
                Debug.Assert(!string.IsNullOrEmpty(filePath), "filePath must not be null or empty");
                Debug.Assert(content != null, "content must not be null");

                FilePath = filePath;
                Content = content ?? string.Empty;
            }

            public string FilePath { get; }
            public string Content { get; }
        }

        private readonly struct MigrationPlan
        {
            public MigrationPlan(List<MigrationFileChange> changes, int replacementCount)
            {
                Debug.Assert(changes != null, "changes must not be null");
                Debug.Assert(replacementCount >= 0, "replacementCount must not be negative");

                Changes = changes ?? new List<MigrationFileChange>();
                ReplacementCount = replacementCount;
                ChangedFilePaths = Changes
                    .Select(change => change.FilePath)
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .ToList();
            }

            public List<MigrationFileChange> Changes { get; }
            public int ReplacementCount { get; }
            public List<string> ChangedFilePaths { get; }
        }

        private readonly struct MigrationAssemblyUsage
        {
            public MigrationAssemblyUsage(
                HashSet<string> legacyAssemblyDirectories,
                HashSet<string> applicationReferenceAssemblyDirectories)
            {
                Debug.Assert(legacyAssemblyDirectories != null, "legacyAssemblyDirectories must not be null");
                Debug.Assert(
                    applicationReferenceAssemblyDirectories != null,
                    "applicationReferenceAssemblyDirectories must not be null");

                LegacyAssemblyDirectories = legacyAssemblyDirectories ?? new HashSet<string>(StringComparer.Ordinal);
                ApplicationReferenceAssemblyDirectories = applicationReferenceAssemblyDirectories ??
                    new HashSet<string>(StringComparer.Ordinal);
            }

            public HashSet<string> LegacyAssemblyDirectories { get; }
            public HashSet<string> ApplicationReferenceAssemblyDirectories { get; }
        }

        private sealed class ProjectFileInventory
        {
            private ProjectFileInventory(List<string> csharpFilePaths, List<string> asmdefFilePaths)
            {
                CSharpFilePaths = csharpFilePaths;
                AsmdefFilePaths = asmdefFilePaths;
            }

            public List<string> CSharpFilePaths { get; }
            public List<string> AsmdefFilePaths { get; }

            public static ProjectFileInventory Create(string projectRoot)
            {
                Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

                List<string> csharpFilePaths = new();
                List<string> asmdefFilePaths = new();
                CollectCandidateFiles(projectRoot, csharpFilePaths, asmdefFilePaths);
                csharpFilePaths.Sort(StringComparer.Ordinal);
                asmdefFilePaths.Sort(StringComparer.Ordinal);
                return new ProjectFileInventory(csharpFilePaths, asmdefFilePaths);
            }

            private static void CollectCandidateFiles(
                string directoryPath,
                List<string> csharpFilePaths,
                List<string> asmdefFilePaths)
            {
                Debug.Assert(!string.IsNullOrEmpty(directoryPath), "directoryPath must not be null or empty");
                Debug.Assert(csharpFilePaths != null, "csharpFilePaths must not be null");
                Debug.Assert(asmdefFilePaths != null, "asmdefFilePaths must not be null");

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
                    }
                }

                foreach (string childDirectoryPath in Directory.EnumerateDirectories(directoryPath))
                {
                    string directoryName = Path.GetFileName(childDirectoryPath);
                    if (ThirdPartyToolMigrationRules.IsExcludedDirectoryName(directoryName))
                    {
                        continue;
                    }

                    CollectCandidateFiles(childDirectoryPath, csharpFilePaths, asmdefFilePaths);
                }
            }
        }
    }
}
