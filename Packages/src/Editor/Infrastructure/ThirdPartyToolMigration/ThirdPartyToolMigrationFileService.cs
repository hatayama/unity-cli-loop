using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

using Newtonsoft.Json.Linq;

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
                inventory.AsmdefFilePaths,
                inventory.AsmrefFilePaths);
            List<MigrationFileChange> changes = new();
            int replacementCount = 0;

            foreach (string csharpFilePath in inventory.CSharpFilePaths)
            {
                string source = File.ReadAllText(csharpFilePath);
                string assemblyDirectory = FindNearestAssemblyDirectory(
                    csharpFilePath,
                    assemblyUsage.AsmdefDirectories,
                    assemblyUsage.AssemblyReferenceDirectories,
                    projectRoot);
                bool hasLegacyAssemblySource =
                    assemblyUsage.AssemblyScopedLegacyDirectories.Contains(assemblyDirectory) &&
                    ThirdPartyToolMigrationRules.ContainsLegacyAssemblyScopedApi(source);
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
            HashSet<string> legacyAssemblyDirectories = new(StringComparer.Ordinal);
            HashSet<string> assemblyScopedLegacyDirectories = new(StringComparer.Ordinal);
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
                    assemblyReferenceDirectories,
                    projectRoot);

                if (ThirdPartyToolMigrationRules.ContainsLegacyCSharpApi(source))
                {
                    legacyAssemblyDirectories.Add(assemblyDirectory);
                }

                if (ThirdPartyToolMigrationRules.ContainsLegacyGlobalUsing(source))
                {
                    assemblyScopedLegacyDirectories.Add(assemblyDirectory);
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
                assemblyReferenceDirectories,
                legacyAssemblyDirectories,
                assemblyScopedLegacyDirectories,
                applicationReferenceAssemblyDirectories,
                domainReferenceAssemblyDirectories);
        }

        private static List<AssemblyReferenceDirectory> CreateAssemblyReferenceDirectories(
            List<string> asmdefFilePaths,
            List<string> asmrefFilePaths)
        {
            Debug.Assert(asmdefFilePaths != null, "asmdefFilePaths must not be null");
            Debug.Assert(asmrefFilePaths != null, "asmrefFilePaths must not be null");

            Dictionary<string, string> asmdefDirectoriesByReference = CreateAsmdefDirectoryMap(asmdefFilePaths);
            List<AssemblyReferenceDirectory> assemblyReferenceDirectories = new();
            foreach (string asmrefFilePath in asmrefFilePaths)
            {
                JObject asmref = JObject.Parse(File.ReadAllText(asmrefFilePath));
                string reference = asmref["reference"]?.Value<string>() ?? string.Empty;
                if (reference.Length == 0)
                {
                    continue;
                }

                if (!asmdefDirectoriesByReference.TryGetValue(reference, out string targetAssemblyDirectory))
                {
                    continue;
                }

                string sourceDirectory = Path.GetDirectoryName(asmrefFilePath) ?? string.Empty;
                if (sourceDirectory.Length == 0)
                {
                    continue;
                }

                assemblyReferenceDirectories.Add(
                    new AssemblyReferenceDirectory(sourceDirectory, targetAssemblyDirectory));
            }

            return assemblyReferenceDirectories
                .OrderByDescending(assemblyReferenceDirectory => assemblyReferenceDirectory.SourceDirectory.Length)
                .ToList();
        }

        private static Dictionary<string, string> CreateAsmdefDirectoryMap(List<string> asmdefFilePaths)
        {
            Debug.Assert(asmdefFilePaths != null, "asmdefFilePaths must not be null");

            Dictionary<string, string> asmdefDirectoriesByReference = new(StringComparer.Ordinal);
            foreach (string asmdefFilePath in asmdefFilePaths)
            {
                string asmdefDirectory = Path.GetDirectoryName(asmdefFilePath) ?? string.Empty;
                if (asmdefDirectory.Length == 0)
                {
                    continue;
                }

                JObject asmdef = JObject.Parse(File.ReadAllText(asmdefFilePath));
                string assemblyName = asmdef["name"]?.Value<string>() ?? string.Empty;
                AddAsmdefDirectoryReference(asmdefDirectoriesByReference, assemblyName, asmdefDirectory);
                AddAsmdefDirectoryReference(
                    asmdefDirectoriesByReference,
                    ReadAsmdefGuidReference(asmdefFilePath),
                    asmdefDirectory);
            }

            return asmdefDirectoriesByReference;
        }

        private static void AddAsmdefDirectoryReference(
            Dictionary<string, string> asmdefDirectoriesByReference,
            string reference,
            string asmdefDirectory)
        {
            Debug.Assert(asmdefDirectoriesByReference != null, "asmdefDirectoriesByReference must not be null");
            Debug.Assert(reference != null, "reference must not be null");
            Debug.Assert(!string.IsNullOrEmpty(asmdefDirectory), "asmdefDirectory must not be null or empty");

            if (reference.Length == 0 || asmdefDirectoriesByReference.ContainsKey(reference))
            {
                return;
            }

            asmdefDirectoriesByReference.Add(reference, asmdefDirectory);
        }

        private static string ReadAsmdefGuidReference(string asmdefFilePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(asmdefFilePath), "asmdefFilePath must not be null or empty");

            string metaPath = asmdefFilePath + ".meta";
            if (!File.Exists(metaPath))
            {
                return string.Empty;
            }

            foreach (string line in File.ReadLines(metaPath))
            {
                string trimmedLine = line.Trim();
                if (!trimmedLine.StartsWith("guid:", StringComparison.Ordinal))
                {
                    continue;
                }

                string guid = trimmedLine.Substring("guid:".Length).Trim();
                return guid.Length == 0 ? string.Empty : $"GUID:{guid}";
            }

            return string.Empty;
        }

        private static string FindNearestAssemblyDirectory(
            string csharpFilePath,
            List<string> asmdefDirectories,
            List<AssemblyReferenceDirectory> assemblyReferenceDirectories,
            string projectRoot)
        {
            Debug.Assert(!string.IsNullOrEmpty(csharpFilePath), "csharpFilePath must not be null or empty");
            Debug.Assert(asmdefDirectories != null, "asmdefDirectories must not be null");
            Debug.Assert(
                assemblyReferenceDirectories != null,
                "assemblyReferenceDirectories must not be null");
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            string csharpDirectory = Path.GetDirectoryName(csharpFilePath) ?? string.Empty;
            string matchedAssemblyDirectory = string.Empty;
            int matchedSourceDirectoryLength = -1;
            foreach (string asmdefDirectory in asmdefDirectories)
            {
                if (!IsSameOrChildPath(csharpDirectory, asmdefDirectory) ||
                    asmdefDirectory.Length <= matchedSourceDirectoryLength)
                {
                    continue;
                }

                matchedAssemblyDirectory = asmdefDirectory;
                matchedSourceDirectoryLength = asmdefDirectory.Length;
            }

            foreach (AssemblyReferenceDirectory assemblyReferenceDirectory in assemblyReferenceDirectories)
            {
                string sourceDirectory = assemblyReferenceDirectory.SourceDirectory;
                if (!IsSameOrChildPath(csharpDirectory, sourceDirectory) ||
                    sourceDirectory.Length <= matchedSourceDirectoryLength)
                {
                    continue;
                }

                matchedAssemblyDirectory = assemblyReferenceDirectory.TargetAssemblyDirectory;
                matchedSourceDirectoryLength = sourceDirectory.Length;
            }

            if (matchedAssemblyDirectory.Length > 0)
            {
                return matchedAssemblyDirectory;
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
                List<AssemblyReferenceDirectory> assemblyReferenceDirectories,
                HashSet<string> legacyAssemblyDirectories,
                HashSet<string> assemblyScopedLegacyDirectories,
                HashSet<string> applicationReferenceAssemblyDirectories,
                HashSet<string> domainReferenceAssemblyDirectories)
            {
                Debug.Assert(asmdefDirectories != null, "asmdefDirectories must not be null");
                Debug.Assert(
                    assemblyReferenceDirectories != null,
                    "assemblyReferenceDirectories must not be null");
                Debug.Assert(legacyAssemblyDirectories != null, "legacyAssemblyDirectories must not be null");
                Debug.Assert(
                    assemblyScopedLegacyDirectories != null,
                    "assemblyScopedLegacyDirectories must not be null");
                Debug.Assert(
                    applicationReferenceAssemblyDirectories != null,
                    "applicationReferenceAssemblyDirectories must not be null");
                Debug.Assert(
                    domainReferenceAssemblyDirectories != null,
                    "domainReferenceAssemblyDirectories must not be null");

                AsmdefDirectories = asmdefDirectories ?? new List<string>();
                AssemblyReferenceDirectories = assemblyReferenceDirectories ?? new List<AssemblyReferenceDirectory>();
                LegacyAssemblyDirectories = legacyAssemblyDirectories ?? new HashSet<string>(StringComparer.Ordinal);
                AssemblyScopedLegacyDirectories = assemblyScopedLegacyDirectories ??
                    new HashSet<string>(StringComparer.Ordinal);
                ApplicationReferenceAssemblyDirectories = applicationReferenceAssemblyDirectories ??
                    new HashSet<string>(StringComparer.Ordinal);
                DomainReferenceAssemblyDirectories = domainReferenceAssemblyDirectories ??
                    new HashSet<string>(StringComparer.Ordinal);
            }

            public List<string> AsmdefDirectories { get; }
            public List<AssemblyReferenceDirectory> AssemblyReferenceDirectories { get; }
            public HashSet<string> LegacyAssemblyDirectories { get; }
            public HashSet<string> AssemblyScopedLegacyDirectories { get; }
            public HashSet<string> ApplicationReferenceAssemblyDirectories { get; }
            public HashSet<string> DomainReferenceAssemblyDirectories { get; }
        }

        private readonly struct AssemblyReferenceDirectory
        {
            public AssemblyReferenceDirectory(string sourceDirectory, string targetAssemblyDirectory)
            {
                Debug.Assert(!string.IsNullOrEmpty(sourceDirectory), "sourceDirectory must not be null or empty");
                Debug.Assert(
                    !string.IsNullOrEmpty(targetAssemblyDirectory),
                    "targetAssemblyDirectory must not be null or empty");

                SourceDirectory = sourceDirectory;
                TargetAssemblyDirectory = targetAssemblyDirectory;
            }

            public string SourceDirectory { get; }
            public string TargetAssemblyDirectory { get; }
        }

        private sealed class ProjectFileInventory
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
                CollectCandidateFiles(projectRoot, csharpFilePaths, asmdefFilePaths, asmrefFilePaths);
                csharpFilePaths.Sort(StringComparer.Ordinal);
                asmdefFilePaths.Sort(StringComparer.Ordinal);
                asmrefFilePaths.Sort(StringComparer.Ordinal);
                return new ProjectFileInventory(csharpFilePaths, asmdefFilePaths, asmrefFilePaths);
            }

            private static void CollectCandidateFiles(
                string directoryPath,
                List<string> csharpFilePaths,
                List<string> asmdefFilePaths,
                List<string> asmrefFilePaths)
            {
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
                    string directoryName = Path.GetFileName(childDirectoryPath);
                    if (ThirdPartyToolMigrationRules.IsExcludedDirectoryName(directoryName))
                    {
                        continue;
                    }

                    CollectCandidateFiles(childDirectoryPath, csharpFilePaths, asmdefFilePaths, asmrefFilePaths);
                }
            }
        }
    }
}
