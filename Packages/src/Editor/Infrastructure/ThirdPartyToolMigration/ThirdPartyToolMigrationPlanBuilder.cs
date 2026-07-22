using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.Domain;

using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationAssemblyReferenceResolver;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationAssemblyScopedNameMap;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationAsmdefMigrationRequirementResolver;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationCrossFileTimingMigrationPlanner;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Builds preview/apply migration plans without mutating project files.
    /// </summary>
    internal static class ThirdPartyToolMigrationPlanBuilder
    {
        internal static MigrationPlan Create(string projectRoot)
        {
            if (!Directory.Exists(projectRoot))
            {
                throw new DirectoryNotFoundException(projectRoot);
            }

            ProjectFileInventory inventory = ProjectFileInventory.Create(projectRoot);
            MigrationProjectFingerprint projectFingerprint =
                MigrationProjectFingerprint.CaptureFromInventory(inventory);
            MigrationAssemblyUsage assemblyUsage = ThirdPartyToolMigrationAssemblyUsageAnalyzer.FindMigrationAssemblyUsage(
                projectRoot,
                inventory.CSharpFilePaths,
                inventory.AsmdefFilePaths,
                inventory.AsmrefFilePaths);
            MigrationPlanAccumulator accumulator = new();
            string[] legacyToolInfoAliases = GetAllAssemblyScopedLegacyToolInfoAliases(assemblyUsage);
            ProcessCSharpMigrationFiles(
                inventory.CSharpFilePaths,
                projectRoot,
                assemblyUsage,
                legacyToolInfoAliases,
                ThirdPartyToolMigrationFileAccess.ReadAllText,
                accumulator);

            accumulator.AddReplacementCount(ApplyCrossFilePlayerLoopTimingCallerArgumentMigrations(
                inventory.CSharpFilePaths,
                projectRoot,
                assemblyUsage,
                accumulator.Changes,
                accumulator.RemovedPlayerLoopTimingSignaturesByAssemblyDirectory,
                ThirdPartyToolMigrationFileAccess.ReadAllText));

            ProcessAsmdefMigrationFiles(
                inventory.AsmdefFilePaths,
                projectRoot,
                assemblyUsage,
                ThirdPartyToolMigrationFileAccess.ReadAllText,
                accumulator);

            return accumulator.ToMigrationPlan(projectFingerprint);
        }

        internal static async Task<MigrationPlan> CreateAsync(
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

            return await BuildPlanFromInventoryAsync(projectRoot, inventory, progress, ct);
        }

        /// <summary>
        /// Builds a plan limited to the given assembly directories (e.g. the assemblies containing
        /// compile-error-matched files) instead of scanning the whole project. The full-scan entry
        /// points above (Create/CreateAsync) are untouched and remain the source of truth for the
        /// manual "scan whole project" flow.
        /// </summary>
        internal static async Task<MigrationPlan> CreateInScopeAsync(
            string projectRoot,
            List<string> scopeDirectories,
            IProgress<ThirdPartyToolMigrationProgress> progress,
            CancellationToken ct)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            Debug.Assert(scopeDirectories != null, "scopeDirectories must not be null");
            Debug.Assert(progress != null, "progress must not be null");

            if (!Directory.Exists(projectRoot))
            {
                throw new DirectoryNotFoundException(projectRoot);
            }

            ProjectFileInventory inventory = await ProjectFileInventory.CreateFromDirectoriesAsync(
                scopeDirectories,
                projectRoot,
                progress,
                ct);
            if (ct.IsCancellationRequested)
            {
                return MigrationPlan.Empty;
            }

            return await BuildPlanFromInventoryAsync(projectRoot, inventory, progress, ct);
        }

        private static async Task<MigrationPlan> BuildPlanFromInventoryAsync(
            string projectRoot,
            ProjectFileInventory inventory,
            IProgress<ThirdPartyToolMigrationProgress> progress,
            CancellationToken ct)
        {
            MigrationProjectFingerprint projectFingerprint =
                MigrationProjectFingerprint.CaptureFromInventory(inventory);
            MigrationProgressCounter progressCounter = new(GetPreviewWorkItemCount(inventory), progress);
            ThirdPartyToolMigrationSourceFileCache sourceFileCache = new();
            MigrationAssemblyUsage assemblyUsage = await ThirdPartyToolMigrationAssemblyUsageAsyncAnalyzer.FindMigrationAssemblyUsageAsync(
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

            MigrationPlanAccumulator accumulator = new();
            string[] legacyToolInfoAliases = GetAllAssemblyScopedLegacyToolInfoAliases(assemblyUsage);

            foreach (string csharpFilePath in inventory.CSharpFilePaths)
            {
                if (ct.IsCancellationRequested)
                {
                    return MigrationPlan.Empty;
                }

                string source = sourceFileCache.ReadAllText(csharpFilePath);
                await progressCounter.ReportProcessedItemAsync(ct);
                CSharpMigrationFileResult? csharpResult = CreateCSharpMigrationFileResult(
                    csharpFilePath,
                    source,
                    projectRoot,
                    assemblyUsage,
                    legacyToolInfoAliases);
                accumulator.AddCSharpResult(csharpResult);
            }

            if (ct.IsCancellationRequested)
            {
                return MigrationPlan.Empty;
            }

            accumulator.AddReplacementCount(ApplyCrossFilePlayerLoopTimingCallerArgumentMigrations(
                inventory.CSharpFilePaths,
                projectRoot,
                assemblyUsage,
                accumulator.Changes,
                accumulator.RemovedPlayerLoopTimingSignaturesByAssemblyDirectory,
                sourceFileCache.ReadAllText));

            foreach (string asmdefFilePath in inventory.AsmdefFilePaths)
            {
                if (ct.IsCancellationRequested)
                {
                    return MigrationPlan.Empty;
                }

                string source = sourceFileCache.ReadAllText(asmdefFilePath);
                await progressCounter.ReportProcessedItemAsync(ct);
                MigrationFileResult? asmdefResult = CreateAsmdefMigrationFileResult(
                    asmdefFilePath,
                    source,
                    projectRoot,
                    assemblyUsage);
                accumulator.AddFileResult(asmdefResult);
            }

            progressCounter.ReportComplete();
            return accumulator.ToMigrationPlan(projectFingerprint);
        }

        private static void ProcessCSharpMigrationFiles(
            List<string> csharpFilePaths,
            string projectRoot,
            MigrationAssemblyUsage assemblyUsage,
            string[] legacyToolInfoAliases,
            Func<string, string> readAllText,
            MigrationPlanAccumulator accumulator)
        {
            foreach (string csharpFilePath in csharpFilePaths)
            {
                string source = readAllText(csharpFilePath);
                CSharpMigrationFileResult? result = CreateCSharpMigrationFileResult(
                    csharpFilePath,
                    source,
                    projectRoot,
                    assemblyUsage,
                    legacyToolInfoAliases);
                accumulator.AddCSharpResult(result);
            }
        }

        private static CSharpMigrationFileResult? CreateCSharpMigrationFileResult(
            string csharpFilePath,
            string source,
            string projectRoot,
            MigrationAssemblyUsage assemblyUsage,
            string[] legacyToolInfoAliases)
        {
            if (!ThirdPartyToolMigrationRules.ContainsMigrationCandidateText(source) &&
                !ThirdPartyToolMigrationRules.ContainsLegacyTypeAliasReference(source, legacyToolInfoAliases))
            {
                return null;
            }

            if (ThirdPartyToolMigrationRules.ContainsNonAutoPropertySuccessHidingUnityCliLoopToolResponse(source))
            {
                // why: a getter with logic can't be auto-rewritten safely, so surface it instead of migrating silently.
                UnityEngine.Debug.LogWarning(
                    $"[UnityCliLoop] {csharpFilePath} declares its own non-auto-property Success member that " +
                    "hides UnityCliLoopToolResponse.Success. Migration cannot rewrite it automatically; " +
                    "resolve the member-hiding manually.");
            }

            string assemblyDirectory = FindNearestAssemblyDirectory(
                csharpFilePath,
                assemblyUsage.AsmdefDirectories,
                assemblyUsage.AssemblyReferenceDirectories,
                projectRoot);
            ThirdPartyToolMigrationContentResult result = MigrateCSharpFileSource(
                source,
                assemblyDirectory,
                assemblyUsage);
            return result.Changed
                ? new CSharpMigrationFileResult(csharpFilePath, assemblyDirectory, result)
                : null;
        }

        private static ThirdPartyToolMigrationContentResult MigrateCSharpFileSource(
            string source,
            string assemblyDirectory,
            MigrationAssemblyUsage assemblyUsage)
        {
            string[] legacyAssemblyAliases =
                GetStringArrayFromDirectoryMap(assemblyUsage.AssemblyScopedLegacyAliasesByDirectory, assemblyDirectory);
            bool hasLegacyAssemblySource =
                assemblyUsage.AssemblyScopedLegacyDirectories.Contains(assemblyDirectory) &&
                ThirdPartyToolMigrationRules.ContainsLegacyAssemblyScopedApi(source, legacyAssemblyAliases);

            return ThirdPartyToolMigrationRules.MigrateCSharpSourceForLegacyAssembly(
                source,
                hasLegacyAssemblySource,
                assemblyUsage.AssemblyScopedCurrentToolContractsDirectories.Contains(assemblyDirectory),
                assemblyUsage.AssemblyScopedCurrentApplicationDirectories.Contains(assemblyDirectory),
                assemblyUsage.AssemblyScopedCurrentDomainDirectories.Contains(assemblyDirectory),
                assemblyUsage.AssemblyScopedCurrentFirstPartyToolsDirectories.Contains(assemblyDirectory),
                legacyAssemblyAliases,
                GetStringArrayFromDirectoryMap(
                    assemblyUsage.AssemblyScopedLegacyToolInfoAliasesByDirectory,
                    assemblyDirectory),
                GetStringArrayFromDirectoryMap(
                    assemblyUsage.AssemblyScopedCurrentApplicationAliasesByDirectory,
                    assemblyDirectory),
                GetStringArrayFromDirectoryMap(
                    assemblyUsage.AssemblyScopedCurrentDomainAliasesByDirectory,
                    assemblyDirectory),
                GetStringArrayFromDirectoryMap(
                    assemblyUsage.AssemblyScopedCurrentFirstPartyToolsAliasesByDirectory,
                    assemblyDirectory),
                GetStringArrayFromDirectoryMap(
                    assemblyUsage.AssemblyDeclaredTypeNamesByDirectory,
                    assemblyDirectory));
        }

        private static string[] GetStringArrayFromDirectoryMap(
            Dictionary<string, string[]> namesByDirectory,
            string assemblyDirectory)
        {
            return namesByDirectory.TryGetValue(assemblyDirectory, out string[] names)
                ? names
                : Array.Empty<string>();
        }

        private static void ProcessAsmdefMigrationFiles(
            List<string> asmdefFilePaths,
            string projectRoot,
            MigrationAssemblyUsage assemblyUsage,
            Func<string, string> readAllText,
            MigrationPlanAccumulator accumulator)
        {
            foreach (string asmdefFilePath in asmdefFilePaths)
            {
                string source = readAllText(asmdefFilePath);
                MigrationFileResult? result = CreateAsmdefMigrationFileResult(
                    asmdefFilePath,
                    source,
                    projectRoot,
                    assemblyUsage);
                accumulator.AddFileResult(result);
            }
        }

        private static MigrationFileResult? CreateAsmdefMigrationFileResult(
            string asmdefFilePath,
            string source,
            string projectRoot,
            MigrationAssemblyUsage assemblyUsage)
        {
            bool hasAssemblyMigrationRequirement = TryGetAsmdefMigrationRequirements(
                asmdefFilePath,
                projectRoot,
                assemblyUsage,
                out bool hasLegacyCSharpSource,
                out bool requiresToolContractsReference,
                out bool requiresApplicationReference,
                out bool requiresDomainReference,
                out bool requiresFirstPartyScreenshotReference);
            if (!hasAssemblyMigrationRequirement &&
                !ThirdPartyToolMigrationRules.ContainsLegacyMigrationCandidateText(source))
            {
                return null;
            }

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateAsmdefSource(
                    source,
                    hasLegacyCSharpSource,
                    requiresToolContractsReference,
                    requiresApplicationReference,
                    requiresDomainReference,
                    requiresFirstPartyScreenshotReference);
            return result.Changed
                ? new MigrationFileResult(asmdefFilePath, result.Content, result.ReplacementCount)
                : null;
        }

        internal static int GetPreviewWorkItemCount(ProjectFileInventory inventory)
        {
            Debug.Assert(inventory != null, "inventory must not be null");

            return (inventory.CSharpFilePaths.Count * 3) +
                (inventory.AsmdefFilePaths.Count * 2) +
                inventory.AsmrefFilePaths.Count;
        }

        private sealed class MigrationPlanAccumulator
        {
            public List<MigrationFileChange> Changes { get; } = new();

            public Dictionary<string, List<RemovedLegacyPlayerLoopTimingSignature>>
                RemovedPlayerLoopTimingSignaturesByAssemblyDirectory { get; } = new(StringComparer.Ordinal);

            private int ReplacementCount { get; set; }

            public void AddCSharpResult(CSharpMigrationFileResult? result)
            {
                if (!result.HasValue)
                {
                    return;
                }

                AddReplacementCount(result.Value.Result.ReplacementCount);
                Changes.Add(new MigrationFileChange(result.Value.FilePath, result.Value.Result.Content));
                AddRemovedPlayerLoopTimingSignatures(
                    RemovedPlayerLoopTimingSignaturesByAssemblyDirectory,
                    result.Value.AssemblyDirectory,
                    result.Value.Result.RemovedPlayerLoopTimingSignatures);
            }

            public void AddFileResult(MigrationFileResult? result)
            {
                if (!result.HasValue)
                {
                    return;
                }

                AddReplacementCount(result.Value.ReplacementCount);
                Changes.Add(new MigrationFileChange(result.Value.FilePath, result.Value.Content));
            }

            public void AddReplacementCount(int replacementCount)
            {
                ReplacementCount += replacementCount;
            }

            public MigrationPlan ToMigrationPlan(MigrationProjectFingerprint projectFingerprint)
            {
                return new MigrationPlan(Changes, ReplacementCount, projectFingerprint);
            }
        }

        private readonly struct CSharpMigrationFileResult
        {
            public CSharpMigrationFileResult(
                string filePath,
                string assemblyDirectory,
                ThirdPartyToolMigrationContentResult result)
            {
                FilePath = filePath;
                AssemblyDirectory = assemblyDirectory;
                Result = result;
            }

            public string FilePath { get; }
            public string AssemblyDirectory { get; }
            public ThirdPartyToolMigrationContentResult Result { get; }
        }

        private readonly struct MigrationFileResult
        {
            public MigrationFileResult(string filePath, string content, int replacementCount)
            {
                FilePath = filePath;
                Content = content;
                ReplacementCount = replacementCount;
            }

            public string FilePath { get; }
            public string Content { get; }
            public int ReplacementCount { get; }
        }
    }
}
