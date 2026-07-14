using System.Diagnostics;

using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationAssemblyScopedNameMap;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Records asmdef reference requirements from a single source file against scan-state collections.
    /// </summary>
    internal static class ThirdPartyToolMigrationAssemblyUsageReferenceRequirementRecorder
    {
        public static void Record(
            ThirdPartyToolMigrationAssemblyUsageScanState scanState,
            string source,
            string assemblyDirectory)
        {
            Debug.Assert(scanState != null, "scanState must not be null");
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(!string.IsNullOrEmpty(assemblyDirectory), "assemblyDirectory must not be null or empty");

            string[] legacyAssemblyAliases = GetAssemblyScopedNames(
                scanState.AssemblyScopedLegacyAliasesByDirectory,
                assemblyDirectory);
            string[] assemblyDeclaredTypeNames = GetAssemblyScopedNames(
                scanState.AssemblyDeclaredTypeNamesByDirectory,
                assemblyDirectory);
            bool hasLegacyCSharpApi = ThirdPartyToolMigrationRules.ContainsLegacyCSharpApi(source);
            RecordToolContractsRequirement(
                scanState,
                source,
                assemblyDirectory,
                legacyAssemblyAliases,
                assemblyDeclaredTypeNames,
                hasLegacyCSharpApi);
            RecordRegistrarDomainReturnRequirement(scanState, source, assemblyDirectory, legacyAssemblyAliases);
            RecordDomainContractRequirement(scanState, source, assemblyDirectory);
            RecordFirstPartyScreenshotRequirement(
                scanState,
                source,
                assemblyDirectory,
                legacyAssemblyAliases,
                assemblyDeclaredTypeNames,
                hasLegacyCSharpApi);
            RecordEditorWindowCaptureTimeoutRequirement(
                scanState,
                source,
                assemblyDirectory,
                legacyAssemblyAliases,
                assemblyDeclaredTypeNames,
                hasLegacyCSharpApi);
        }

        private static void RecordToolContractsRequirement(
            ThirdPartyToolMigrationAssemblyUsageScanState scanState,
            string source,
            string assemblyDirectory,
            string[] legacyAssemblyAliases,
            string[] assemblyDeclaredTypeNames,
            bool hasLegacyCSharpApi)
        {
            string[] currentApplicationAssemblyAliases = GetAssemblyScopedNames(
                scanState.AssemblyScopedCurrentApplicationAliasesByDirectory,
                assemblyDirectory);
            bool hasCurrentApplicationSourceTarget =
                ThirdPartyToolMigrationRules.ContainsCurrentApplicationApiForAssembly(
                    source,
                    scanState.AssemblyScopedCurrentApplicationDirectories.Contains(assemblyDirectory),
                    currentApplicationAssemblyAliases,
                    assemblyDeclaredTypeNames);
            if (!ContainsToolContractsReferenceRequirement(
                    scanState,
                    source,
                    assemblyDirectory,
                    legacyAssemblyAliases,
                    currentApplicationAssemblyAliases,
                    assemblyDeclaredTypeNames,
                    hasLegacyCSharpApi,
                    hasCurrentApplicationSourceTarget))
            {
                return;
            }

            scanState.ToolContractsReferenceAssemblyDirectories.Add(assemblyDirectory);
        }

        private static bool ContainsToolContractsReferenceRequirement(
            ThirdPartyToolMigrationAssemblyUsageScanState scanState,
            string source,
            string assemblyDirectory,
            string[] legacyAssemblyAliases,
            string[] currentApplicationAssemblyAliases,
            string[] assemblyDeclaredTypeNames,
            bool hasLegacyCSharpApi,
            bool hasCurrentApplicationSourceTarget)
        {
            bool canUseLegacyAssemblyApi =
                scanState.AssemblyScopedLegacyDirectories.Contains(assemblyDirectory) ||
                hasLegacyCSharpApi ||
                ThirdPartyToolMigrationRules.ContainsCurrentToolContractsApi(source) ||
                scanState.AssemblyScopedCurrentToolContractsDirectories.Contains(assemblyDirectory) ||
                scanState.AssemblyScopedCurrentApplicationDirectories.Contains(assemblyDirectory);
            return ThirdPartyToolMigrationRules.ContainsLegacyRegistrarApiForAssembly(
                    source,
                    scanState.AssemblyScopedLegacyDirectories.Contains(assemblyDirectory),
                    legacyAssemblyAliases) ||
                ThirdPartyToolMigrationRules.ContainsLegacyApplicationApiForAssembly(
                    source,
                    canUseLegacyAssemblyApi,
                    legacyAssemblyAliases,
                    currentApplicationAssemblyAliases,
                    assemblyDeclaredTypeNames) ||
                hasCurrentApplicationSourceTarget;
        }

        private static void RecordRegistrarDomainReturnRequirement(
            ThirdPartyToolMigrationAssemblyUsageScanState scanState,
            string source,
            string assemblyDirectory,
            string[] legacyAssemblyAliases)
        {
            if (!ThirdPartyToolMigrationRules.ContainsRegistrarDomainReturnApiForAssembly(
                    source,
                    scanState.AssemblyScopedLegacyDirectories.Contains(assemblyDirectory),
                    legacyAssemblyAliases))
            {
                return;
            }

            scanState.ToolContractsReferenceAssemblyDirectories.Add(assemblyDirectory);
        }

        private static void RecordDomainContractRequirement(
            ThirdPartyToolMigrationAssemblyUsageScanState scanState,
            string source,
            string assemblyDirectory)
        {
            string[] currentDomainAssemblyAliases = GetAssemblyScopedNames(
                scanState.AssemblyScopedCurrentDomainAliasesByDirectory,
                assemblyDirectory);
            string[] currentDomainNamespaceAliases =
                ThirdPartyToolMigrationAliasRules.GetCombinedCurrentDomainNamespaceAliases(
                    source,
                    currentDomainAssemblyAliases);
            if (!ThirdPartyToolMigrationRules.ContainsCurrentDomainMetadataApiForAssembly(
                    source,
                    scanState.AssemblyScopedCurrentDomainDirectories.Contains(assemblyDirectory)) &&
                !ThirdPartyToolMigrationRules.ContainsCurrentDomainContractAliasReference(
                    source,
                    currentDomainNamespaceAliases))
            {
                return;
            }

            scanState.ToolContractsReferenceAssemblyDirectories.Add(assemblyDirectory);
        }

        private static void RecordFirstPartyScreenshotRequirement(
            ThirdPartyToolMigrationAssemblyUsageScanState scanState,
            string source,
            string assemblyDirectory,
            string[] legacyAssemblyAliases,
            string[] assemblyDeclaredTypeNames,
            bool hasLegacyCSharpApi)
        {
            string[] currentFirstPartyToolsAssemblyAliases = GetAssemblyScopedNames(
                scanState.AssemblyScopedCurrentFirstPartyToolsAliasesByDirectory,
                assemblyDirectory);
            bool hasCurrentFirstPartyScreenshotReferenceRequirement =
                ThirdPartyToolMigrationRules.ContainsCurrentFirstPartyScreenshotApiForAssembly(
                    source,
                    scanState.AssemblyScopedCurrentFirstPartyToolsDirectories.Contains(assemblyDirectory),
                    currentFirstPartyToolsAssemblyAliases,
                    assemblyDeclaredTypeNames);
            if (ContainsFirstPartyScreenshotToolContractsRequirement(
                scanState,
                source,
                assemblyDirectory,
                legacyAssemblyAliases,
                currentFirstPartyToolsAssemblyAliases,
                assemblyDeclaredTypeNames,
                hasLegacyCSharpApi,
                hasCurrentFirstPartyScreenshotReferenceRequirement))
            {
                scanState.ToolContractsReferenceAssemblyDirectories.Add(assemblyDirectory);
            }

            if (hasCurrentFirstPartyScreenshotReferenceRequirement)
            {
                scanState.FirstPartyScreenshotReferenceAssemblyDirectories.Add(assemblyDirectory);
            }
        }

        private static bool ContainsFirstPartyScreenshotToolContractsRequirement(
            ThirdPartyToolMigrationAssemblyUsageScanState scanState,
            string source,
            string assemblyDirectory,
            string[] legacyAssemblyAliases,
            string[] currentFirstPartyToolsAssemblyAliases,
            string[] assemblyDeclaredTypeNames,
            bool hasLegacyCSharpApi,
            bool hasCurrentFirstPartyScreenshotReferenceRequirement)
        {
            bool hasAssemblyScopedCurrentToolContractsUsing =
                scanState.AssemblyScopedCurrentToolContractsDirectories.Contains(assemblyDirectory);
            bool hasAssemblyScopedCurrentFirstPartyToolsUsing =
                scanState.AssemblyScopedCurrentFirstPartyToolsDirectories.Contains(assemblyDirectory);
            return ThirdPartyToolMigrationRules.ContainsLegacyFirstPartyScreenshotApiForAssembly(
                    source,
                    scanState.AssemblyScopedLegacyDirectories.Contains(assemblyDirectory) ||
                    hasLegacyCSharpApi ||
                    ThirdPartyToolMigrationRules.ContainsCurrentToolContractsApi(source) ||
                    hasAssemblyScopedCurrentToolContractsUsing,
                    legacyAssemblyAliases,
                    assemblyDeclaredTypeNames) ||
                ThirdPartyToolMigrationRules.ContainsLegacyEditorWindowCaptureUtilityMigrationForAssembly(
                    source,
                    scanState.AssemblyScopedLegacyDirectories.Contains(assemblyDirectory) || hasLegacyCSharpApi,
                    hasAssemblyScopedCurrentToolContractsUsing,
                    hasAssemblyScopedCurrentFirstPartyToolsUsing,
                    legacyAssemblyAliases,
                    currentFirstPartyToolsAssemblyAliases,
                    assemblyDeclaredTypeNames) ||
                ThirdPartyToolMigrationRules.ContainsCurrentFirstPartyScreenshotContractApiForAssembly(
                    source,
                    hasAssemblyScopedCurrentFirstPartyToolsUsing,
                    currentFirstPartyToolsAssemblyAliases,
                    assemblyDeclaredTypeNames) ||
                hasCurrentFirstPartyScreenshotReferenceRequirement;
        }

        private static void RecordEditorWindowCaptureTimeoutRequirement(
            ThirdPartyToolMigrationAssemblyUsageScanState scanState,
            string source,
            string assemblyDirectory,
            string[] legacyAssemblyAliases,
            string[] assemblyDeclaredTypeNames,
            bool hasLegacyCSharpApi)
        {
            if (!ThirdPartyToolMigrationRules.ContainsLegacyEditorWindowCaptureUtilityTimeoutMigrationForAssembly(
                    source,
                    scanState.AssemblyScopedLegacyDirectories.Contains(assemblyDirectory) || hasLegacyCSharpApi,
                    scanState.AssemblyScopedCurrentToolContractsDirectories.Contains(assemblyDirectory),
                    scanState.AssemblyScopedCurrentFirstPartyToolsDirectories.Contains(assemblyDirectory),
                    legacyAssemblyAliases,
                    GetAssemblyScopedNames(
                        scanState.AssemblyScopedCurrentFirstPartyToolsAliasesByDirectory,
                        assemblyDirectory),
                    assemblyDeclaredTypeNames))
            {
                return;
            }

            scanState.ToolContractsReferenceAssemblyDirectories.Add(assemblyDirectory);
        }
    }
}
