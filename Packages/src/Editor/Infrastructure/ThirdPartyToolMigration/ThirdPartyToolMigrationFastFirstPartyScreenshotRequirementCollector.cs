using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationAssemblyReferenceResolver;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationAssemblyScopedNameMap;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Collects first-party screenshot API requirements during fast migration-target scans.
    /// </summary>
    internal static class ThirdPartyToolMigrationFastFirstPartyScreenshotRequirementCollector
    {
        internal static async Task<bool> CollectFastFirstPartyScreenshotRequirementsAsync(
            List<string> csharpFilePaths,
            List<string> asmdefDirectories,
            List<AssemblyReferenceDirectory> assemblyReferenceDirectories,
            string projectRoot,
            HashSet<string> legacyAssemblyDirectories,
            Dictionary<string, HashSet<string>> assemblyScopedLegacyAliasesByDirectory,
            HashSet<string> assemblyScopedCurrentToolContractsDirectories,
            HashSet<string> assemblyScopedCurrentFirstPartyToolsDirectories,
            Dictionary<string, HashSet<string>> assemblyScopedCurrentFirstPartyToolsAliasesByDirectory,
            Dictionary<string, HashSet<string>> assemblyDeclaredTypeNamesByDirectory,
            HashSet<string> toolContractsReferenceAssemblyDirectories,
            HashSet<string> firstPartyScreenshotReferenceAssemblyDirectories,
            CancellationToken ct)
        {
            Debug.Assert(csharpFilePaths != null, "csharpFilePaths must not be null");
            Debug.Assert(asmdefDirectories != null, "asmdefDirectories must not be null");
            Debug.Assert(
                assemblyReferenceDirectories != null,
                "assemblyReferenceDirectories must not be null");
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            Debug.Assert(legacyAssemblyDirectories != null, "legacyAssemblyDirectories must not be null");
            Debug.Assert(
                assemblyScopedLegacyAliasesByDirectory != null,
                "assemblyScopedLegacyAliasesByDirectory must not be null");
            Debug.Assert(
                assemblyScopedCurrentToolContractsDirectories != null,
                "assemblyScopedCurrentToolContractsDirectories must not be null");
            Debug.Assert(
                assemblyScopedCurrentFirstPartyToolsDirectories != null,
                "assemblyScopedCurrentFirstPartyToolsDirectories must not be null");
            Debug.Assert(
                assemblyScopedCurrentFirstPartyToolsAliasesByDirectory != null,
                "assemblyScopedCurrentFirstPartyToolsAliasesByDirectory must not be null");
            Debug.Assert(
                assemblyDeclaredTypeNamesByDirectory != null,
                "assemblyDeclaredTypeNamesByDirectory must not be null");
            Debug.Assert(
                toolContractsReferenceAssemblyDirectories != null,
                "toolContractsReferenceAssemblyDirectories must not be null");
            Debug.Assert(
                firstPartyScreenshotReferenceAssemblyDirectories != null,
                "firstPartyScreenshotReferenceAssemblyDirectories must not be null");

            int inspectedEntryCount = 0;
            foreach (string csharpFilePath in csharpFilePaths)
            {
                if (ct.IsCancellationRequested)
                {
                    return false;
                }

                string source = ThirdPartyToolMigrationFileAccess.ReadAllText(csharpFilePath);
                inspectedEntryCount++;
                if (inspectedEntryCount % ThirdPartyToolMigrationFileServiceConstants.PreviewYieldBatchSize == 0)
                {
                    await Task.Yield();
                }

                if (!ThirdPartyToolMigrationRules.ContainsMigrationCandidateText(source))
                {
                    continue;
                }

                string assemblyDirectory = FindNearestAssemblyDirectory(
                    csharpFilePath,
                    asmdefDirectories,
                    assemblyReferenceDirectories,
                    projectRoot);
                bool foundMigrationTarget = CollectFastFirstPartyScreenshotRequirementsForSource(
                    source,
                    assemblyDirectory,
                    legacyAssemblyDirectories,
                    assemblyScopedLegacyAliasesByDirectory,
                    assemblyScopedCurrentToolContractsDirectories,
                    assemblyScopedCurrentFirstPartyToolsDirectories,
                    assemblyScopedCurrentFirstPartyToolsAliasesByDirectory,
                    assemblyDeclaredTypeNamesByDirectory,
                    toolContractsReferenceAssemblyDirectories,
                    firstPartyScreenshotReferenceAssemblyDirectories);
                if (foundMigrationTarget)
                {
                    return true;
                }

            }

            return false;
        }

        private static bool CollectFastFirstPartyScreenshotRequirementsForSource(
            string source,
            string assemblyDirectory,
            HashSet<string> legacyAssemblyDirectories,
            Dictionary<string, HashSet<string>> assemblyScopedLegacyAliasesByDirectory,
            HashSet<string> assemblyScopedCurrentToolContractsDirectories,
            HashSet<string> assemblyScopedCurrentFirstPartyToolsDirectories,
            Dictionary<string, HashSet<string>> assemblyScopedCurrentFirstPartyToolsAliasesByDirectory,
            Dictionary<string, HashSet<string>> assemblyDeclaredTypeNamesByDirectory,
            HashSet<string> toolContractsReferenceAssemblyDirectories,
            HashSet<string> firstPartyScreenshotReferenceAssemblyDirectories)
        {
            string[] assemblyDeclaredTypeNames =
                GetAssemblyScopedNames(assemblyDeclaredTypeNamesByDirectory, assemblyDirectory);
            string[] legacyAssemblyAliases =
                GetAssemblyScopedNames(assemblyScopedLegacyAliasesByDirectory, assemblyDirectory);
            string[] currentFirstPartyToolsAssemblyAliases =
                GetAssemblyScopedNames(assemblyScopedCurrentFirstPartyToolsAliasesByDirectory, assemblyDirectory);
            FirstPartyScreenshotRequirementScan scan = ScanFastFirstPartyScreenshotRequirement(
                source,
                assemblyDirectory,
                legacyAssemblyDirectories,
                assemblyScopedCurrentToolContractsDirectories,
                assemblyScopedCurrentFirstPartyToolsDirectories,
                legacyAssemblyAliases,
                currentFirstPartyToolsAssemblyAliases,
                assemblyDeclaredTypeNames);

            if (scan.RequiresToolContractsReference)
            {
                toolContractsReferenceAssemblyDirectories.Add(assemblyDirectory);
            }

            if (scan.RequiresFirstPartyScreenshotReference)
            {
                firstPartyScreenshotReferenceAssemblyDirectories.Add(assemblyDirectory);
            }

            return scan.HasMigrationTarget;
        }

        private static FirstPartyScreenshotRequirementScan ScanFastFirstPartyScreenshotRequirement(
            string source,
            string assemblyDirectory,
            HashSet<string> legacyAssemblyDirectories,
            HashSet<string> assemblyScopedCurrentToolContractsDirectories,
            HashSet<string> assemblyScopedCurrentFirstPartyToolsDirectories,
            string[] legacyAssemblyAliases,
            string[] currentFirstPartyToolsAssemblyAliases,
            string[] assemblyDeclaredTypeNames)
        {
            bool hasLegacyEditorWindowCaptureUtilitySourceTarget =
                HasLegacyEditorWindowCaptureUtilitySourceTarget(
                    source,
                    assemblyDirectory,
                    legacyAssemblyDirectories,
                    assemblyScopedCurrentToolContractsDirectories,
                    assemblyScopedCurrentFirstPartyToolsDirectories,
                    legacyAssemblyAliases,
                    currentFirstPartyToolsAssemblyAliases,
                    assemblyDeclaredTypeNames);
            bool hasLegacyScreenshotSourceTarget = HasLegacyScreenshotSourceTarget(
                source,
                assemblyDirectory,
                legacyAssemblyDirectories,
                assemblyScopedCurrentToolContractsDirectories,
                legacyAssemblyAliases,
                assemblyDeclaredTypeNames,
                hasLegacyEditorWindowCaptureUtilitySourceTarget);
            bool hasCurrentScreenshotReferenceRequirement =
                ThirdPartyToolMigrationRules.ContainsCurrentFirstPartyScreenshotApiForAssembly(
                    source,
                    assemblyScopedCurrentFirstPartyToolsDirectories.Contains(assemblyDirectory),
                    currentFirstPartyToolsAssemblyAliases,
                    assemblyDeclaredTypeNames);
            bool hasCurrentFirstPartyToolsContractSourceTarget =
                ThirdPartyToolMigrationRules.ContainsCurrentFirstPartyScreenshotContractApiForAssembly(
                    source,
                    assemblyScopedCurrentFirstPartyToolsDirectories.Contains(assemblyDirectory),
                    currentFirstPartyToolsAssemblyAliases,
                    assemblyDeclaredTypeNames);
            bool hasCurrentRenderingCaptureSourceTarget =
                ThirdPartyToolMigrationRules.ContainsCurrentCaptureGameRenderingDeconstructionMigrationForAssembly(
                    source,
                    assemblyScopedCurrentFirstPartyToolsDirectories.Contains(assemblyDirectory),
                    currentFirstPartyToolsAssemblyAliases,
                    assemblyDeclaredTypeNames);
            bool hasTimeoutMigration = HasLegacyEditorWindowCaptureUtilityTimeoutMigration(
                source,
                assemblyDirectory,
                legacyAssemblyDirectories,
                assemblyScopedCurrentToolContractsDirectories,
                assemblyScopedCurrentFirstPartyToolsDirectories,
                legacyAssemblyAliases,
                currentFirstPartyToolsAssemblyAliases,
                assemblyDeclaredTypeNames);
            return new FirstPartyScreenshotRequirementScan(
                hasLegacyScreenshotSourceTarget ||
                hasCurrentFirstPartyToolsContractSourceTarget ||
                hasCurrentScreenshotReferenceRequirement ||
                hasTimeoutMigration,
                hasCurrentRenderingCaptureSourceTarget ||
                hasCurrentScreenshotReferenceRequirement,
                hasLegacyScreenshotSourceTarget ||
                hasCurrentRenderingCaptureSourceTarget ||
                hasCurrentFirstPartyToolsContractSourceTarget ||
                hasTimeoutMigration);
        }

        private static bool HasLegacyEditorWindowCaptureUtilitySourceTarget(
            string source,
            string assemblyDirectory,
            HashSet<string> legacyAssemblyDirectories,
            HashSet<string> assemblyScopedCurrentToolContractsDirectories,
            HashSet<string> assemblyScopedCurrentFirstPartyToolsDirectories,
            string[] legacyAssemblyAliases,
            string[] currentFirstPartyToolsAssemblyAliases,
            string[] assemblyDeclaredTypeNames)
        {
            return ThirdPartyToolMigrationRules.ContainsLegacyEditorWindowCaptureUtilityMigrationForAssembly(
                source,
                legacyAssemblyDirectories.Contains(assemblyDirectory),
                assemblyScopedCurrentToolContractsDirectories.Contains(assemblyDirectory),
                assemblyScopedCurrentFirstPartyToolsDirectories.Contains(assemblyDirectory),
                legacyAssemblyAliases,
                currentFirstPartyToolsAssemblyAliases,
                assemblyDeclaredTypeNames);
        }

        private static bool HasLegacyScreenshotSourceTarget(
            string source,
            string assemblyDirectory,
            HashSet<string> legacyAssemblyDirectories,
            HashSet<string> assemblyScopedCurrentToolContractsDirectories,
            string[] legacyAssemblyAliases,
            string[] assemblyDeclaredTypeNames,
            bool hasLegacyEditorWindowCaptureUtilitySourceTarget)
        {
            return ThirdPartyToolMigrationRules.ContainsLegacyFirstPartyScreenshotApiForAssembly(
                source,
                legacyAssemblyDirectories.Contains(assemblyDirectory) ||
                assemblyScopedCurrentToolContractsDirectories.Contains(assemblyDirectory) ||
                ThirdPartyToolMigrationRules.ContainsCurrentToolContractsApi(source),
                legacyAssemblyAliases,
                assemblyDeclaredTypeNames) ||
                hasLegacyEditorWindowCaptureUtilitySourceTarget;
        }

        private static bool HasLegacyEditorWindowCaptureUtilityTimeoutMigration(
            string source,
            string assemblyDirectory,
            HashSet<string> legacyAssemblyDirectories,
            HashSet<string> assemblyScopedCurrentToolContractsDirectories,
            HashSet<string> assemblyScopedCurrentFirstPartyToolsDirectories,
            string[] legacyAssemblyAliases,
            string[] currentFirstPartyToolsAssemblyAliases,
            string[] assemblyDeclaredTypeNames)
        {
            return ThirdPartyToolMigrationRules.ContainsLegacyEditorWindowCaptureUtilityTimeoutMigrationForAssembly(
                source,
                legacyAssemblyDirectories.Contains(assemblyDirectory),
                assemblyScopedCurrentToolContractsDirectories.Contains(assemblyDirectory),
                assemblyScopedCurrentFirstPartyToolsDirectories.Contains(assemblyDirectory),
                legacyAssemblyAliases,
                currentFirstPartyToolsAssemblyAliases,
                assemblyDeclaredTypeNames);
        }

        private readonly struct FirstPartyScreenshotRequirementScan
        {
            public FirstPartyScreenshotRequirementScan(
                bool requiresToolContractsReference,
                bool requiresFirstPartyScreenshotReference,
                bool hasMigrationTarget)
            {
                RequiresToolContractsReference = requiresToolContractsReference;
                RequiresFirstPartyScreenshotReference = requiresFirstPartyScreenshotReference;
                HasMigrationTarget = hasMigrationTarget;
            }

            public bool RequiresToolContractsReference { get; }
            public bool RequiresFirstPartyScreenshotReference { get; }
            public bool HasMigrationTarget { get; }
        }
    }
}
