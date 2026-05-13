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
        private const string ImplicitEditorAssemblyDirectoryName = "__UnityCliLoopImplicitEditorAssembly";
        private const string ImplicitRuntimeAssemblyDirectoryName = "__UnityCliLoopImplicitRuntimeAssembly";

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
                WriteMigrationFile(change.FilePath, change.Content);
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
                projectRoot,
                inventory.CSharpFilePaths,
                inventory.AsmdefFilePaths);
            List<MigrationFileChange> changes = new();
            int replacementCount = 0;

            foreach (string csharpFilePath in inventory.CSharpFilePaths)
            {
                string source = File.ReadAllText(csharpFilePath);
                string assemblyDirectory = FindNearestAssemblyDirectory(
                    csharpFilePath,
                    assemblyUsage.AsmdefDirectories,
                    projectRoot);
                bool hasLegacyAssemblySource =
                    assemblyUsage.LegacyAssemblyDirectories.Contains(assemblyDirectory);
                ThirdPartyToolMigrationContentResult result =
                    ThirdPartyToolMigrationRules.MigrateCSharpSourceForLegacyAssembly(
                        source,
                        hasLegacyAssemblySource);
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
                bool requiresDomainReference =
                    assemblyUsage.DomainReferenceAssemblyDirectories.Contains(asmdefDirectory) ||
                    requiresApplicationReference;
                ThirdPartyToolMigrationContentResult result =
                    ThirdPartyToolMigrationRules.MigrateAsmdefSource(
                        source,
                        hasLegacyCSharpSource,
                        requiresApplicationReference,
                        requiresDomainReference);
                if (!result.Changed)
                {
                    continue;
                }

                replacementCount += result.ReplacementCount;
                changes.Add(new MigrationFileChange(asmdefFilePath, result.Content));
            }

            return new MigrationPlan(changes, replacementCount);
        }

        private static void WriteMigrationFile(string filePath, string content)
        {
            Debug.Assert(!string.IsNullOrEmpty(filePath), "filePath must not be null or empty");
            Debug.Assert(content != null, "content must not be null");

            string tempFilePath = CreateUniqueSidecarPath(filePath, ".tmp");
            File.WriteAllText(tempFilePath, content);
            if (!File.Exists(filePath))
            {
                File.Move(tempFilePath, filePath);
                return;
            }

            string backupFilePath = CreateUniqueSidecarPath(filePath, ".bak");
            File.Replace(tempFilePath, filePath, backupFilePath);
            File.Delete(backupFilePath);
        }

        private static string CreateUniqueSidecarPath(string filePath, string extension)
        {
            Debug.Assert(!string.IsNullOrEmpty(filePath), "filePath must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(extension), "extension must not be null or empty");

            string directory = Path.GetDirectoryName(filePath) ?? string.Empty;
            string fileName = Path.GetFileName(filePath);
            string sidecarPath = Path.Combine(directory, $"{fileName}.{Guid.NewGuid():N}{extension}");
            while (File.Exists(sidecarPath))
            {
                sidecarPath = Path.Combine(directory, $"{fileName}.{Guid.NewGuid():N}{extension}");
            }

            return sidecarPath;
        }

        private static MigrationAssemblyUsage FindMigrationAssemblyUsage(
            string projectRoot,
            List<string> csharpFilePaths,
            List<string> asmdefFilePaths)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            Debug.Assert(csharpFilePaths != null, "csharpFilePaths must not be null");
            Debug.Assert(asmdefFilePaths != null, "asmdefFilePaths must not be null");

            List<string> asmdefDirectories = asmdefFilePaths
                .Select(path => Path.GetDirectoryName(path) ?? string.Empty)
                .Where(path => !string.IsNullOrEmpty(path))
                .OrderByDescending(path => path.Length)
                .ToList();
            HashSet<string> legacyAssemblyDirectories = new(StringComparer.Ordinal);
            HashSet<string> registrarAssemblyDirectories = new(StringComparer.Ordinal);
            HashSet<string> domainMetadataAssemblyDirectories = new(StringComparer.Ordinal);
            HashSet<string> applicationReferenceAssemblyDirectories = new(StringComparer.Ordinal);
            HashSet<string> domainReferenceAssemblyDirectories = new(StringComparer.Ordinal);

            foreach (string csharpFilePath in csharpFilePaths)
            {
                string source = File.ReadAllText(csharpFilePath);
                string assemblyDirectory = FindNearestAssemblyDirectory(
                    csharpFilePath,
                    asmdefDirectories,
                    projectRoot);

                if (ThirdPartyToolMigrationRules.ContainsLegacyCSharpApi(source))
                {
                    legacyAssemblyDirectories.Add(assemblyDirectory);
                }

                if (ThirdPartyToolMigrationRules.ContainsLegacyRegistrarApi(source))
                {
                    registrarAssemblyDirectories.Add(assemblyDirectory);
                }

                if (ThirdPartyToolMigrationRules.ContainsLegacyDomainMetadataApi(source))
                {
                    domainMetadataAssemblyDirectories.Add(assemblyDirectory);
                }
            }

            foreach (string registrarAssemblyDirectory in registrarAssemblyDirectories)
            {
                if (legacyAssemblyDirectories.Contains(registrarAssemblyDirectory))
                {
                    applicationReferenceAssemblyDirectories.Add(registrarAssemblyDirectory);
                }
            }

            foreach (string domainMetadataAssemblyDirectory in domainMetadataAssemblyDirectories)
            {
                if (legacyAssemblyDirectories.Contains(domainMetadataAssemblyDirectory))
                {
                    domainReferenceAssemblyDirectories.Add(domainMetadataAssemblyDirectory);
                }
            }

            return new MigrationAssemblyUsage(
                asmdefDirectories,
                legacyAssemblyDirectories,
                applicationReferenceAssemblyDirectories,
                domainReferenceAssemblyDirectories);
        }

        private static string FindNearestAssemblyDirectory(
            string csharpFilePath,
            List<string> asmdefDirectories,
            string projectRoot)
        {
            Debug.Assert(!string.IsNullOrEmpty(csharpFilePath), "csharpFilePath must not be null or empty");
            Debug.Assert(asmdefDirectories != null, "asmdefDirectories must not be null");
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            string csharpDirectory = Path.GetDirectoryName(csharpFilePath) ?? string.Empty;
            foreach (string asmdefDirectory in asmdefDirectories)
            {
                if (IsSameOrChildPath(csharpDirectory, asmdefDirectory))
                {
                    return asmdefDirectory;
                }
            }

            return GetImplicitAssemblyDirectory(csharpFilePath, projectRoot);
        }

        private static string GetImplicitAssemblyDirectory(string csharpFilePath, string projectRoot)
        {
            Debug.Assert(!string.IsNullOrEmpty(csharpFilePath), "csharpFilePath must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            string implicitAssemblyDirectoryName = IsEditorAssemblyPath(csharpFilePath, projectRoot)
                ? ImplicitEditorAssemblyDirectoryName
                : ImplicitRuntimeAssemblyDirectoryName;
            return Path.Combine(projectRoot, implicitAssemblyDirectoryName);
        }

        private static bool IsEditorAssemblyPath(string csharpFilePath, string projectRoot)
        {
            Debug.Assert(!string.IsNullOrEmpty(csharpFilePath), "csharpFilePath must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            string relativePath = csharpFilePath.StartsWith(projectRoot, StringComparison.Ordinal)
                ? csharpFilePath.Substring(projectRoot.Length)
                : csharpFilePath;
            char[] separators =
            {
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar
            };
            string[] pathSegments = relativePath.Split(separators, StringSplitOptions.RemoveEmptyEntries);
            return pathSegments.Any(
                pathSegment => string.Equals(pathSegment, "Editor", StringComparison.OrdinalIgnoreCase));
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
                List<string> asmdefDirectories,
                HashSet<string> legacyAssemblyDirectories,
                HashSet<string> applicationReferenceAssemblyDirectories,
                HashSet<string> domainReferenceAssemblyDirectories)
            {
                Debug.Assert(asmdefDirectories != null, "asmdefDirectories must not be null");
                Debug.Assert(legacyAssemblyDirectories != null, "legacyAssemblyDirectories must not be null");
                Debug.Assert(
                    applicationReferenceAssemblyDirectories != null,
                    "applicationReferenceAssemblyDirectories must not be null");
                Debug.Assert(
                    domainReferenceAssemblyDirectories != null,
                    "domainReferenceAssemblyDirectories must not be null");

                AsmdefDirectories = asmdefDirectories ?? new List<string>();
                LegacyAssemblyDirectories = legacyAssemblyDirectories ?? new HashSet<string>(StringComparer.Ordinal);
                ApplicationReferenceAssemblyDirectories = applicationReferenceAssemblyDirectories ??
                    new HashSet<string>(StringComparer.Ordinal);
                DomainReferenceAssemblyDirectories = domainReferenceAssemblyDirectories ??
                    new HashSet<string>(StringComparer.Ordinal);
            }

            public List<string> AsmdefDirectories { get; }
            public HashSet<string> LegacyAssemblyDirectories { get; }
            public HashSet<string> ApplicationReferenceAssemblyDirectories { get; }
            public HashSet<string> DomainReferenceAssemblyDirectories { get; }
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
