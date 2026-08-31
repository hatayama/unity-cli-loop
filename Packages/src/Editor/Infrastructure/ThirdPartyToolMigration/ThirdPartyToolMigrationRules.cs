using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Applies deterministic source rewrites for V2 custom tools that need the V3 public contract API.
    /// </summary>
    internal static class ThirdPartyToolMigrationRules
    {
        internal static ThirdPartyToolMigrationContentResult MigrateCSharpSource(string source)
        {
            return ThirdPartyToolMigrationCSharpRules.MigrateCSharpSource(source);
        }

        internal static ThirdPartyToolMigrationContentResult MigrateCSharpSourceForLegacyAssembly(
            string source,
            bool hasLegacyAssemblySource,
            bool hasAssemblyScopedCurrentToolContractsUsing,
            bool hasAssemblyScopedCurrentApplicationUsing,
            bool hasAssemblyScopedCurrentDomainUsing,
            bool hasAssemblyScopedCurrentFirstPartyToolsUsing,
            string[] legacyAssemblyAliases,
            string[] legacyAssemblyToolInfoAliases,
            string[] currentApplicationAssemblyAliases,
            string[] currentDomainAssemblyAliases,
            string[] currentFirstPartyToolsAssemblyAliases,
            string[] assemblyDeclaredTypeNames)
        {
            return ThirdPartyToolMigrationCSharpRules.MigrateCSharpSourceForLegacyAssembly(
                source,
                hasLegacyAssemblySource,
                hasAssemblyScopedCurrentToolContractsUsing,
                hasAssemblyScopedCurrentApplicationUsing,
                hasAssemblyScopedCurrentDomainUsing,
                hasAssemblyScopedCurrentFirstPartyToolsUsing,
                legacyAssemblyAliases,
                legacyAssemblyToolInfoAliases,
                currentApplicationAssemblyAliases,
                currentDomainAssemblyAliases,
                currentFirstPartyToolsAssemblyAliases,
                assemblyDeclaredTypeNames);
        }

        internal static ThirdPartyToolMigrationContentResult MigrateAsmdefSource(
            string source,
            bool hasLegacyCSharpSource,
            bool requiresToolContractsReference,
            bool requiresApplicationReference,
            bool requiresDomainReference,
            bool requiresFirstPartyScreenshotReference)
        {
            return ThirdPartyToolMigrationAsmdefRules.MigrateAsmdefSource(
                source,
                hasLegacyCSharpSource,
                requiresToolContractsReference,
                requiresApplicationReference,
                requiresDomainReference,
                requiresFirstPartyScreenshotReference);
        }

        internal static bool ContainsLegacyCSharpApi(string source)
        {
            return ThirdPartyToolMigrationSourceDetectionRules.ContainsLegacyCSharpApi(source);
        }

        internal static bool ContainsMigrationCandidateText(string source)
        {
            return ThirdPartyToolMigrationSourceDetectionRules.ContainsMigrationCandidateText(source);
        }

        internal static bool ContainsLegacyMigrationCandidateText(string source)
        {
            return ThirdPartyToolMigrationSourceDetectionRules.ContainsLegacyMigrationCandidateText(source);
        }

        internal static bool ContainsLegacyAsmdefNameReference(string source)
        {
            return ThirdPartyToolMigrationDetectionRules.ContainsLegacyAsmdefNameReference(source);
        }

        internal static bool ContainsLegacyRegistrarApi(string source)
        {
            return ThirdPartyToolMigrationApiDetectionRules.ContainsLegacyRegistrarApi(source);
        }

        internal static bool ContainsLegacyRegistrarApiForAssembly(
            string source,
            bool hasLegacyAssemblySource,
            string[] legacyAssemblyAliases)
        {
            return ThirdPartyToolMigrationApiDetectionRules.ContainsLegacyRegistrarApiForAssembly(source, hasLegacyAssemblySource, legacyAssemblyAliases);
        }

        internal static bool ContainsCurrentRegistrarApi(string source)
        {
            return ThirdPartyToolMigrationApiDetectionRules.ContainsCurrentRegistrarApi(source);
        }

        internal static bool ContainsLegacyApplicationApiForAssembly(
            string source,
            bool hasLegacyAssemblySource,
            string[] legacyAssemblyAliases,
            string[] currentApplicationAssemblyAliases,
            string[] assemblyDeclaredTypeNames)
        {
            return ThirdPartyToolMigrationApiDetectionRules.ContainsLegacyApplicationApiForAssembly(source, hasLegacyAssemblySource, legacyAssemblyAliases, currentApplicationAssemblyAliases, assemblyDeclaredTypeNames);
        }

        internal static bool ContainsCurrentApplicationApi(string source)
        {
            return ThirdPartyToolMigrationApiDetectionRules.ContainsCurrentApplicationApi(source);
        }

        internal static bool ContainsCurrentApplicationApiForAssembly(
            string source,
            bool hasAssemblyScopedCurrentApplicationUsing,
            string[] currentApplicationAssemblyAliases,
            string[] assemblyDeclaredTypeNames)
        {
            return ThirdPartyToolMigrationApiDetectionRules.ContainsCurrentApplicationApiForAssembly(source, hasAssemblyScopedCurrentApplicationUsing, currentApplicationAssemblyAliases, assemblyDeclaredTypeNames);
        }

        internal static bool ContainsRegistrarDomainReturnApi(string source)
        {
            return ThirdPartyToolMigrationApiDetectionRules.ContainsRegistrarDomainReturnApi(source);
        }

        internal static bool ContainsRegistrarDomainReturnApiForAssembly(
            string source,
            bool hasLegacyAssemblySource,
            string[] legacyAssemblyAliases)
        {
            return ThirdPartyToolMigrationApiDetectionRules.ContainsRegistrarDomainReturnApiForAssembly(source, hasLegacyAssemblySource, legacyAssemblyAliases);
        }

        internal static bool ContainsCurrentToolContractsApi(string source)
        {
            return ThirdPartyToolMigrationApiDetectionRules.ContainsCurrentToolContractsApi(source);
        }

        internal static bool ContainsCurrentDomainMetadataApi(string source)
        {
            return ThirdPartyToolMigrationApiDetectionRules.ContainsCurrentDomainMetadataApi(source);
        }

        internal static bool ContainsCurrentDomainMetadataApiForAssembly(
            string source,
            bool hasAssemblyScopedCurrentDomainUsing)
        {
            return ThirdPartyToolMigrationApiDetectionRules.ContainsCurrentDomainMetadataApiForAssembly(source, hasAssemblyScopedCurrentDomainUsing);
        }

        internal static bool ContainsCurrentDomainContractAliasReference(
            string source,
            string[] currentDomainNamespaceAliases)
        {
            return ThirdPartyToolMigrationDomainDetectionRules.ContainsCurrentDomainContractAliasReference(
                source,
                currentDomainNamespaceAliases);
        }

        internal static bool ContainsLegacyFirstPartyScreenshotApiForAssembly(
            string source,
            bool hasLegacyAssemblySource,
            string[] legacyAssemblyAliases,
            string[] assemblyDeclaredTypeNames)
        {
            return ThirdPartyToolMigrationApiDetectionRules.ContainsLegacyFirstPartyScreenshotApiForAssembly(source, hasLegacyAssemblySource, legacyAssemblyAliases, assemblyDeclaredTypeNames);
        }

        internal static bool ContainsLegacyEditorWindowCaptureUtilityMigrationForAssembly(
            string source,
            bool hasLegacyAssemblySource,
            bool hasAssemblyScopedCurrentToolContractsUsing,
            bool hasAssemblyScopedCurrentFirstPartyToolsUsing,
            string[] legacyAssemblyAliases,
            string[] currentFirstPartyToolsAssemblyAliases,
            string[] assemblyDeclaredTypeNames)
        {
            return ThirdPartyToolMigrationApiDetectionRules.ContainsLegacyEditorWindowCaptureUtilityMigrationForAssembly(source, hasLegacyAssemblySource, hasAssemblyScopedCurrentToolContractsUsing, hasAssemblyScopedCurrentFirstPartyToolsUsing, legacyAssemblyAliases, currentFirstPartyToolsAssemblyAliases, assemblyDeclaredTypeNames);
        }

        internal static bool ContainsLegacyEditorWindowCaptureUtilityTimeoutMigrationForAssembly(
            string source,
            bool hasLegacyAssemblySource,
            bool hasAssemblyScopedCurrentToolContractsUsing,
            bool hasAssemblyScopedCurrentFirstPartyToolsUsing,
            string[] legacyAssemblyAliases,
            string[] currentFirstPartyToolsAssemblyAliases,
            string[] assemblyDeclaredTypeNames)
        {
            return ThirdPartyToolMigrationApiDetectionRules.ContainsLegacyEditorWindowCaptureUtilityTimeoutMigrationForAssembly(source, hasLegacyAssemblySource, hasAssemblyScopedCurrentToolContractsUsing, hasAssemblyScopedCurrentFirstPartyToolsUsing, legacyAssemblyAliases, currentFirstPartyToolsAssemblyAliases, assemblyDeclaredTypeNames);
        }

        internal static bool ContainsCurrentFirstPartyScreenshotApiForAssembly(
            string source,
            bool hasAssemblyScopedCurrentFirstPartyToolsUsing,
            string[] currentFirstPartyToolsAssemblyAliases,
            string[] assemblyDeclaredTypeNames)
        {
            return ThirdPartyToolMigrationApiDetectionRules.ContainsCurrentFirstPartyScreenshotApiForAssembly(source, hasAssemblyScopedCurrentFirstPartyToolsUsing, currentFirstPartyToolsAssemblyAliases, assemblyDeclaredTypeNames);
        }

        internal static bool ContainsCurrentFirstPartyScreenshotContractApiForAssembly(
            string source,
            bool hasAssemblyScopedCurrentFirstPartyToolsUsing,
            string[] currentFirstPartyToolsAssemblyAliases,
            string[] assemblyDeclaredTypeNames)
        {
            return ThirdPartyToolMigrationApiDetectionRules.ContainsCurrentFirstPartyScreenshotContractApiForAssembly(source, hasAssemblyScopedCurrentFirstPartyToolsUsing, currentFirstPartyToolsAssemblyAliases, assemblyDeclaredTypeNames);
        }

        internal static bool ContainsCurrentCaptureGameRenderingDeconstructionMigrationForAssembly(
            string source,
            bool hasAssemblyScopedCurrentFirstPartyToolsUsing,
            string[] currentFirstPartyToolsAssemblyAliases,
            string[] assemblyDeclaredTypeNames)
        {
            return ThirdPartyToolMigrationApiDetectionRules.ContainsCurrentCaptureGameRenderingDeconstructionMigrationForAssembly(source, hasAssemblyScopedCurrentFirstPartyToolsUsing, currentFirstPartyToolsAssemblyAliases, assemblyDeclaredTypeNames);
        }

        internal static bool ContainsLegacyAssemblyScopedApi(string source, string[] legacyAssemblyAliases)
        {
            return ThirdPartyToolMigrationSourceDetectionRules.ContainsLegacyAssemblyScopedApi(source, legacyAssemblyAliases);
        }

        internal static bool ContainsSuccessPropertyHidingUnityCliLoopToolResponse(string source)
        {
            return ThirdPartyToolMigrationSuccessPropertyRules.ContainsSuccessPropertyHidingUnityCliLoopToolResponse(source);
        }

        internal static bool ContainsNonAutoPropertySuccessHidingUnityCliLoopToolResponse(string source)
        {
            return ThirdPartyToolMigrationSuccessPropertyRules.ContainsNonAutoPropertySuccessHidingUnityCliLoopToolResponse(source);
        }

        internal static bool ContainsLegacyGlobalUsing(string source)
        {
            return ThirdPartyToolMigrationSourceDetectionRules.ContainsLegacyGlobalUsing(source);
        }

        internal static bool ContainsCurrentDomainGlobalUsing(string source)
        {
            return ThirdPartyToolMigrationSourceDetectionRules.ContainsCurrentDomainGlobalUsing(source);
        }

        internal static bool ContainsCurrentDomainUsing(string source)
        {
            return ThirdPartyToolMigrationSourceDetectionRules.ContainsCurrentDomainUsing(source);
        }

        internal static bool ContainsCurrentDomainNamespaceAlias(string source)
        {
            return ThirdPartyToolMigrationSourceDetectionRules.ContainsCurrentDomainNamespaceAlias(source);
        }

        internal static bool ContainsCurrentToolContractsGlobalUsing(string source)
        {
            return ThirdPartyToolMigrationSourceDetectionRules.ContainsCurrentToolContractsGlobalUsing(source);
        }

        internal static bool ContainsCurrentApplicationGlobalUsing(string source)
        {
            return ThirdPartyToolMigrationSourceDetectionRules.ContainsCurrentApplicationGlobalUsing(source);
        }

        internal static bool ContainsCurrentApplicationUsing(string source)
        {
            return ThirdPartyToolMigrationSourceDetectionRules.ContainsCurrentApplicationUsing(source);
        }

        internal static bool ContainsCurrentApplicationNamespaceAlias(string source)
        {
            return ThirdPartyToolMigrationSourceDetectionRules.ContainsCurrentApplicationNamespaceAlias(source);
        }

        internal static bool ContainsCurrentFirstPartyToolsGlobalUsing(string source)
        {
            return ThirdPartyToolMigrationSourceDetectionRules.ContainsCurrentFirstPartyToolsGlobalUsing(source);
        }

        internal static bool ContainsCurrentFirstPartyToolsNamespaceAlias(string source)
        {
            return ThirdPartyToolMigrationSourceDetectionRules.ContainsCurrentFirstPartyToolsNamespaceAlias(source);
        }

        internal static string[] GetLegacyGlobalNamespaceAliases(string source)
        {
            return ThirdPartyToolMigrationSourceDetectionRules.GetLegacyGlobalNamespaceAliases(source);
        }

        internal static string[] GetLegacyGlobalToolInfoTypeAliases(string source)
        {
            return ThirdPartyToolMigrationSourceDetectionRules.GetLegacyGlobalToolInfoTypeAliases(source);
        }

        internal static string[] GetCurrentApplicationGlobalNamespaceAliases(string source)
        {
            return ThirdPartyToolMigrationSourceDetectionRules.GetCurrentApplicationGlobalNamespaceAliases(source);
        }

        internal static string[] GetCurrentDomainGlobalNamespaceAliases(string source)
        {
            return ThirdPartyToolMigrationSourceDetectionRules.GetCurrentDomainGlobalNamespaceAliases(source);
        }

        internal static string[] GetCurrentFirstPartyToolsGlobalNamespaceAliases(string source)
        {
            return ThirdPartyToolMigrationSourceDetectionRules.GetCurrentFirstPartyToolsGlobalNamespaceAliases(source);
        }

        internal static string[] GetDeclaredTypeNames(string source)
        {
            return ThirdPartyToolMigrationCodeTextDetectionRules.GetDeclaredTypeNames(source);
        }

        internal static bool ContainsLegacyGlobalToolInfoTypeAlias(string source)
        {
            return ThirdPartyToolMigrationSourceDetectionRules.ContainsLegacyGlobalToolInfoTypeAlias(source);
        }

        internal static bool ContainsLegacyTypeAliasReference(string source, string[] aliases)
        {
            return ThirdPartyToolMigrationSourceDetectionRules.ContainsLegacyTypeAliasReference(source, aliases);
        }

        internal static bool IsExcludedDirectoryName(string directoryName)
        {
            return ThirdPartyToolMigrationSourceDetectionRules.IsExcludedDirectoryName(directoryName);
        }

        internal static string[] GetExcludedDirectoryNames()
        {
            return ThirdPartyToolMigrationSourceDetectionRules.GetExcludedDirectoryNames();
        }

        internal static ThirdPartyToolMigrationContentResult RemoveLegacyPlayerLoopTimingCallerArgumentsForLegacyAssembly(
            string source,
            string originalSource,
            RemovedLegacyPlayerLoopTimingSignature[] removedSignatures,
            string[] legacyAssemblyAliases)
        {
            return ThirdPartyToolMigrationTimingCallerRules.RemoveLegacyPlayerLoopTimingCallerArgumentsForLegacyAssembly(
                source,
                originalSource,
                removedSignatures,
                legacyAssemblyAliases);
        }

        internal static ThirdPartyToolMigrationContentResult RemoveLegacyPlayerLoopTimingParametersForLegacyAssembly(
            string source,
            string originalSource,
            string[] legacyAssemblyAliases,
            bool canMigrateBareLegacyPlayerLoopTiming,
            string[] migratedCalleeMethodNames)
        {
            return ThirdPartyToolMigrationTimingCallerRules.RemoveLegacyPlayerLoopTimingParametersForLegacyAssembly(
                source,
                originalSource,
                legacyAssemblyAliases,
                canMigrateBareLegacyPlayerLoopTiming,
                migratedCalleeMethodNames);
        }

    }
}
