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
            List<MigrationFileChange> changes = new();
            int replacementCount = 0;
            string[] legacyToolInfoAliases = GetAllAssemblyScopedLegacyToolInfoAliases(assemblyUsage);
            Dictionary<string, List<ThirdPartyToolMigrationRules.RemovedLegacyPlayerLoopTimingSignature>>
                removedPlayerLoopTimingSignaturesByAssemblyDirectory = new(StringComparer.Ordinal);

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
                string[] currentApplicationAssemblyAliases;
                if (!assemblyUsage.AssemblyScopedCurrentApplicationAliasesByDirectory.TryGetValue(
                        assemblyDirectory,
                        out currentApplicationAssemblyAliases))
                {
                    currentApplicationAssemblyAliases = Array.Empty<string>();
                }
                string[] currentFirstPartyToolsAssemblyAliases;
                if (!assemblyUsage.AssemblyScopedCurrentFirstPartyToolsAliasesByDirectory.TryGetValue(
                        assemblyDirectory,
                        out currentFirstPartyToolsAssemblyAliases))
                {
                    currentFirstPartyToolsAssemblyAliases = Array.Empty<string>();
                }
                string[] assemblyDeclaredTypeNames;
                if (!assemblyUsage.AssemblyDeclaredTypeNamesByDirectory.TryGetValue(
                        assemblyDirectory,
                        out assemblyDeclaredTypeNames))
                {
                    assemblyDeclaredTypeNames = Array.Empty<string>();
                }
                bool hasAssemblyScopedCurrentToolContractsUsing =
                    assemblyUsage.AssemblyScopedCurrentToolContractsDirectories.Contains(assemblyDirectory);
                bool hasAssemblyScopedCurrentApplicationUsing =
                    assemblyUsage.AssemblyScopedCurrentApplicationDirectories.Contains(assemblyDirectory);
                bool hasAssemblyScopedCurrentFirstPartyToolsUsing =
                    assemblyUsage.AssemblyScopedCurrentFirstPartyToolsDirectories.Contains(assemblyDirectory);
                bool hasLegacyAssemblySource =
                    assemblyUsage.AssemblyScopedLegacyDirectories.Contains(assemblyDirectory) &&
                    ThirdPartyToolMigrationRules.ContainsLegacyAssemblyScopedApi(source, legacyAssemblyAliases);
                ThirdPartyToolMigrationContentResult result =
                    ThirdPartyToolMigrationRules.MigrateCSharpSourceForLegacyAssembly(
                        source,
                        hasLegacyAssemblySource,
                        hasAssemblyScopedCurrentToolContractsUsing,
                        hasAssemblyScopedCurrentApplicationUsing,
                        hasAssemblyScopedCurrentFirstPartyToolsUsing,
                        legacyAssemblyAliases,
                        legacyAssemblyToolInfoAliases,
                        currentApplicationAssemblyAliases,
                        currentFirstPartyToolsAssemblyAliases,
                        assemblyDeclaredTypeNames);
                if (!result.Changed)
                {
                    continue;
                }

                replacementCount += result.ReplacementCount;
                changes.Add(new MigrationFileChange(csharpFilePath, result.Content));
                AddRemovedPlayerLoopTimingSignatures(
                    removedPlayerLoopTimingSignaturesByAssemblyDirectory,
                    assemblyDirectory,
                    result.RemovedPlayerLoopTimingSignatures);
            }

            replacementCount += ApplyCrossFilePlayerLoopTimingCallerArgumentMigrations(
                inventory.CSharpFilePaths,
                projectRoot,
                assemblyUsage,
                changes,
                removedPlayerLoopTimingSignaturesByAssemblyDirectory,
                File.ReadAllText);

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

            return new MigrationPlan(
                changes,
                replacementCount,
                projectFingerprint);
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

            List<MigrationFileChange> changes = new();
            int replacementCount = 0;
            string[] legacyToolInfoAliases = GetAllAssemblyScopedLegacyToolInfoAliases(assemblyUsage);
            Dictionary<string, List<ThirdPartyToolMigrationRules.RemovedLegacyPlayerLoopTimingSignature>>
                removedPlayerLoopTimingSignaturesByAssemblyDirectory = new(StringComparer.Ordinal);

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
                string[] currentApplicationAssemblyAliases;
                if (!assemblyUsage.AssemblyScopedCurrentApplicationAliasesByDirectory.TryGetValue(
                        assemblyDirectory,
                        out currentApplicationAssemblyAliases))
                {
                    currentApplicationAssemblyAliases = Array.Empty<string>();
                }
                string[] currentFirstPartyToolsAssemblyAliases;
                if (!assemblyUsage.AssemblyScopedCurrentFirstPartyToolsAliasesByDirectory.TryGetValue(
                        assemblyDirectory,
                        out currentFirstPartyToolsAssemblyAliases))
                {
                    currentFirstPartyToolsAssemblyAliases = Array.Empty<string>();
                }
                string[] assemblyDeclaredTypeNames;
                if (!assemblyUsage.AssemblyDeclaredTypeNamesByDirectory.TryGetValue(
                        assemblyDirectory,
                        out assemblyDeclaredTypeNames))
                {
                    assemblyDeclaredTypeNames = Array.Empty<string>();
                }
                bool hasAssemblyScopedCurrentToolContractsUsing =
                    assemblyUsage.AssemblyScopedCurrentToolContractsDirectories.Contains(assemblyDirectory);
                bool hasAssemblyScopedCurrentApplicationUsing =
                    assemblyUsage.AssemblyScopedCurrentApplicationDirectories.Contains(assemblyDirectory);
                bool hasAssemblyScopedCurrentFirstPartyToolsUsing =
                    assemblyUsage.AssemblyScopedCurrentFirstPartyToolsDirectories.Contains(assemblyDirectory);
                bool hasLegacyAssemblySource =
                    assemblyUsage.AssemblyScopedLegacyDirectories.Contains(assemblyDirectory) &&
                    ThirdPartyToolMigrationRules.ContainsLegacyAssemblyScopedApi(source, legacyAssemblyAliases);
                ThirdPartyToolMigrationContentResult result =
                    ThirdPartyToolMigrationRules.MigrateCSharpSourceForLegacyAssembly(
                        source,
                        hasLegacyAssemblySource,
                        hasAssemblyScopedCurrentToolContractsUsing,
                        hasAssemblyScopedCurrentApplicationUsing,
                        hasAssemblyScopedCurrentFirstPartyToolsUsing,
                        legacyAssemblyAliases,
                        legacyAssemblyToolInfoAliases,
                        currentApplicationAssemblyAliases,
                        currentFirstPartyToolsAssemblyAliases,
                        assemblyDeclaredTypeNames);
                if (!result.Changed)
                {
                    continue;
                }

                replacementCount += result.ReplacementCount;
                changes.Add(new MigrationFileChange(csharpFilePath, result.Content));
                AddRemovedPlayerLoopTimingSignatures(
                    removedPlayerLoopTimingSignaturesByAssemblyDirectory,
                    assemblyDirectory,
                    result.RemovedPlayerLoopTimingSignatures);
            }

            if (ct.IsCancellationRequested)
            {
                return MigrationPlan.Empty;
            }

            replacementCount += ApplyCrossFilePlayerLoopTimingCallerArgumentMigrations(
                inventory.CSharpFilePaths,
                projectRoot,
                assemblyUsage,
                changes,
                removedPlayerLoopTimingSignaturesByAssemblyDirectory,
                sourceFileCache.ReadAllText);

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
            return new MigrationPlan(
                changes,
                replacementCount,
                projectFingerprint);
        }


        internal static int GetPreviewWorkItemCount(ProjectFileInventory inventory)
        {
            Debug.Assert(inventory != null, "inventory must not be null");

            return (inventory.CSharpFilePaths.Count * 3) +
                (inventory.AsmdefFilePaths.Count * 2) +
                inventory.AsmrefFilePaths.Count;
        }
    }
}
