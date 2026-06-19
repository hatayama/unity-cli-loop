using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Caches source text within one preview pass so analysis phases share a single disk read per file.
    /// </summary>
    internal sealed class ThirdPartyToolMigrationSourceFileCache
    {
        private readonly Func<string, string> _readAllText;
        private readonly Dictionary<string, string> _sources = new(StringComparer.Ordinal);

        internal ThirdPartyToolMigrationSourceFileCache()
            : this(File.ReadAllText)
        {
        }

        internal ThirdPartyToolMigrationSourceFileCache(Func<string, string> readAllText)
        {
            Debug.Assert(readAllText != null, "readAllText must not be null");

            _readAllText = readAllText ?? throw new ArgumentNullException(nameof(readAllText));
        }

        internal string ReadAllText(string filePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(filePath), "filePath must not be null or empty");

            if (_sources.TryGetValue(filePath, out string source))
            {
                return source;
            }

            string loadedSource = _readAllText(filePath);
            _sources.Add(filePath, loadedSource);
            return loadedSource;
        }
    }

    /// <summary>
    /// Scans Unity project files and rewrites V2 custom tool source to the V3 public contract API.
    /// </summary>
    public sealed class ThirdPartyToolMigrationFileService : IThirdPartyToolMigrationPort
    {
        private const string ImplicitEditorAssemblyDirectoryName = "__UnityCliLoopImplicitEditorAssembly";
        private const string ImplicitRuntimeAssemblyDirectoryName = "__UnityCliLoopImplicitRuntimeAssembly";
        private const string ImplicitFirstPassEditorAssemblyDirectoryName =
            "__UnityCliLoopImplicitFirstPassEditorAssembly";
        private const string ImplicitFirstPassRuntimeAssemblyDirectoryName =
            "__UnityCliLoopImplicitFirstPassRuntimeAssembly";
        private const int PreviewYieldBatchSize = 32;

        private readonly object _previewCacheLock = new();
        private bool _hasCachedPreview;
        private string _cachedPreviewProjectRoot = string.Empty;
        private ThirdPartyToolMigrationPreview _cachedPreview;

        public ThirdPartyToolMigrationPreview PreviewMigration(string projectRoot)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            string normalizedProjectRoot = NormalizeProjectRoot(projectRoot);
            if (TryGetCachedPreview(normalizedProjectRoot, out ThirdPartyToolMigrationPreview cachedPreview))
            {
                return cachedPreview;
            }

            MigrationPlan plan = CreateMigrationPlan(normalizedProjectRoot);
            ThirdPartyToolMigrationPreview preview = new(
                plan.ChangedFilePaths.Count,
                plan.ReplacementCount,
                plan.ChangedFilePaths.ToArray());
            StoreCachedPreview(normalizedProjectRoot, preview);
            return preview;
        }

        public async Task<ThirdPartyToolMigrationPreview> PreviewMigrationAsync(
            string projectRoot,
            IProgress<ThirdPartyToolMigrationProgress> progress,
            CancellationToken ct)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            Debug.Assert(progress != null, "progress must not be null");

            string normalizedProjectRoot = NormalizeProjectRoot(projectRoot);
            MigrationPlan plan = await CreateMigrationPlanAsync(normalizedProjectRoot, progress, ct);
            if (ct.IsCancellationRequested)
            {
                return new ThirdPartyToolMigrationPreview(0, 0, Array.Empty<string>());
            }

            ThirdPartyToolMigrationPreview preview = new(
                plan.ChangedFilePaths.Count,
                plan.ReplacementCount,
                plan.ChangedFilePaths.ToArray());
            StoreCachedPreview(normalizedProjectRoot, preview);
            return preview;
        }

        public async Task<bool> HasMigrationTargetsAsync(string projectRoot, CancellationToken ct)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            string normalizedProjectRoot = NormalizeProjectRoot(projectRoot);
            if (!Directory.Exists(normalizedProjectRoot))
            {
                throw new DirectoryNotFoundException(normalizedProjectRoot);
            }

            if (!Directory.Exists(Path.Combine(normalizedProjectRoot, "Assets")))
            {
                return false;
            }

            return await HasMigrationTargetAsync(normalizedProjectRoot, ct);
        }

        public ThirdPartyToolMigrationResult ApplyMigration(string projectRoot)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            string normalizedProjectRoot = NormalizeProjectRoot(projectRoot);
            InvalidatePreviewCache();
            MigrationPlan plan = CreateMigrationPlan(normalizedProjectRoot);
            foreach (MigrationFileChange change in plan.Changes)
            {
                WriteMigrationFile(change.FilePath, change.Content);
            }

            return new ThirdPartyToolMigrationResult(
                plan.ChangedFilePaths.Count,
                plan.ReplacementCount,
                plan.ChangedFilePaths.ToArray());
        }

        public async Task<ThirdPartyToolMigrationResult> ApplyMigrationAsync(
            string projectRoot,
            IProgress<ThirdPartyToolMigrationProgress> progress,
            CancellationToken ct)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            Debug.Assert(progress != null, "progress must not be null");

            string normalizedProjectRoot = NormalizeProjectRoot(projectRoot);
            InvalidatePreviewCache();
            MigrationPlan plan = await CreateMigrationPlanAsync(normalizedProjectRoot, progress, ct);
            if (ct.IsCancellationRequested)
            {
                return new ThirdPartyToolMigrationResult(0, 0, Array.Empty<string>());
            }

            for (int index = 0; index < plan.Changes.Count; index++)
            {
                if (ct.IsCancellationRequested)
                {
                    return new ThirdPartyToolMigrationResult(0, 0, Array.Empty<string>());
                }

                MigrationFileChange change = plan.Changes[index];
                WriteMigrationFile(change.FilePath, change.Content);
                if ((index + 1) % PreviewYieldBatchSize == 0)
                {
                    await Task.Yield();
                }
            }

            return new ThirdPartyToolMigrationResult(
                plan.ChangedFilePaths.Count,
                plan.ReplacementCount,
                plan.ChangedFilePaths.ToArray());
        }

        internal void InvalidatePreviewCache()
        {
            lock (_previewCacheLock)
            {
                _hasCachedPreview = false;
                _cachedPreviewProjectRoot = string.Empty;
                _cachedPreview = default;
            }
        }

        private static async Task<bool> HasMigrationTargetAsync(string projectRoot, CancellationToken ct)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            ProjectFileInventory inventory = await ProjectFileInventory.CreateAsync(
                projectRoot,
                new Progress<ThirdPartyToolMigrationProgress>(),
                ct);
            if (ct.IsCancellationRequested)
            {
                return false;
            }

            List<string> asmdefDirectories = inventory.AsmdefFilePaths
                .Select(path => Path.GetDirectoryName(path) ?? string.Empty)
                .Where(path => !string.IsNullOrEmpty(path))
                .OrderByDescending(path => path.Length)
                .ToList();
            List<AssemblyReferenceDirectory> assemblyReferenceDirectories = inventory.AsmrefFilePaths.Count == 0
                ? new List<AssemblyReferenceDirectory>()
                : CreateAssemblyReferenceDirectories(inventory.AsmdefFilePaths, inventory.AsmrefFilePaths);
            HashSet<string> legacyAssemblyDirectories = new(StringComparer.Ordinal);
            HashSet<string> assemblyScopedCurrentDomainDirectories = new(StringComparer.Ordinal);
            HashSet<string> assemblyScopedCurrentFirstPartyToolsDirectories = new(StringComparer.Ordinal);
            HashSet<string> toolContractsReferenceAssemblyDirectories = new(StringComparer.Ordinal);
            HashSet<string> applicationReferenceAssemblyDirectories = new(StringComparer.Ordinal);
            HashSet<string> domainReferenceAssemblyDirectories = new(StringComparer.Ordinal);
            HashSet<string> firstPartyScreenshotReferenceAssemblyDirectories = new(StringComparer.Ordinal);
            int inspectedEntryCount = 0;
            foreach (string csharpFilePath in inventory.CSharpFilePaths)
            {
                if (ct.IsCancellationRequested)
                {
                    return false;
                }

                string source = File.ReadAllText(csharpFilePath);
                if (ContainsFastCSharpMigrationTarget(source))
                {
                    return true;
                }

                if (ThirdPartyToolMigrationRules.ContainsMigrationCandidateText(source))
                {
                    string assemblyDirectory = FindNearestAssemblyDirectory(
                        csharpFilePath,
                        asmdefDirectories,
                        assemblyReferenceDirectories,
                        projectRoot);
                    if (ThirdPartyToolMigrationRules.ContainsCurrentDomainGlobalUsing(source))
                    {
                        assemblyScopedCurrentDomainDirectories.Add(assemblyDirectory);
                    }

                    if (ThirdPartyToolMigrationRules.ContainsCurrentFirstPartyToolsGlobalUsing(source))
                    {
                        assemblyScopedCurrentFirstPartyToolsDirectories.Add(assemblyDirectory);
                    }

                    CollectFastAssemblyReferenceRequirements(
                        source,
                        assemblyDirectory,
                        assemblyScopedCurrentDomainDirectories.Contains(assemblyDirectory),
                        assemblyScopedCurrentFirstPartyToolsDirectories.Contains(assemblyDirectory),
                        legacyAssemblyDirectories,
                        toolContractsReferenceAssemblyDirectories,
                        applicationReferenceAssemblyDirectories,
                        domainReferenceAssemblyDirectories,
                        firstPartyScreenshotReferenceAssemblyDirectories);
                }

                inspectedEntryCount++;
                if (inspectedEntryCount % PreviewYieldBatchSize == 0)
                {
                    await Task.Yield();
                }
            }

            if (assemblyScopedCurrentDomainDirectories.Count > 0)
            {
                await CollectFastAssemblyScopedCurrentDomainRequirementsAsync(
                    inventory.CSharpFilePaths,
                    asmdefDirectories,
                    assemblyReferenceDirectories,
                    projectRoot,
                    assemblyScopedCurrentDomainDirectories,
                    domainReferenceAssemblyDirectories,
                    ct);
                if (ct.IsCancellationRequested)
                {
                    return false;
                }
            }

            if (assemblyScopedCurrentFirstPartyToolsDirectories.Count > 0)
            {
                await CollectFastAssemblyScopedCurrentFirstPartyToolsRequirementsAsync(
                    inventory.CSharpFilePaths,
                    asmdefDirectories,
                    assemblyReferenceDirectories,
                    projectRoot,
                    assemblyScopedCurrentFirstPartyToolsDirectories,
                    firstPartyScreenshotReferenceAssemblyDirectories,
                    ct);
                if (ct.IsCancellationRequested)
                {
                    return false;
                }
            }

            foreach (string asmdefFilePath in inventory.AsmdefFilePaths)
            {
                if (ct.IsCancellationRequested)
                {
                    return false;
                }

                string source = File.ReadAllText(asmdefFilePath);
                if (ContainsFastAsmdefMigrationTarget(source))
                {
                    return true;
                }

                inspectedEntryCount++;
                if (inspectedEntryCount % PreviewYieldBatchSize == 0)
                {
                    await Task.Yield();
                }
            }

            if (toolContractsReferenceAssemblyDirectories.Count == 0 &&
                applicationReferenceAssemblyDirectories.Count == 0 &&
                domainReferenceAssemblyDirectories.Count == 0 &&
                firstPartyScreenshotReferenceAssemblyDirectories.Count == 0)
            {
                return false;
            }

            MigrationAssemblyUsage assemblyUsage = new(
                asmdefDirectories,
                assemblyReferenceDirectories,
                legacyAssemblyDirectories,
                new HashSet<string>(StringComparer.Ordinal),
                new Dictionary<string, string[]>(StringComparer.Ordinal),
                new Dictionary<string, string[]>(StringComparer.Ordinal),
                toolContractsReferenceAssemblyDirectories,
                applicationReferenceAssemblyDirectories,
                domainReferenceAssemblyDirectories,
                firstPartyScreenshotReferenceAssemblyDirectories);

            foreach (string asmdefFilePath in inventory.AsmdefFilePaths)
            {
                if (ct.IsCancellationRequested)
                {
                    return false;
                }

                if (ContainsAsmdefMigrationTarget(asmdefFilePath, projectRoot, assemblyUsage))
                {
                    return true;
                }
            }

            return false;
        }

        private static void CollectFastAssemblyReferenceRequirements(
            string source,
            string assemblyDirectory,
            bool hasAssemblyScopedCurrentDomainNamespaceUsage,
            bool hasAssemblyScopedCurrentFirstPartyToolsNamespaceUsage,
            HashSet<string> legacyAssemblyDirectories,
            HashSet<string> toolContractsReferenceAssemblyDirectories,
            HashSet<string> applicationReferenceAssemblyDirectories,
            HashSet<string> domainReferenceAssemblyDirectories,
            HashSet<string> firstPartyScreenshotReferenceAssemblyDirectories)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(!string.IsNullOrEmpty(assemblyDirectory), "assemblyDirectory must not be null or empty");
            Debug.Assert(legacyAssemblyDirectories != null, "legacyAssemblyDirectories must not be null");
            Debug.Assert(
                toolContractsReferenceAssemblyDirectories != null,
                "toolContractsReferenceAssemblyDirectories must not be null");
            Debug.Assert(
                applicationReferenceAssemblyDirectories != null,
                "applicationReferenceAssemblyDirectories must not be null");
            Debug.Assert(domainReferenceAssemblyDirectories != null, "domainReferenceAssemblyDirectories must not be null");
            Debug.Assert(
                firstPartyScreenshotReferenceAssemblyDirectories != null,
                "firstPartyScreenshotReferenceAssemblyDirectories must not be null");

            if (ThirdPartyToolMigrationRules.ContainsLegacyCSharpApi(source))
            {
                legacyAssemblyDirectories.Add(assemblyDirectory);
            }

            if (ThirdPartyToolMigrationRules.ContainsCurrentToolContractsApi(source))
            {
                toolContractsReferenceAssemblyDirectories.Add(assemblyDirectory);
            }

            if (ThirdPartyToolMigrationRules.ContainsLegacyRegistrarApi(source) ||
                ThirdPartyToolMigrationRules.ContainsCurrentRegistrarApi(source) ||
                ThirdPartyToolMigrationRules.ContainsLegacyApplicationApiForAssembly(
                    source,
                    legacyAssemblyDirectories.Contains(assemblyDirectory) ||
                    ThirdPartyToolMigrationRules.ContainsCurrentToolContractsApi(source),
                    Array.Empty<string>()) ||
                ThirdPartyToolMigrationRules.ContainsCurrentApplicationApi(source))
            {
                applicationReferenceAssemblyDirectories.Add(assemblyDirectory);
            }

            if (ThirdPartyToolMigrationRules.ContainsRegistrarDomainReturnApi(source))
            {
                domainReferenceAssemblyDirectories.Add(assemblyDirectory);
            }

            if (ThirdPartyToolMigrationRules.ContainsCurrentDomainMetadataApi(source) ||
                ThirdPartyToolMigrationRules.ContainsCurrentDomainMetadataApiForAssembly(
                    source,
                    hasAssemblyScopedCurrentDomainNamespaceUsage))
            {
                domainReferenceAssemblyDirectories.Add(assemblyDirectory);
            }

            if (ThirdPartyToolMigrationRules.ContainsLegacyFirstPartyScreenshotApiForAssembly(
                    source,
                    legacyAssemblyDirectories.Contains(assemblyDirectory) ||
                    ThirdPartyToolMigrationRules.ContainsCurrentToolContractsApi(source),
                    Array.Empty<string>()) ||
                ThirdPartyToolMigrationRules.ContainsCurrentFirstPartyScreenshotApi(source) ||
                ThirdPartyToolMigrationRules.ContainsCurrentFirstPartyScreenshotApiForAssembly(
                    source,
                    hasAssemblyScopedCurrentFirstPartyToolsNamespaceUsage))
            {
                firstPartyScreenshotReferenceAssemblyDirectories.Add(assemblyDirectory);
            }
        }

        private static async Task CollectFastAssemblyScopedCurrentDomainRequirementsAsync(
            List<string> csharpFilePaths,
            List<string> asmdefDirectories,
            List<AssemblyReferenceDirectory> assemblyReferenceDirectories,
            string projectRoot,
            HashSet<string> assemblyScopedCurrentDomainDirectories,
            HashSet<string> domainReferenceAssemblyDirectories,
            CancellationToken ct)
        {
            Debug.Assert(csharpFilePaths != null, "csharpFilePaths must not be null");
            Debug.Assert(asmdefDirectories != null, "asmdefDirectories must not be null");
            Debug.Assert(
                assemblyReferenceDirectories != null,
                "assemblyReferenceDirectories must not be null");
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            Debug.Assert(
                assemblyScopedCurrentDomainDirectories != null,
                "assemblyScopedCurrentDomainDirectories must not be null");
            Debug.Assert(domainReferenceAssemblyDirectories != null, "domainReferenceAssemblyDirectories must not be null");

            int inspectedEntryCount = 0;
            foreach (string csharpFilePath in csharpFilePaths)
            {
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                string source = File.ReadAllText(csharpFilePath);
                if (!ThirdPartyToolMigrationRules.ContainsMigrationCandidateText(source))
                {
                    continue;
                }

                string assemblyDirectory = FindNearestAssemblyDirectory(
                    csharpFilePath,
                    asmdefDirectories,
                    assemblyReferenceDirectories,
                    projectRoot);
                if (assemblyScopedCurrentDomainDirectories.Contains(assemblyDirectory) &&
                    ThirdPartyToolMigrationRules.ContainsCurrentDomainMetadataApiForAssembly(source, true))
                {
                    domainReferenceAssemblyDirectories.Add(assemblyDirectory);
                }

                inspectedEntryCount++;
                if (inspectedEntryCount % PreviewYieldBatchSize == 0)
                {
                    await Task.Yield();
                }
            }
        }

        private static async Task CollectFastAssemblyScopedCurrentFirstPartyToolsRequirementsAsync(
            List<string> csharpFilePaths,
            List<string> asmdefDirectories,
            List<AssemblyReferenceDirectory> assemblyReferenceDirectories,
            string projectRoot,
            HashSet<string> assemblyScopedCurrentFirstPartyToolsDirectories,
            HashSet<string> firstPartyScreenshotReferenceAssemblyDirectories,
            CancellationToken ct)
        {
            Debug.Assert(csharpFilePaths != null, "csharpFilePaths must not be null");
            Debug.Assert(asmdefDirectories != null, "asmdefDirectories must not be null");
            Debug.Assert(
                assemblyReferenceDirectories != null,
                "assemblyReferenceDirectories must not be null");
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            Debug.Assert(
                assemblyScopedCurrentFirstPartyToolsDirectories != null,
                "assemblyScopedCurrentFirstPartyToolsDirectories must not be null");
            Debug.Assert(
                firstPartyScreenshotReferenceAssemblyDirectories != null,
                "firstPartyScreenshotReferenceAssemblyDirectories must not be null");

            int inspectedEntryCount = 0;
            foreach (string csharpFilePath in csharpFilePaths)
            {
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                string source = File.ReadAllText(csharpFilePath);
                if (!ThirdPartyToolMigrationRules.ContainsMigrationCandidateText(source))
                {
                    continue;
                }

                string assemblyDirectory = FindNearestAssemblyDirectory(
                    csharpFilePath,
                    asmdefDirectories,
                    assemblyReferenceDirectories,
                    projectRoot);
                if (assemblyScopedCurrentFirstPartyToolsDirectories.Contains(assemblyDirectory) &&
                    ThirdPartyToolMigrationRules.ContainsCurrentFirstPartyScreenshotApiForAssembly(source, true))
                {
                    firstPartyScreenshotReferenceAssemblyDirectories.Add(assemblyDirectory);
                }

                inspectedEntryCount++;
                if (inspectedEntryCount % PreviewYieldBatchSize == 0)
                {
                    await Task.Yield();
                }
            }
        }

        private static bool ContainsAsmdefMigrationTarget(
            string asmdefFilePath,
            string projectRoot,
            MigrationAssemblyUsage assemblyUsage)
        {
            Debug.Assert(!string.IsNullOrEmpty(asmdefFilePath), "asmdefFilePath must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            bool hasLegacyCSharpSource;
            bool requiresToolContractsReference;
            bool requiresApplicationReference;
            bool requiresDomainReference;
            bool requiresFirstPartyScreenshotReference;
            bool hasAssemblyMigrationRequirement = TryGetAsmdefMigrationRequirements(
                asmdefFilePath,
                projectRoot,
                assemblyUsage,
                out hasLegacyCSharpSource,
                out requiresToolContractsReference,
                out requiresApplicationReference,
                out requiresDomainReference,
                out requiresFirstPartyScreenshotReference);
            string source = File.ReadAllText(asmdefFilePath);
            if (!hasAssemblyMigrationRequirement &&
                !ThirdPartyToolMigrationRules.ContainsLegacyMigrationCandidateText(source))
            {
                return false;
            }

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateAsmdefSource(
                    source,
                    hasLegacyCSharpSource,
                    requiresToolContractsReference,
                    requiresApplicationReference,
                    requiresDomainReference,
                    requiresFirstPartyScreenshotReference);

            return result.Changed;
        }

        private static bool TryGetAsmdefMigrationRequirements(
            string asmdefFilePath,
            string projectRoot,
            MigrationAssemblyUsage assemblyUsage,
            out bool hasLegacyCSharpSource,
            out bool requiresToolContractsReference,
            out bool requiresApplicationReference,
            out bool requiresDomainReference,
            out bool requiresFirstPartyScreenshotReference)
        {
            Debug.Assert(!string.IsNullOrEmpty(asmdefFilePath), "asmdefFilePath must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            string asmdefDirectory = Path.GetDirectoryName(asmdefFilePath) ?? projectRoot;
            hasLegacyCSharpSource = assemblyUsage.LegacyAssemblyDirectories.Contains(asmdefDirectory);
            requiresToolContractsReference =
                assemblyUsage.ToolContractsReferenceAssemblyDirectories.Contains(asmdefDirectory);
            requiresApplicationReference =
                assemblyUsage.ApplicationReferenceAssemblyDirectories.Contains(asmdefDirectory);
            requiresDomainReference =
                assemblyUsage.DomainReferenceAssemblyDirectories.Contains(asmdefDirectory);
            requiresFirstPartyScreenshotReference =
                assemblyUsage.FirstPartyScreenshotReferenceAssemblyDirectories.Contains(asmdefDirectory);
            return hasLegacyCSharpSource ||
                requiresToolContractsReference ||
                requiresApplicationReference ||
                requiresDomainReference ||
                requiresFirstPartyScreenshotReference;
        }

        private static bool ContainsFastCSharpMigrationTarget(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return ThirdPartyToolMigrationRules.ContainsLegacyMigrationCandidateText(source) &&
                ThirdPartyToolMigrationRules.ContainsLegacyCSharpApi(source);
        }

        private static bool ContainsFastAsmdefMigrationTarget(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return ThirdPartyToolMigrationRules.ContainsLegacyAsmdefNameReference(source);
        }

        private static bool ShouldExcludeFastScanDirectory(string directoryPath)
        {
            Debug.Assert(!string.IsNullOrEmpty(directoryPath), "directoryPath must not be null or empty");

            string directoryName = Path.GetFileName(directoryPath);
            return ThirdPartyToolMigrationRules.IsExcludedDirectoryName(directoryName);
        }

        private bool TryGetCachedPreview(
            string projectRoot,
            out ThirdPartyToolMigrationPreview preview)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            lock (_previewCacheLock)
            {
                if (_hasCachedPreview &&
                    string.Equals(_cachedPreviewProjectRoot, projectRoot, StringComparison.Ordinal))
                {
                    preview = _cachedPreview;
                    return true;
                }
            }

            preview = default;
            return false;
        }

        private void StoreCachedPreview(string projectRoot, ThirdPartyToolMigrationPreview preview)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            lock (_previewCacheLock)
            {
                _cachedPreviewProjectRoot = projectRoot;
                _cachedPreview = preview;
                _hasCachedPreview = true;
            }
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
            string[] legacyToolInfoAliases = GetAllAssemblyScopedLegacyToolInfoAliases(assemblyUsage);

            foreach (string csharpFilePath in inventory.CSharpFilePaths)
            {
                string source = File.ReadAllText(csharpFilePath);
                if (!ThirdPartyToolMigrationRules.ContainsMigrationCandidateText(source) &&
                    !ThirdPartyToolMigrationRules.ContainsLegacyTypeAliasReference(source, legacyToolInfoAliases))
                {
                    continue;
                }

                string assemblyDirectory = FindNearestAssemblyDirectory(
                    csharpFilePath,
                    assemblyUsage.AsmdefDirectories,
                    assemblyUsage.AssemblyReferenceDirectories,
                    projectRoot);
                string[] legacyAssemblyAliases;
                if (!assemblyUsage.AssemblyScopedLegacyAliasesByDirectory.TryGetValue(
                        assemblyDirectory,
                        out legacyAssemblyAliases))
                {
                    legacyAssemblyAliases = Array.Empty<string>();
                }
                string[] legacyAssemblyToolInfoAliases;
                if (!assemblyUsage.AssemblyScopedLegacyToolInfoAliasesByDirectory.TryGetValue(
                        assemblyDirectory,
                        out legacyAssemblyToolInfoAliases))
                {
                    legacyAssemblyToolInfoAliases = Array.Empty<string>();
                }
                bool hasLegacyAssemblySource =
                    assemblyUsage.AssemblyScopedLegacyDirectories.Contains(assemblyDirectory) &&
                    ThirdPartyToolMigrationRules.ContainsLegacyAssemblyScopedApi(source, legacyAssemblyAliases);
                ThirdPartyToolMigrationContentResult result =
                    ThirdPartyToolMigrationRules.MigrateCSharpSourceForLegacyAssembly(
                        source,
                        hasLegacyAssemblySource,
                        legacyAssemblyAliases,
                        legacyAssemblyToolInfoAliases);
                if (!result.Changed)
                {
                    continue;
                }

                replacementCount += result.ReplacementCount;
                changes.Add(new MigrationFileChange(csharpFilePath, result.Content));
            }

            foreach (string asmdefFilePath in inventory.AsmdefFilePaths)
            {
                bool hasLegacyCSharpSource;
                bool requiresToolContractsReference;
                bool requiresApplicationReference;
                bool requiresDomainReference;
                bool requiresFirstPartyScreenshotReference;
                bool hasAssemblyMigrationRequirement = TryGetAsmdefMigrationRequirements(
                    asmdefFilePath,
                    projectRoot,
                    assemblyUsage,
                    out hasLegacyCSharpSource,
                    out requiresToolContractsReference,
                    out requiresApplicationReference,
                    out requiresDomainReference,
                    out requiresFirstPartyScreenshotReference);
                string source = File.ReadAllText(asmdefFilePath);
                if (!hasAssemblyMigrationRequirement &&
                    !ThirdPartyToolMigrationRules.ContainsLegacyMigrationCandidateText(source))
                {
                    continue;
                }

                ThirdPartyToolMigrationContentResult result =
                    ThirdPartyToolMigrationRules.MigrateAsmdefSource(
                        source,
                        hasLegacyCSharpSource,
                        requiresToolContractsReference,
                        requiresApplicationReference,
                        requiresDomainReference,
                        requiresFirstPartyScreenshotReference);
                if (!result.Changed)
                {
                    continue;
                }

                replacementCount += result.ReplacementCount;
                changes.Add(new MigrationFileChange(asmdefFilePath, result.Content));
            }

            return new MigrationPlan(changes, replacementCount);
        }

        private static async Task<MigrationPlan> CreateMigrationPlanAsync(
            string projectRoot,
            IProgress<ThirdPartyToolMigrationProgress> progress,
            CancellationToken ct)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            Debug.Assert(progress != null, "progress must not be null");

            if (!Directory.Exists(projectRoot))
            {
                throw new DirectoryNotFoundException(projectRoot);
            }

            ProjectFileInventory inventory = await ProjectFileInventory.CreateAsync(projectRoot, progress, ct);
            if (ct.IsCancellationRequested)
            {
                return MigrationPlan.Empty;
            }

            MigrationProgressCounter progressCounter = new(GetPreviewWorkItemCount(inventory), progress);
            ThirdPartyToolMigrationSourceFileCache sourceFileCache = new();
            MigrationAssemblyUsage assemblyUsage = await FindMigrationAssemblyUsageAsync(
                projectRoot,
                inventory.CSharpFilePaths,
                inventory.AsmdefFilePaths,
                inventory.AsmrefFilePaths,
                sourceFileCache,
                progressCounter,
                ct);
            if (ct.IsCancellationRequested)
            {
                return MigrationPlan.Empty;
            }

            List<MigrationFileChange> changes = new();
            int replacementCount = 0;
            string[] legacyToolInfoAliases = GetAllAssemblyScopedLegacyToolInfoAliases(assemblyUsage);

            foreach (string csharpFilePath in inventory.CSharpFilePaths)
            {
                if (ct.IsCancellationRequested)
                {
                    return MigrationPlan.Empty;
                }

                string source = sourceFileCache.ReadAllText(csharpFilePath);
                await progressCounter.ReportProcessedItemAsync(ct);
                if (!ThirdPartyToolMigrationRules.ContainsMigrationCandidateText(source) &&
                    !ThirdPartyToolMigrationRules.ContainsLegacyTypeAliasReference(source, legacyToolInfoAliases))
                {
                    continue;
                }

                string assemblyDirectory = FindNearestAssemblyDirectory(
                    csharpFilePath,
                    assemblyUsage.AsmdefDirectories,
                    assemblyUsage.AssemblyReferenceDirectories,
                    projectRoot);
                string[] legacyAssemblyAliases;
                if (!assemblyUsage.AssemblyScopedLegacyAliasesByDirectory.TryGetValue(
                        assemblyDirectory,
                        out legacyAssemblyAliases))
                {
                    legacyAssemblyAliases = Array.Empty<string>();
                }
                string[] legacyAssemblyToolInfoAliases;
                if (!assemblyUsage.AssemblyScopedLegacyToolInfoAliasesByDirectory.TryGetValue(
                        assemblyDirectory,
                        out legacyAssemblyToolInfoAliases))
                {
                    legacyAssemblyToolInfoAliases = Array.Empty<string>();
                }
                bool hasLegacyAssemblySource =
                    assemblyUsage.AssemblyScopedLegacyDirectories.Contains(assemblyDirectory) &&
                    ThirdPartyToolMigrationRules.ContainsLegacyAssemblyScopedApi(source, legacyAssemblyAliases);
                ThirdPartyToolMigrationContentResult result =
                    ThirdPartyToolMigrationRules.MigrateCSharpSourceForLegacyAssembly(
                        source,
                        hasLegacyAssemblySource,
                        legacyAssemblyAliases,
                        legacyAssemblyToolInfoAliases);
                if (!result.Changed)
                {
                    continue;
                }

                replacementCount += result.ReplacementCount;
                changes.Add(new MigrationFileChange(csharpFilePath, result.Content));
            }

            foreach (string asmdefFilePath in inventory.AsmdefFilePaths)
            {
                if (ct.IsCancellationRequested)
                {
                    return MigrationPlan.Empty;
                }

                bool hasLegacyCSharpSource;
                bool requiresToolContractsReference;
                bool requiresApplicationReference;
                bool requiresDomainReference;
                bool requiresFirstPartyScreenshotReference;
                bool hasAssemblyMigrationRequirement = TryGetAsmdefMigrationRequirements(
                    asmdefFilePath,
                    projectRoot,
                    assemblyUsage,
                    out hasLegacyCSharpSource,
                    out requiresToolContractsReference,
                    out requiresApplicationReference,
                    out requiresDomainReference,
                    out requiresFirstPartyScreenshotReference);
                string source = sourceFileCache.ReadAllText(asmdefFilePath);
                await progressCounter.ReportProcessedItemAsync(ct);
                if (!hasAssemblyMigrationRequirement &&
                    !ThirdPartyToolMigrationRules.ContainsLegacyMigrationCandidateText(source))
                {
                    continue;
                }

                ThirdPartyToolMigrationContentResult result =
                    ThirdPartyToolMigrationRules.MigrateAsmdefSource(
                        source,
                        hasLegacyCSharpSource,
                        requiresToolContractsReference,
                        requiresApplicationReference,
                        requiresDomainReference,
                        requiresFirstPartyScreenshotReference);
                if (!result.Changed)
                {
                    continue;
                }

                replacementCount += result.ReplacementCount;
                changes.Add(new MigrationFileChange(asmdefFilePath, result.Content));
            }

            progressCounter.ReportComplete();
            return new MigrationPlan(changes, replacementCount);
        }

        private static int GetPreviewWorkItemCount(ProjectFileInventory inventory)
        {
            Debug.Assert(inventory != null, "inventory must not be null");

            return (inventory.CSharpFilePaths.Count * 3) +
                (inventory.AsmdefFilePaths.Count * 2) +
                inventory.AsmrefFilePaths.Count;
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

        private static string NormalizeProjectRoot(string projectRoot)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            return Path.GetFullPath(projectRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
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
            HashSet<string> assemblyScopedCurrentDomainDirectories = new(StringComparer.Ordinal);
            HashSet<string> assemblyScopedCurrentFirstPartyToolsDirectories = new(StringComparer.Ordinal);
            Dictionary<string, HashSet<string>> assemblyScopedLegacyAliasesByDirectory =
                new(StringComparer.Ordinal);
            Dictionary<string, HashSet<string>> assemblyScopedLegacyToolInfoAliasesByDirectory =
                new(StringComparer.Ordinal);
            HashSet<string> registrarAssemblyDirectories = new(StringComparer.Ordinal);
            HashSet<string> domainMetadataAssemblyDirectories = new(StringComparer.Ordinal);
            HashSet<string> toolContractsReferenceAssemblyDirectories = new(StringComparer.Ordinal);
            HashSet<string> applicationReferenceAssemblyDirectories = new(StringComparer.Ordinal);
            HashSet<string> domainReferenceAssemblyDirectories = new(StringComparer.Ordinal);
            HashSet<string> firstPartyScreenshotReferenceAssemblyDirectories = new(StringComparer.Ordinal);

            foreach (string csharpFilePath in csharpFilePaths)
            {
                string source = File.ReadAllText(csharpFilePath);
                if (!ThirdPartyToolMigrationRules.ContainsMigrationCandidateText(source))
                {
                    continue;
                }

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
                    AddAssemblyScopedLegacyAliases(
                        assemblyScopedLegacyAliasesByDirectory,
                        assemblyDirectory,
                        ThirdPartyToolMigrationRules.GetLegacyGlobalNamespaceAliases(source));
                }

                if (ThirdPartyToolMigrationRules.ContainsLegacyGlobalToolInfoTypeAlias(source))
                {
                    AddAssemblyScopedLegacyAliases(
                        assemblyScopedLegacyToolInfoAliasesByDirectory,
                        assemblyDirectory,
                        ThirdPartyToolMigrationRules.GetLegacyGlobalToolInfoTypeAliases(source));
                }

                if (ThirdPartyToolMigrationRules.ContainsCurrentDomainGlobalUsing(source))
                {
                    assemblyScopedCurrentDomainDirectories.Add(assemblyDirectory);
                }

                if (ThirdPartyToolMigrationRules.ContainsCurrentFirstPartyToolsGlobalUsing(source))
                {
                    assemblyScopedCurrentFirstPartyToolsDirectories.Add(assemblyDirectory);
                }

                if (ThirdPartyToolMigrationRules.ContainsLegacyRegistrarApi(source) ||
                    ThirdPartyToolMigrationRules.ContainsCurrentRegistrarApi(source) ||
                    ThirdPartyToolMigrationRules.ContainsLegacyApplicationApiForAssembly(
                        source,
                        ThirdPartyToolMigrationRules.ContainsLegacyCSharpApi(source) ||
                        ThirdPartyToolMigrationRules.ContainsCurrentToolContractsApi(source),
                        Array.Empty<string>()) ||
                    ThirdPartyToolMigrationRules.ContainsCurrentApplicationApi(source))
                {
                    registrarAssemblyDirectories.Add(assemblyDirectory);
                }

                if (ThirdPartyToolMigrationRules.ContainsRegistrarDomainReturnApi(source))
                {
                    domainReferenceAssemblyDirectories.Add(assemblyDirectory);
                }

                if (ThirdPartyToolMigrationRules.ContainsCurrentToolContractsApi(source))
                {
                    toolContractsReferenceAssemblyDirectories.Add(assemblyDirectory);
                }

                if (ThirdPartyToolMigrationRules.ContainsLegacyDomainMetadataApi(source))
                {
                    domainMetadataAssemblyDirectories.Add(assemblyDirectory);
                }

                if (ThirdPartyToolMigrationRules.ContainsCurrentDomainMetadataApi(source))
                {
                    domainReferenceAssemblyDirectories.Add(assemblyDirectory);
                }

                if (ThirdPartyToolMigrationRules.ContainsLegacyFirstPartyScreenshotApiForAssembly(
                        source,
                        ThirdPartyToolMigrationRules.ContainsLegacyCSharpApi(source) ||
                        ThirdPartyToolMigrationRules.ContainsCurrentToolContractsApi(source),
                        Array.Empty<string>()) ||
                    ThirdPartyToolMigrationRules.ContainsCurrentFirstPartyScreenshotApi(source))
                {
                    firstPartyScreenshotReferenceAssemblyDirectories.Add(assemblyDirectory);
                }
            }

            foreach (string csharpFilePath in csharpFilePaths)
            {
                string source = File.ReadAllText(csharpFilePath);
                if (!ThirdPartyToolMigrationRules.ContainsMigrationCandidateText(source))
                {
                    continue;
                }

                string assemblyDirectory = FindNearestAssemblyDirectory(
                    csharpFilePath,
                    asmdefDirectories,
                    assemblyReferenceDirectories,
                    projectRoot);
                string[] legacyAssemblyAliases = Array.Empty<string>();
                if (assemblyScopedLegacyAliasesByDirectory.TryGetValue(
                        assemblyDirectory,
                        out HashSet<string> legacyAssemblyAliasSet))
                {
                    legacyAssemblyAliases = legacyAssemblyAliasSet
                        .OrderBy(alias => alias, StringComparer.Ordinal)
                        .ToArray();
                }

                if (ThirdPartyToolMigrationRules.ContainsLegacyDomainHelperApiForAssembly(
                        source,
                        assemblyScopedLegacyDirectories.Contains(assemblyDirectory),
                        legacyAssemblyAliases))
                {
                    domainMetadataAssemblyDirectories.Add(assemblyDirectory);
                }

                if (ThirdPartyToolMigrationRules.ContainsLegacyRegistrarApiForAssembly(
                        source,
                        assemblyScopedLegacyDirectories.Contains(assemblyDirectory),
                        legacyAssemblyAliases) ||
                    ThirdPartyToolMigrationRules.ContainsLegacyApplicationApiForAssembly(
                        source,
                        assemblyScopedLegacyDirectories.Contains(assemblyDirectory) ||
                        ThirdPartyToolMigrationRules.ContainsCurrentToolContractsApi(source),
                        legacyAssemblyAliases))
                {
                    registrarAssemblyDirectories.Add(assemblyDirectory);
                }

                if (ThirdPartyToolMigrationRules.ContainsRegistrarDomainReturnApiForAssembly(
                        source,
                        assemblyScopedLegacyDirectories.Contains(assemblyDirectory),
                        legacyAssemblyAliases))
                {
                    domainReferenceAssemblyDirectories.Add(assemblyDirectory);
                }

                if (ThirdPartyToolMigrationRules.ContainsCurrentDomainMetadataApiForAssembly(
                        source,
                        assemblyScopedCurrentDomainDirectories.Contains(assemblyDirectory)))
                {
                    domainReferenceAssemblyDirectories.Add(assemblyDirectory);
                }

                if (ThirdPartyToolMigrationRules.ContainsLegacyFirstPartyScreenshotApiForAssembly(
                        source,
                        assemblyScopedLegacyDirectories.Contains(assemblyDirectory) ||
                        ThirdPartyToolMigrationRules.ContainsCurrentToolContractsApi(source),
                        legacyAssemblyAliases) ||
                    ThirdPartyToolMigrationRules.ContainsCurrentFirstPartyScreenshotApiForAssembly(
                        source,
                        assemblyScopedCurrentFirstPartyToolsDirectories.Contains(assemblyDirectory)))
                {
                    firstPartyScreenshotReferenceAssemblyDirectories.Add(assemblyDirectory);
                }
            }

            foreach (string registrarAssemblyDirectory in registrarAssemblyDirectories)
            {
                applicationReferenceAssemblyDirectories.Add(registrarAssemblyDirectory);
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
                CreateAssemblyScopedLegacyAliasesByDirectory(assemblyScopedLegacyAliasesByDirectory),
                CreateAssemblyScopedLegacyAliasesByDirectory(assemblyScopedLegacyToolInfoAliasesByDirectory),
                toolContractsReferenceAssemblyDirectories,
                applicationReferenceAssemblyDirectories,
                domainReferenceAssemblyDirectories,
                firstPartyScreenshotReferenceAssemblyDirectories);
        }

        private static async Task<MigrationAssemblyUsage> FindMigrationAssemblyUsageAsync(
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
            HashSet<string> legacyAssemblyDirectories = new(StringComparer.Ordinal);
            HashSet<string> assemblyScopedLegacyDirectories = new(StringComparer.Ordinal);
            HashSet<string> assemblyScopedCurrentDomainDirectories = new(StringComparer.Ordinal);
            HashSet<string> assemblyScopedCurrentFirstPartyToolsDirectories = new(StringComparer.Ordinal);
            Dictionary<string, HashSet<string>> assemblyScopedLegacyAliasesByDirectory =
                new(StringComparer.Ordinal);
            Dictionary<string, HashSet<string>> assemblyScopedLegacyToolInfoAliasesByDirectory =
                new(StringComparer.Ordinal);
            HashSet<string> registrarAssemblyDirectories = new(StringComparer.Ordinal);
            HashSet<string> domainMetadataAssemblyDirectories = new(StringComparer.Ordinal);
            HashSet<string> toolContractsReferenceAssemblyDirectories = new(StringComparer.Ordinal);
            HashSet<string> applicationReferenceAssemblyDirectories = new(StringComparer.Ordinal);
            HashSet<string> domainReferenceAssemblyDirectories = new(StringComparer.Ordinal);
            HashSet<string> firstPartyScreenshotReferenceAssemblyDirectories = new(StringComparer.Ordinal);

            foreach (string csharpFilePath in csharpFilePaths)
            {
                if (ct.IsCancellationRequested)
                {
                    return CreateMigrationAssemblyUsage(
                        asmdefDirectories,
                        assemblyReferenceDirectories,
                        legacyAssemblyDirectories,
                        assemblyScopedLegacyDirectories,
                        assemblyScopedLegacyAliasesByDirectory,
                        assemblyScopedLegacyToolInfoAliasesByDirectory,
                        toolContractsReferenceAssemblyDirectories,
                        applicationReferenceAssemblyDirectories,
                        domainReferenceAssemblyDirectories,
                        firstPartyScreenshotReferenceAssemblyDirectories);
                }

                string source = sourceFileCache.ReadAllText(csharpFilePath);
                await progressCounter.ReportProcessedItemAsync(ct);
                if (!ThirdPartyToolMigrationRules.ContainsMigrationCandidateText(source))
                {
                    continue;
                }

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
                    AddAssemblyScopedLegacyAliases(
                        assemblyScopedLegacyAliasesByDirectory,
                        assemblyDirectory,
                        ThirdPartyToolMigrationRules.GetLegacyGlobalNamespaceAliases(source));
                }

                if (ThirdPartyToolMigrationRules.ContainsLegacyGlobalToolInfoTypeAlias(source))
                {
                    AddAssemblyScopedLegacyAliases(
                        assemblyScopedLegacyToolInfoAliasesByDirectory,
                        assemblyDirectory,
                        ThirdPartyToolMigrationRules.GetLegacyGlobalToolInfoTypeAliases(source));
                }

                if (ThirdPartyToolMigrationRules.ContainsCurrentDomainGlobalUsing(source))
                {
                    assemblyScopedCurrentDomainDirectories.Add(assemblyDirectory);
                }

                if (ThirdPartyToolMigrationRules.ContainsCurrentFirstPartyToolsGlobalUsing(source))
                {
                    assemblyScopedCurrentFirstPartyToolsDirectories.Add(assemblyDirectory);
                }

                if (ThirdPartyToolMigrationRules.ContainsLegacyRegistrarApi(source) ||
                    ThirdPartyToolMigrationRules.ContainsCurrentRegistrarApi(source) ||
                    ThirdPartyToolMigrationRules.ContainsLegacyApplicationApiForAssembly(
                        source,
                        ThirdPartyToolMigrationRules.ContainsLegacyCSharpApi(source) ||
                        ThirdPartyToolMigrationRules.ContainsCurrentToolContractsApi(source),
                        Array.Empty<string>()) ||
                    ThirdPartyToolMigrationRules.ContainsCurrentApplicationApi(source))
                {
                    registrarAssemblyDirectories.Add(assemblyDirectory);
                }

                if (ThirdPartyToolMigrationRules.ContainsRegistrarDomainReturnApi(source))
                {
                    domainReferenceAssemblyDirectories.Add(assemblyDirectory);
                }

                if (ThirdPartyToolMigrationRules.ContainsCurrentToolContractsApi(source))
                {
                    toolContractsReferenceAssemblyDirectories.Add(assemblyDirectory);
                }

                if (ThirdPartyToolMigrationRules.ContainsLegacyDomainMetadataApi(source))
                {
                    domainMetadataAssemblyDirectories.Add(assemblyDirectory);
                }

                if (ThirdPartyToolMigrationRules.ContainsCurrentDomainMetadataApi(source))
                {
                    domainReferenceAssemblyDirectories.Add(assemblyDirectory);
                }

                if (ThirdPartyToolMigrationRules.ContainsLegacyFirstPartyScreenshotApiForAssembly(
                        source,
                        ThirdPartyToolMigrationRules.ContainsLegacyCSharpApi(source) ||
                        ThirdPartyToolMigrationRules.ContainsCurrentToolContractsApi(source),
                        Array.Empty<string>()) ||
                    ThirdPartyToolMigrationRules.ContainsCurrentFirstPartyScreenshotApi(source))
                {
                    firstPartyScreenshotReferenceAssemblyDirectories.Add(assemblyDirectory);
                }
            }

            foreach (string csharpFilePath in csharpFilePaths)
            {
                if (ct.IsCancellationRequested)
                {
                    return CreateMigrationAssemblyUsage(
                        asmdefDirectories,
                        assemblyReferenceDirectories,
                        legacyAssemblyDirectories,
                        assemblyScopedLegacyDirectories,
                        assemblyScopedLegacyAliasesByDirectory,
                        assemblyScopedLegacyToolInfoAliasesByDirectory,
                        toolContractsReferenceAssemblyDirectories,
                        applicationReferenceAssemblyDirectories,
                        domainReferenceAssemblyDirectories,
                        firstPartyScreenshotReferenceAssemblyDirectories);
                }

                string source = sourceFileCache.ReadAllText(csharpFilePath);
                await progressCounter.ReportProcessedItemAsync(ct);
                if (!ThirdPartyToolMigrationRules.ContainsMigrationCandidateText(source))
                {
                    continue;
                }

                string assemblyDirectory = FindNearestAssemblyDirectory(
                    csharpFilePath,
                    asmdefDirectories,
                    assemblyReferenceDirectories,
                    projectRoot);
                string[] legacyAssemblyAliases = Array.Empty<string>();
                if (assemblyScopedLegacyAliasesByDirectory.TryGetValue(
                        assemblyDirectory,
                        out HashSet<string> legacyAssemblyAliasSet))
                {
                    legacyAssemblyAliases = legacyAssemblyAliasSet
                        .OrderBy(alias => alias, StringComparer.Ordinal)
                        .ToArray();
                }

                if (ThirdPartyToolMigrationRules.ContainsLegacyDomainHelperApiForAssembly(
                        source,
                        assemblyScopedLegacyDirectories.Contains(assemblyDirectory),
                        legacyAssemblyAliases))
                {
                    domainMetadataAssemblyDirectories.Add(assemblyDirectory);
                }

                if (ThirdPartyToolMigrationRules.ContainsLegacyRegistrarApiForAssembly(
                        source,
                        assemblyScopedLegacyDirectories.Contains(assemblyDirectory),
                        legacyAssemblyAliases) ||
                    ThirdPartyToolMigrationRules.ContainsLegacyApplicationApiForAssembly(
                        source,
                        assemblyScopedLegacyDirectories.Contains(assemblyDirectory) ||
                        ThirdPartyToolMigrationRules.ContainsCurrentToolContractsApi(source),
                        legacyAssemblyAliases))
                {
                    registrarAssemblyDirectories.Add(assemblyDirectory);
                }

                if (ThirdPartyToolMigrationRules.ContainsRegistrarDomainReturnApiForAssembly(
                        source,
                        assemblyScopedLegacyDirectories.Contains(assemblyDirectory),
                        legacyAssemblyAliases))
                {
                    domainReferenceAssemblyDirectories.Add(assemblyDirectory);
                }

                if (ThirdPartyToolMigrationRules.ContainsCurrentDomainMetadataApiForAssembly(
                        source,
                        assemblyScopedCurrentDomainDirectories.Contains(assemblyDirectory)))
                {
                    domainReferenceAssemblyDirectories.Add(assemblyDirectory);
                }

                if (ThirdPartyToolMigrationRules.ContainsLegacyFirstPartyScreenshotApiForAssembly(
                        source,
                        assemblyScopedLegacyDirectories.Contains(assemblyDirectory) ||
                        ThirdPartyToolMigrationRules.ContainsCurrentToolContractsApi(source),
                        legacyAssemblyAliases) ||
                    ThirdPartyToolMigrationRules.ContainsCurrentFirstPartyScreenshotApiForAssembly(
                        source,
                        assemblyScopedCurrentFirstPartyToolsDirectories.Contains(assemblyDirectory)))
                {
                    firstPartyScreenshotReferenceAssemblyDirectories.Add(assemblyDirectory);
                }
            }

            foreach (string registrarAssemblyDirectory in registrarAssemblyDirectories)
            {
                applicationReferenceAssemblyDirectories.Add(registrarAssemblyDirectory);
            }

            foreach (string domainMetadataAssemblyDirectory in domainMetadataAssemblyDirectories)
            {
                if (legacyAssemblyDirectories.Contains(domainMetadataAssemblyDirectory))
                {
                    domainReferenceAssemblyDirectories.Add(domainMetadataAssemblyDirectory);
                }
            }

            return CreateMigrationAssemblyUsage(
                asmdefDirectories,
                assemblyReferenceDirectories,
                legacyAssemblyDirectories,
                assemblyScopedLegacyDirectories,
                assemblyScopedLegacyAliasesByDirectory,
                assemblyScopedLegacyToolInfoAliasesByDirectory,
                toolContractsReferenceAssemblyDirectories,
                applicationReferenceAssemblyDirectories,
                domainReferenceAssemblyDirectories,
                firstPartyScreenshotReferenceAssemblyDirectories);
        }

        private static MigrationAssemblyUsage CreateMigrationAssemblyUsage(
            List<string> asmdefDirectories,
            List<AssemblyReferenceDirectory> assemblyReferenceDirectories,
            HashSet<string> legacyAssemblyDirectories,
            HashSet<string> assemblyScopedLegacyDirectories,
            Dictionary<string, HashSet<string>> assemblyScopedLegacyAliasesByDirectory,
            Dictionary<string, HashSet<string>> assemblyScopedLegacyToolInfoAliasesByDirectory,
            HashSet<string> toolContractsReferenceAssemblyDirectories,
            HashSet<string> applicationReferenceAssemblyDirectories,
            HashSet<string> domainReferenceAssemblyDirectories,
            HashSet<string> firstPartyScreenshotReferenceAssemblyDirectories)
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
                assemblyScopedLegacyAliasesByDirectory != null,
                "assemblyScopedLegacyAliasesByDirectory must not be null");
            Debug.Assert(
                assemblyScopedLegacyToolInfoAliasesByDirectory != null,
                "assemblyScopedLegacyToolInfoAliasesByDirectory must not be null");
            Debug.Assert(
                toolContractsReferenceAssemblyDirectories != null,
                "toolContractsReferenceAssemblyDirectories must not be null");
            Debug.Assert(
                applicationReferenceAssemblyDirectories != null,
                "applicationReferenceAssemblyDirectories must not be null");
            Debug.Assert(
                domainReferenceAssemblyDirectories != null,
                "domainReferenceAssemblyDirectories must not be null");
            Debug.Assert(
                firstPartyScreenshotReferenceAssemblyDirectories != null,
                "firstPartyScreenshotReferenceAssemblyDirectories must not be null");

            return new MigrationAssemblyUsage(
                asmdefDirectories,
                assemblyReferenceDirectories,
                legacyAssemblyDirectories,
                assemblyScopedLegacyDirectories,
                CreateAssemblyScopedLegacyAliasesByDirectory(assemblyScopedLegacyAliasesByDirectory),
                CreateAssemblyScopedLegacyAliasesByDirectory(assemblyScopedLegacyToolInfoAliasesByDirectory),
                toolContractsReferenceAssemblyDirectories,
                applicationReferenceAssemblyDirectories,
                domainReferenceAssemblyDirectories,
                firstPartyScreenshotReferenceAssemblyDirectories);
        }

        private static void AddAssemblyScopedLegacyAliases(
            Dictionary<string, HashSet<string>> aliasesByDirectory,
            string assemblyDirectory,
            string[] aliases)
        {
            Debug.Assert(aliasesByDirectory != null, "aliasesByDirectory must not be null");
            Debug.Assert(!string.IsNullOrEmpty(assemblyDirectory), "assemblyDirectory must not be null or empty");
            Debug.Assert(aliases != null, "aliases must not be null");

            if (aliases.Length == 0)
            {
                return;
            }

            if (!aliasesByDirectory.TryGetValue(assemblyDirectory, out HashSet<string> aliasSet))
            {
                aliasSet = new HashSet<string>(StringComparer.Ordinal);
                aliasesByDirectory.Add(assemblyDirectory, aliasSet);
            }

            foreach (string alias in aliases)
            {
                aliasSet.Add(alias);
            }
        }

        private static Dictionary<string, string[]> CreateAssemblyScopedLegacyAliasesByDirectory(
            Dictionary<string, HashSet<string>> aliasesByDirectory)
        {
            Debug.Assert(aliasesByDirectory != null, "aliasesByDirectory must not be null");

            Dictionary<string, string[]> result = new(StringComparer.Ordinal);
            foreach (KeyValuePair<string, HashSet<string>> aliasesForDirectory in aliasesByDirectory)
            {
                result.Add(
                    aliasesForDirectory.Key,
                    aliasesForDirectory.Value.OrderBy(alias => alias, StringComparer.Ordinal).ToArray());
            }

            return result;
        }

        private static string[] GetAllAssemblyScopedLegacyToolInfoAliases(MigrationAssemblyUsage assemblyUsage)
        {
            return assemblyUsage.AssemblyScopedLegacyToolInfoAliasesByDirectory.Values
                .SelectMany(aliases => aliases)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        private static List<AssemblyReferenceDirectory> CreateAssemblyReferenceDirectories(
            List<string> asmdefFilePaths,
            List<string> asmrefFilePaths)
        {
            Debug.Assert(asmdefFilePaths != null, "asmdefFilePaths must not be null");
            Debug.Assert(asmrefFilePaths != null, "asmrefFilePaths must not be null");

            if (asmrefFilePaths.Count == 0)
            {
                return new List<AssemblyReferenceDirectory>();
            }

            Dictionary<string, string> asmdefDirectoriesByReference = CreateAsmdefDirectoryMap(asmdefFilePaths);
            List<AssemblyReferenceDirectory> assemblyReferenceDirectories = new();
            foreach (string asmrefFilePath in asmrefFilePaths)
            {
                if (!TryReadJsonObjectFromFile(asmrefFilePath, out JObject asmref))
                {
                    continue;
                }

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

        private static async Task<List<AssemblyReferenceDirectory>> CreateAssemblyReferenceDirectoriesAsync(
            List<string> asmdefFilePaths,
            List<string> asmrefFilePaths,
            ThirdPartyToolMigrationSourceFileCache sourceFileCache,
            MigrationProgressCounter progressCounter,
            CancellationToken ct)
        {
            Debug.Assert(asmdefFilePaths != null, "asmdefFilePaths must not be null");
            Debug.Assert(asmrefFilePaths != null, "asmrefFilePaths must not be null");
            Debug.Assert(sourceFileCache != null, "sourceFileCache must not be null");
            Debug.Assert(progressCounter != null, "progressCounter must not be null");

            if (asmrefFilePaths.Count == 0)
            {
                return new List<AssemblyReferenceDirectory>();
            }

            Dictionary<string, string> asmdefDirectoriesByReference =
                await CreateAsmdefDirectoryMapAsync(asmdefFilePaths, sourceFileCache, progressCounter, ct);
            List<AssemblyReferenceDirectory> assemblyReferenceDirectories = new();
            foreach (string asmrefFilePath in asmrefFilePaths)
            {
                if (ct.IsCancellationRequested)
                {
                    return assemblyReferenceDirectories
                        .OrderByDescending(
                            assemblyReferenceDirectory => assemblyReferenceDirectory.SourceDirectory.Length)
                        .ToList();
                }

                if (!TryReadJsonObjectFromCache(asmrefFilePath, sourceFileCache, out JObject asmref))
                {
                    await progressCounter.ReportProcessedItemAsync(ct);
                    continue;
                }

                await progressCounter.ReportProcessedItemAsync(ct);
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

                if (!TryReadJsonObjectFromFile(asmdefFilePath, out JObject asmdef))
                {
                    continue;
                }

                string assemblyName = asmdef["name"]?.Value<string>() ?? string.Empty;
                AddAsmdefDirectoryReference(asmdefDirectoriesByReference, assemblyName, asmdefDirectory);
                AddAsmdefDirectoryReference(
                    asmdefDirectoriesByReference,
                    ReadAsmdefGuidReference(asmdefFilePath),
                    asmdefDirectory);
            }

            return asmdefDirectoriesByReference;
        }

        private static async Task<Dictionary<string, string>> CreateAsmdefDirectoryMapAsync(
            List<string> asmdefFilePaths,
            ThirdPartyToolMigrationSourceFileCache sourceFileCache,
            MigrationProgressCounter progressCounter,
            CancellationToken ct)
        {
            Debug.Assert(asmdefFilePaths != null, "asmdefFilePaths must not be null");
            Debug.Assert(sourceFileCache != null, "sourceFileCache must not be null");
            Debug.Assert(progressCounter != null, "progressCounter must not be null");

            Dictionary<string, string> asmdefDirectoriesByReference = new(StringComparer.Ordinal);
            foreach (string asmdefFilePath in asmdefFilePaths)
            {
                if (ct.IsCancellationRequested)
                {
                    return asmdefDirectoriesByReference;
                }

                string asmdefDirectory = Path.GetDirectoryName(asmdefFilePath) ?? string.Empty;
                if (asmdefDirectory.Length == 0)
                {
                    await progressCounter.ReportProcessedItemAsync(ct);
                    continue;
                }

                if (!TryReadJsonObjectFromCache(asmdefFilePath, sourceFileCache, out JObject asmdef))
                {
                    await progressCounter.ReportProcessedItemAsync(ct);
                    continue;
                }

                await progressCounter.ReportProcessedItemAsync(ct);
                string assemblyName = asmdef["name"]?.Value<string>() ?? string.Empty;
                AddAsmdefDirectoryReference(asmdefDirectoriesByReference, assemblyName, asmdefDirectory);
                AddAsmdefDirectoryReference(
                    asmdefDirectoriesByReference,
                    ReadAsmdefGuidReference(asmdefFilePath),
                    asmdefDirectory);
            }

            return asmdefDirectoriesByReference;
        }

        private static bool TryReadJsonObjectFromFile(string filePath, out JObject jsonObject)
        {
            return TryReadJsonObjectForMigration(filePath, File.ReadAllText, out jsonObject);
        }

        private static bool TryReadJsonObjectFromCache(
            string filePath,
            ThirdPartyToolMigrationSourceFileCache sourceFileCache,
            out JObject jsonObject)
        {
            Debug.Assert(sourceFileCache != null, "sourceFileCache must not be null");

            return TryReadJsonObjectForMigration(filePath, sourceFileCache.ReadAllText, out jsonObject);
        }

        internal static bool TryReadJsonObjectForMigration(
            string filePath,
            Func<string, string> readAllText,
            out JObject jsonObject)
        {
            Debug.Assert(!string.IsNullOrEmpty(filePath), "filePath must not be null or empty");
            Debug.Assert(readAllText != null, "readAllText must not be null");

            try
            {
                jsonObject = JObject.Parse(readAllText(filePath));
                return true;
            }
            catch (Exception ex) when (IsSkippableAssemblyJsonReadException(ex))
            {
                UnityEngine.Debug.LogWarning(
                    $"[UnityCliLoop] Skipping unreadable or malformed assembly JSON at {filePath}: {ex.Message}");
                jsonObject = null;
                return false;
            }
        }

        private static bool IsSkippableAssemblyJsonReadException(Exception ex)
        {
            Debug.Assert(ex != null, "ex must not be null");

            return ex is JsonException ||
                   ex is IOException ||
                   ex is UnauthorizedAccessException;
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

            bool isEditorAssemblyPath = IsEditorAssemblyPath(csharpFilePath, projectRoot);
            bool isFirstPassAssemblyPath = IsFirstPassAssemblyPath(csharpFilePath, projectRoot);
            string implicitAssemblyDirectoryName = GetImplicitAssemblyDirectoryName(
                isEditorAssemblyPath,
                isFirstPassAssemblyPath);
            return Path.Combine(projectRoot, implicitAssemblyDirectoryName);
        }

        private static string GetImplicitAssemblyDirectoryName(
            bool isEditorAssemblyPath,
            bool isFirstPassAssemblyPath)
        {
            if (isEditorAssemblyPath && isFirstPassAssemblyPath)
            {
                return ImplicitFirstPassEditorAssemblyDirectoryName;
            }

            if (isFirstPassAssemblyPath)
            {
                return ImplicitFirstPassRuntimeAssemblyDirectoryName;
            }

            return isEditorAssemblyPath
                ? ImplicitEditorAssemblyDirectoryName
                : ImplicitRuntimeAssemblyDirectoryName;
        }

        private static bool IsEditorAssemblyPath(string csharpFilePath, string projectRoot)
        {
            Debug.Assert(!string.IsNullOrEmpty(csharpFilePath), "csharpFilePath must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            string[] pathSegments = GetRelativePathSegments(csharpFilePath, projectRoot);
            return pathSegments.Any(
                pathSegment => string.Equals(pathSegment, "Editor", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsFirstPassAssemblyPath(string csharpFilePath, string projectRoot)
        {
            Debug.Assert(!string.IsNullOrEmpty(csharpFilePath), "csharpFilePath must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            string[] pathSegments = GetRelativePathSegments(csharpFilePath, projectRoot);
            if (pathSegments.Length < 2 ||
                !string.Equals(pathSegments[0], "Assets", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return string.Equals(pathSegments[1], "Plugins", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(pathSegments[1], "Standard Assets", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(pathSegments[1], "Pro Standard Assets", StringComparison.OrdinalIgnoreCase);
        }

        private static string[] GetRelativePathSegments(string filePath, string projectRoot)
        {
            Debug.Assert(!string.IsNullOrEmpty(filePath), "filePath must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            string relativePath = filePath.StartsWith(projectRoot, StringComparison.Ordinal)
                ? filePath.Substring(projectRoot.Length)
                : filePath;
            char[] separators =
            {
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar
            };
            return relativePath.Split(separators, StringSplitOptions.RemoveEmptyEntries);
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
            public static MigrationPlan Empty => new(new List<MigrationFileChange>(), 0);

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

        private sealed class MigrationProgressCounter
        {
            private readonly IProgress<ThirdPartyToolMigrationProgress> _progress;
            private readonly int _totalItemCount;
            private int _processedItemCount;

            public MigrationProgressCounter(
                int totalItemCount,
                IProgress<ThirdPartyToolMigrationProgress> progress)
            {
                Debug.Assert(totalItemCount >= 0, "totalItemCount must not be negative");
                Debug.Assert(progress != null, "progress must not be null");

                _totalItemCount = totalItemCount;
                _progress = progress;
                Report();
            }

            public async Task ReportProcessedItemAsync(CancellationToken ct)
            {
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                _processedItemCount++;
                Report();
                if (_processedItemCount % PreviewYieldBatchSize != 0)
                {
                    return;
                }

                await Task.Yield();
            }

            public void ReportComplete()
            {
                _processedItemCount = _totalItemCount;
                Report();
            }

            private void Report()
            {
                _progress.Report(
                    new ThirdPartyToolMigrationProgress(
                        Math.Min(_processedItemCount, _totalItemCount),
                        _totalItemCount));
            }
        }

        private readonly struct MigrationAssemblyUsage
        {
            public MigrationAssemblyUsage(
                List<string> asmdefDirectories,
                List<AssemblyReferenceDirectory> assemblyReferenceDirectories,
                HashSet<string> legacyAssemblyDirectories,
                HashSet<string> assemblyScopedLegacyDirectories,
                Dictionary<string, string[]> assemblyScopedLegacyAliasesByDirectory,
                Dictionary<string, string[]> assemblyScopedLegacyToolInfoAliasesByDirectory,
                HashSet<string> toolContractsReferenceAssemblyDirectories,
                HashSet<string> applicationReferenceAssemblyDirectories,
                HashSet<string> domainReferenceAssemblyDirectories,
                HashSet<string> firstPartyScreenshotReferenceAssemblyDirectories)
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
                    assemblyScopedLegacyAliasesByDirectory != null,
                    "assemblyScopedLegacyAliasesByDirectory must not be null");
                Debug.Assert(
                    assemblyScopedLegacyToolInfoAliasesByDirectory != null,
                    "assemblyScopedLegacyToolInfoAliasesByDirectory must not be null");
                Debug.Assert(
                    toolContractsReferenceAssemblyDirectories != null,
                    "toolContractsReferenceAssemblyDirectories must not be null");
                Debug.Assert(
                    applicationReferenceAssemblyDirectories != null,
                    "applicationReferenceAssemblyDirectories must not be null");
                Debug.Assert(
                    domainReferenceAssemblyDirectories != null,
                    "domainReferenceAssemblyDirectories must not be null");
                Debug.Assert(
                    firstPartyScreenshotReferenceAssemblyDirectories != null,
                    "firstPartyScreenshotReferenceAssemblyDirectories must not be null");

                AsmdefDirectories = asmdefDirectories ?? new List<string>();
                AssemblyReferenceDirectories = assemblyReferenceDirectories ?? new List<AssemblyReferenceDirectory>();
                LegacyAssemblyDirectories = legacyAssemblyDirectories ?? new HashSet<string>(StringComparer.Ordinal);
                AssemblyScopedLegacyDirectories = assemblyScopedLegacyDirectories ??
                    new HashSet<string>(StringComparer.Ordinal);
                AssemblyScopedLegacyAliasesByDirectory = assemblyScopedLegacyAliasesByDirectory ??
                    new Dictionary<string, string[]>(StringComparer.Ordinal);
                AssemblyScopedLegacyToolInfoAliasesByDirectory = assemblyScopedLegacyToolInfoAliasesByDirectory ??
                    new Dictionary<string, string[]>(StringComparer.Ordinal);
                ToolContractsReferenceAssemblyDirectories = toolContractsReferenceAssemblyDirectories ??
                    new HashSet<string>(StringComparer.Ordinal);
                ApplicationReferenceAssemblyDirectories = applicationReferenceAssemblyDirectories ??
                    new HashSet<string>(StringComparer.Ordinal);
                DomainReferenceAssemblyDirectories = domainReferenceAssemblyDirectories ??
                    new HashSet<string>(StringComparer.Ordinal);
                FirstPartyScreenshotReferenceAssemblyDirectories = firstPartyScreenshotReferenceAssemblyDirectories ??
                    new HashSet<string>(StringComparer.Ordinal);
            }

            public List<string> AsmdefDirectories { get; }
            public List<AssemblyReferenceDirectory> AssemblyReferenceDirectories { get; }
            public HashSet<string> LegacyAssemblyDirectories { get; }
            public HashSet<string> AssemblyScopedLegacyDirectories { get; }
            public Dictionary<string, string[]> AssemblyScopedLegacyAliasesByDirectory { get; }
            public Dictionary<string, string[]> AssemblyScopedLegacyToolInfoAliasesByDirectory { get; }
            public HashSet<string> ToolContractsReferenceAssemblyDirectories { get; }
            public HashSet<string> ApplicationReferenceAssemblyDirectories { get; }
            public HashSet<string> DomainReferenceAssemblyDirectories { get; }
            public HashSet<string> FirstPartyScreenshotReferenceAssemblyDirectories { get; }
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
                        if (inspectedEntryCount % PreviewYieldBatchSize == 0)
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
                        if (inspectedEntryCount % PreviewYieldBatchSize == 0)
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

            private static bool ShouldExcludeDirectory(string projectRoot, string directoryPath)
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
}
