using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.Domain;

using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationAssemblyReferenceResolver;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationAsmdefMigrationRequirementResolver;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationFastAssemblyRequirementCollector;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationFastFirstPartyScreenshotRequirementCollector;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationFastSourceTargetDetector;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Scans a project cheaply to decide whether migration work exists.
    /// </summary>
    internal static class ThirdPartyToolMigrationTargetScanner
    {
        internal static async Task<bool> HasMigrationTargetAsync(string projectRoot, CancellationToken ct)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            MigrationTargetPreflightResult preflightResult =
                await ThirdPartyToolMigrationPreflightScanner.FindMigrationTargetAsync(projectRoot, ct);
            if (preflightResult == MigrationTargetPreflightResult.NoTargets)
            {
                return false;
            }

            if (preflightResult == MigrationTargetPreflightResult.HasTargets)
            {
                return true;
            }

            ProjectFileInventory inventory = await ProjectFileInventory.CreateAsync(
                projectRoot,
                new Progress<ThirdPartyToolMigrationProgress>(),
                ct);
            if (ct.IsCancellationRequested)
            {
                return false;
            }

            ThirdPartyToolMigrationAssemblyUsageScanState scanState =
                CreateScanState(projectRoot, inventory);
            (bool hasInitialCSharpTarget, int inspectedEntryCount) =
                await ScanInitialCSharpSourcesAsync(inventory, scanState, ct);
            if (ct.IsCancellationRequested)
            {
                return false;
            }

            if (hasInitialCSharpTarget)
            {
                return true;
            }

            bool hasCSharpSourceTarget =
                await ContainsFastCSharpSourceTargetAsync(inventory, scanState, projectRoot, ct);
            if (ct.IsCancellationRequested)
            {
                return false;
            }

            if (hasCSharpSourceTarget)
            {
                return true;
            }

            bool hasReferenceSourceTarget =
                await CollectCSharpReferenceRequirementsAsync(inventory, scanState, projectRoot, ct);
            if (ct.IsCancellationRequested)
            {
                return false;
            }

            if (hasReferenceSourceTarget)
            {
                return true;
            }

            bool hasAsmdefSourceTarget = await ContainsFastAsmdefSourceTargetAsync(
                inventory,
                inspectedEntryCount,
                ct);
            if (ct.IsCancellationRequested)
            {
                return false;
            }

            if (hasAsmdefSourceTarget)
            {
                return true;
            }

            if (!scanState.HasReferenceRequirements)
            {
                return false;
            }

            MigrationAssemblyUsage assemblyUsage = scanState.CreateReferenceRequirementUsage();
            return ContainsAsmdefReferenceTarget(inventory, projectRoot, assemblyUsage, ct);
        }

        private static ThirdPartyToolMigrationAssemblyUsageScanState CreateScanState(
            string projectRoot,
            ProjectFileInventory inventory)
        {
            List<string> asmdefDirectories = inventory.AsmdefFilePaths
                .Select(path => Path.GetDirectoryName(path) ?? string.Empty)
                .Where(path => !string.IsNullOrEmpty(path))
                .OrderByDescending(path => path.Length)
                .ToList();
            List<AssemblyReferenceDirectory> assemblyReferenceDirectories = inventory.AsmrefFilePaths.Count == 0
                ? new List<AssemblyReferenceDirectory>()
                : CreateAssemblyReferenceDirectories(inventory.AsmdefFilePaths, inventory.AsmrefFilePaths);
            return new ThirdPartyToolMigrationAssemblyUsageScanState(
                projectRoot,
                asmdefDirectories,
                assemblyReferenceDirectories);
        }

        private static async Task<(bool hasTarget, int inspectedEntryCount)> ScanInitialCSharpSourcesAsync(
            ProjectFileInventory inventory,
            ThirdPartyToolMigrationAssemblyUsageScanState scanState,
            CancellationToken ct)
        {
            int inspectedEntryCount = 0;
            foreach (string csharpFilePath in inventory.CSharpFilePaths)
            {
                if (ct.IsCancellationRequested)
                {
                    return (false, inspectedEntryCount);
                }

                string source = ThirdPartyToolMigrationFileAccess.ReadAllText(csharpFilePath);
                if (ContainsFastCSharpMigrationTarget(source))
                {
                    return (true, inspectedEntryCount);
                }

                scanState.RecordTargetScanInitialSourceFacts(source, csharpFilePath);
                inspectedEntryCount++;
                await YieldPreviewProgressAsync(inspectedEntryCount);
            }

            return (false, inspectedEntryCount);
        }

        private static async Task<bool> ContainsFastCSharpSourceTargetAsync(
            ProjectFileInventory inventory,
            ThirdPartyToolMigrationAssemblyUsageScanState scanState,
            string projectRoot,
            CancellationToken ct)
        {
            return await ContainsFastCSharpSourceMigrationTargetAsync(
                inventory.CSharpFilePaths,
                scanState.AsmdefDirectories,
                scanState.AssemblyReferenceDirectories,
                projectRoot,
                scanState.LegacyAssemblyDirectories,
                scanState.AssemblyScopedLegacyDirectories,
                scanState.AssemblyScopedLegacyAliasesByDirectory,
                scanState.AssemblyScopedLegacyToolInfoAliasesByDirectory,
                scanState.AssemblyScopedCurrentToolContractsDirectories,
                scanState.AssemblyScopedCurrentApplicationDirectories,
                scanState.AssemblyScopedCurrentDomainDirectories,
                scanState.AssemblyScopedCurrentFirstPartyToolsDirectories,
                scanState.AssemblyScopedCurrentApplicationAliasesByDirectory,
                scanState.AssemblyScopedCurrentDomainAliasesByDirectory,
                scanState.AssemblyScopedCurrentFirstPartyToolsAliasesByDirectory,
                scanState.AssemblyDeclaredTypeNamesByDirectory,
                ct);
        }

        private static async Task<bool> CollectCSharpReferenceRequirementsAsync(
            ProjectFileInventory inventory,
            ThirdPartyToolMigrationAssemblyUsageScanState scanState,
            string projectRoot,
            CancellationToken ct)
        {
            await CollectBaseReferenceRequirementsAsync(inventory, scanState, projectRoot, ct);
            if (ct.IsCancellationRequested)
            {
                return false;
            }

            await CollectAssemblyScopedReferenceRequirementsAsync(inventory, scanState, projectRoot, ct);
            if (ct.IsCancellationRequested)
            {
                return false;
            }

            return await CollectFastFirstPartyScreenshotRequirementsAsync(
                inventory.CSharpFilePaths,
                scanState.AsmdefDirectories,
                scanState.AssemblyReferenceDirectories,
                projectRoot,
                scanState.LegacyAssemblyDirectories,
                scanState.AssemblyScopedLegacyAliasesByDirectory,
                scanState.AssemblyScopedCurrentToolContractsDirectories,
                scanState.AssemblyScopedCurrentFirstPartyToolsDirectories,
                scanState.AssemblyScopedCurrentFirstPartyToolsAliasesByDirectory,
                scanState.AssemblyDeclaredTypeNamesByDirectory,
                scanState.ToolContractsReferenceAssemblyDirectories,
                scanState.FirstPartyScreenshotReferenceAssemblyDirectories,
                ct);
        }

        private static async Task CollectBaseReferenceRequirementsAsync(
            ProjectFileInventory inventory,
            ThirdPartyToolMigrationAssemblyUsageScanState scanState,
            string projectRoot,
            CancellationToken ct)
        {
            await CollectFastAssemblyReferenceRequirementsAsync(
                inventory.CSharpFilePaths,
                scanState.AsmdefDirectories,
                scanState.AssemblyReferenceDirectories,
                projectRoot,
                scanState.AssemblyScopedCurrentToolContractsDirectories,
                scanState.AssemblyScopedCurrentDomainDirectories,
                scanState.AssemblyScopedCurrentApplicationDirectories,
                scanState.AssemblyScopedCurrentApplicationAliasesByDirectory,
                scanState.AssemblyScopedCurrentDomainAliasesByDirectory,
                scanState.AssemblyDeclaredTypeNamesByDirectory,
                scanState.LegacyAssemblyDirectories,
                scanState.ToolContractsReferenceAssemblyDirectories,
                scanState.ApplicationReferenceAssemblyDirectories,
                scanState.DomainReferenceAssemblyDirectories,
                ct);
        }

        private static async Task CollectAssemblyScopedReferenceRequirementsAsync(
            ProjectFileInventory inventory,
            ThirdPartyToolMigrationAssemblyUsageScanState scanState,
            string projectRoot,
            CancellationToken ct)
        {
            if (scanState.AssemblyScopedCurrentToolContractsDirectories.Count > 0)
            {
                await CollectFastAssemblyScopedCurrentToolContractsRequirementsAsync(
                    inventory.CSharpFilePaths,
                    scanState.AsmdefDirectories,
                    scanState.AssemblyReferenceDirectories,
                    projectRoot,
                    scanState.AssemblyScopedCurrentToolContractsDirectories,
                    scanState.AssemblyScopedCurrentDomainDirectories,
                    scanState.AssemblyScopedCurrentApplicationDirectories,
                    scanState.AssemblyScopedCurrentApplicationAliasesByDirectory,
                    scanState.AssemblyScopedCurrentDomainAliasesByDirectory,
                    scanState.AssemblyDeclaredTypeNamesByDirectory,
                    scanState.LegacyAssemblyDirectories,
                    scanState.ToolContractsReferenceAssemblyDirectories,
                    scanState.ApplicationReferenceAssemblyDirectories,
                    scanState.DomainReferenceAssemblyDirectories,
                    ct);
            }

            if (scanState.AssemblyScopedCurrentDomainDirectories.Count == 0)
            {
                return;
            }

            await CollectFastAssemblyScopedCurrentDomainRequirementsAsync(
                inventory.CSharpFilePaths,
                scanState.AsmdefDirectories,
                scanState.AssemblyReferenceDirectories,
                projectRoot,
                scanState.AssemblyScopedCurrentDomainDirectories,
                scanState.DomainReferenceAssemblyDirectories,
                ct);
        }

        private static async Task<bool> ContainsFastAsmdefSourceTargetAsync(
            ProjectFileInventory inventory,
            int inspectedEntryCount,
            CancellationToken ct)
        {
            int currentInspectedEntryCount = inspectedEntryCount;
            foreach (string asmdefFilePath in inventory.AsmdefFilePaths)
            {
                if (ct.IsCancellationRequested)
                {
                    return false;
                }

                string source = ThirdPartyToolMigrationFileAccess.ReadAllText(asmdefFilePath);
                if (ContainsFastAsmdefMigrationTarget(source))
                {
                    return true;
                }

                currentInspectedEntryCount++;
                await YieldPreviewProgressAsync(currentInspectedEntryCount);
            }

            return false;
        }

        private static bool ContainsAsmdefReferenceTarget(
            ProjectFileInventory inventory,
            string projectRoot,
            MigrationAssemblyUsage assemblyUsage,
            CancellationToken ct)
        {
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

        private static async Task YieldPreviewProgressAsync(int inspectedEntryCount)
        {
            if (inspectedEntryCount % ThirdPartyToolMigrationFileServiceConstants.PreviewYieldBatchSize != 0)
            {
                return;
            }

            await Task.Yield();
        }
    }
}
