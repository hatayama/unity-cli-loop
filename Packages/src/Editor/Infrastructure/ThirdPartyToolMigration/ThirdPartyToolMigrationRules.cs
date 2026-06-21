using System;
using System.Diagnostics;

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
            return ThirdPartyToolMigrationDetectionRules.ContainsLegacyCSharpApi(source);
        }

        internal static bool ContainsMigrationCandidateText(string source)
        {
            return ThirdPartyToolMigrationDetectionRules.ContainsMigrationCandidateText(source);
        }

        internal static bool ContainsLegacyMigrationCandidateText(string source)
        {
            return ThirdPartyToolMigrationDetectionRules.ContainsLegacyMigrationCandidateText(source);
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

        internal static bool ContainsLegacyDomainMetadataApi(string source)
        {
            return ThirdPartyToolMigrationApiDetectionRules.ContainsLegacyDomainMetadataApi(source);
        }

        internal static bool ContainsLegacyDomainHelperApiForAssembly(
            string source,
            bool hasLegacyAssemblySource,
            string[] legacyAssemblyAliases)
        {
            return ThirdPartyToolMigrationApiDetectionRules.ContainsLegacyDomainHelperApiForAssembly(source, hasLegacyAssemblySource, legacyAssemblyAliases);
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

        internal static bool ContainsCurrentFirstPartyScreenshotApi(string source)
        {
            return ThirdPartyToolMigrationApiDetectionRules.ContainsCurrentFirstPartyScreenshotApi(source);
        }

        internal static bool ContainsCurrentFirstPartyScreenshotApiForAssembly(
            string source,
            bool hasAssemblyScopedCurrentFirstPartyToolsUsing,
            string[] currentFirstPartyToolsAssemblyAliases,
            string[] assemblyDeclaredTypeNames)
        {
            return ThirdPartyToolMigrationApiDetectionRules.ContainsCurrentFirstPartyScreenshotApiForAssembly(source, hasAssemblyScopedCurrentFirstPartyToolsUsing, currentFirstPartyToolsAssemblyAliases, assemblyDeclaredTypeNames);
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
            return ThirdPartyToolMigrationDetectionRules.ContainsLegacyAssemblyScopedApi(source, legacyAssemblyAliases);
        }

        internal static bool ContainsLegacyGlobalUsing(string source)
        {
            return ThirdPartyToolMigrationDetectionRules.ContainsLegacyGlobalUsing(source);
        }

        internal static bool ContainsCurrentDomainGlobalUsing(string source)
        {
            return ThirdPartyToolMigrationDetectionRules.ContainsCurrentDomainGlobalUsing(source);
        }

        internal static bool ContainsCurrentToolContractsGlobalUsing(string source)
        {
            return ThirdPartyToolMigrationDetectionRules.ContainsCurrentToolContractsGlobalUsing(source);
        }

        internal static bool ContainsCurrentApplicationGlobalUsing(string source)
        {
            return ThirdPartyToolMigrationDetectionRules.ContainsCurrentApplicationGlobalUsing(source);
        }

        internal static bool ContainsCurrentApplicationNamespaceAlias(string source)
        {
            return ThirdPartyToolMigrationDetectionRules.ContainsCurrentApplicationNamespaceAlias(source);
        }

        internal static bool ContainsCurrentFirstPartyToolsGlobalUsing(string source)
        {
            return ThirdPartyToolMigrationDetectionRules.ContainsCurrentFirstPartyToolsGlobalUsing(source);
        }

        internal static bool ContainsCurrentFirstPartyToolsNamespaceAlias(string source)
        {
            return ThirdPartyToolMigrationDetectionRules.ContainsCurrentFirstPartyToolsNamespaceAlias(source);
        }

        internal static string[] GetLegacyGlobalNamespaceAliases(string source)
        {
            return ThirdPartyToolMigrationDetectionRules.GetLegacyGlobalNamespaceAliases(source);
        }

        internal static string[] GetLegacyGlobalToolInfoTypeAliases(string source)
        {
            return ThirdPartyToolMigrationDetectionRules.GetLegacyGlobalToolInfoTypeAliases(source);
        }

        internal static string[] GetCurrentApplicationGlobalNamespaceAliases(string source)
        {
            return ThirdPartyToolMigrationDetectionRules.GetCurrentApplicationGlobalNamespaceAliases(source);
        }

        internal static string[] GetCurrentFirstPartyToolsGlobalNamespaceAliases(string source)
        {
            return ThirdPartyToolMigrationDetectionRules.GetCurrentFirstPartyToolsGlobalNamespaceAliases(source);
        }

        internal static string[] GetDeclaredTypeNames(string source)
        {
            return ThirdPartyToolMigrationDetectionRules.GetDeclaredTypeNames(source);
        }

        internal static bool ContainsLegacyGlobalToolInfoTypeAlias(string source)
        {
            return ThirdPartyToolMigrationDetectionRules.ContainsLegacyGlobalToolInfoTypeAlias(source);
        }

        internal static bool ContainsLegacyTypeAliasReference(string source, string[] aliases)
        {
            return ThirdPartyToolMigrationDetectionRules.ContainsLegacyTypeAliasReference(source, aliases);
        }

        internal static bool IsExcludedDirectoryName(string directoryName)
        {
            return ThirdPartyToolMigrationDetectionRules.IsExcludedDirectoryName(directoryName);
        }

        internal static string[] GetExcludedDirectoryNames()
        {
            return ThirdPartyToolMigrationDetectionRules.GetExcludedDirectoryNames();
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

        internal readonly struct RemovedLegacyPlayerLoopTimingSignature
        {
            public RemovedLegacyPlayerLoopTimingSignature(
                string methodName,
                string declaringTypeName,
                LegacyPlayerLoopTimingParameterDeclaration[] originalParameters,
                RemovedLegacyPlayerLoopTimingParameter[] removedParameters)
            {
                Debug.Assert(!string.IsNullOrEmpty(methodName), "methodName must not be null or empty");
                Debug.Assert(declaringTypeName != null, "declaringTypeName must not be null");
                Debug.Assert(originalParameters != null, "originalParameters must not be null");
                Debug.Assert(removedParameters != null, "removedParameters must not be null");

                MethodName = methodName;
                DeclaringTypeName = declaringTypeName;
                OriginalParameters =
                    originalParameters ?? Array.Empty<LegacyPlayerLoopTimingParameterDeclaration>();
                RemovedParameters = removedParameters ?? Array.Empty<RemovedLegacyPlayerLoopTimingParameter>();
            }

            public string MethodName { get; }
            public string DeclaringTypeName { get; }
            public LegacyPlayerLoopTimingParameterDeclaration[] OriginalParameters { get; }
            public RemovedLegacyPlayerLoopTimingParameter[] RemovedParameters { get; }
        }

        internal readonly struct LegacyPlayerLoopTimingParameterDeclaration
        {
            public LegacyPlayerLoopTimingParameterDeclaration(
                int index,
                string typeName,
                string name,
                bool hasDefaultValue)
            {
                Debug.Assert(index >= 0, "index must not be negative");
                Debug.Assert(!string.IsNullOrEmpty(typeName), "typeName must not be null or empty");
                Debug.Assert(!string.IsNullOrEmpty(name), "name must not be null or empty");

                Index = index;
                TypeName = typeName;
                Name = name;
                HasDefaultValue = hasDefaultValue;
            }

            public int Index { get; }
            public string TypeName { get; }
            public string Name { get; }
            public bool HasDefaultValue { get; }
        }

        internal readonly struct RemovedLegacyPlayerLoopTimingParameter
        {
            public RemovedLegacyPlayerLoopTimingParameter(int index, string name)
            {
                Debug.Assert(index >= 0, "index must not be negative");
                Debug.Assert(!string.IsNullOrEmpty(name), "name must not be null or empty");

                Index = index;
                Name = name;
            }

            public int Index { get; }
            public string Name { get; }
        }
    }

    internal readonly struct ThirdPartyToolMigrationContentResult
    {
        public ThirdPartyToolMigrationContentResult(
            string content,
            int replacementCount,
            ThirdPartyToolMigrationRules.RemovedLegacyPlayerLoopTimingSignature[] removedPlayerLoopTimingSignatures)
        {
            Debug.Assert(content != null, "content must not be null");
            Debug.Assert(replacementCount >= 0, "replacementCount must not be negative");
            Debug.Assert(
                removedPlayerLoopTimingSignatures != null,
                "removedPlayerLoopTimingSignatures must not be null");

            Content = content ?? string.Empty;
            ReplacementCount = replacementCount;
            RemovedPlayerLoopTimingSignatures =
                removedPlayerLoopTimingSignatures ??
                Array.Empty<ThirdPartyToolMigrationRules.RemovedLegacyPlayerLoopTimingSignature>();
        }

        public string Content { get; }
        public int ReplacementCount { get; }
        public ThirdPartyToolMigrationRules.RemovedLegacyPlayerLoopTimingSignature[] RemovedPlayerLoopTimingSignatures { get; }
        public bool Changed => ReplacementCount > 0;
    }
}
