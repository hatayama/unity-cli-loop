using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;


using CodeTextMask = io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationParsingRules.CodeTextMask;
using ReplacementRule = io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationParsingRules.ReplacementRule;
using TypeReplacementRule = io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationParsingRules.TypeReplacementRule;
using LegacyPlayerLoopTimingParameterDeclaration = io.github.hatayama.UnityCliLoop.Domain.LegacyPlayerLoopTimingParameterDeclaration;
using RemovedLegacyPlayerLoopTimingParameter = io.github.hatayama.UnityCliLoop.Domain.RemovedLegacyPlayerLoopTimingParameter;
using RemovedLegacyPlayerLoopTimingSignature = io.github.hatayama.UnityCliLoop.Domain.RemovedLegacyPlayerLoopTimingSignature;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationAliasRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationApplicationDetectionRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationArgumentRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationAttributeRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationCSharpRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationCodeTextMaskBuilder;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationCodeTextMaskInterpolationRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationConstructorArgumentRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationDelayRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationDomainDetectionRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationEditorDelayRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationMetadataConstructorRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationParsingRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationRegexRewriteRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationRuleCatalog;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationCodeTextDetectionRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationScreenshotArgumentRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationScreenshotDeconstructionRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationScreenshotDetectionRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationScreenshotRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingArgumentRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingCallerRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingCleanupRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingDeclarationRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingInvocationRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingMethodBodyRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingMethodDeclarationRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingTypeNameRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingTypeResolutionRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingTypeScopeRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationToolContractDetectionRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTypeReplacementRules;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    public static class ThirdPartyToolMigrationApiDetectionRules
    {
        public static bool ContainsLegacyRegistrarApi(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return ContainsLegacyRegistrarApiForAssembly(
                source,
                hasLegacyAssemblySource: ContainsLegacyToolMigrationMarker(source),
                legacyAssemblyAliases: Array.Empty<string>());
        }

        public static bool ContainsLegacyRegistrarApiForAssembly(
            string source,
            bool hasLegacyAssemblySource,
            string[] legacyAssemblyAliases)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(legacyAssemblyAliases != null, "legacyAssemblyAliases must not be null");

            string[] legacyNamespaceAliases = GetCombinedLegacyNamespaceAliases(source, legacyAssemblyAliases);
            bool canMigrateBareLegacyRegistrar =
                hasLegacyAssemblySource ||
                ContainsLegacyToolMigrationMarker(source) ||
                legacyNamespaceAliases.Length > 0;
            return ContainsLegacyRegistrarReference(
                source,
                canMigrateBareLegacyRegistrar,
                legacyNamespaceAliases);
        }

        public static bool ContainsCurrentRegistrarApi(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return RegexMatchesCode(source, CurrentRegistrarRegex);
        }

        public static bool ContainsLegacyApplicationApiForAssembly(
            string source,
            bool hasLegacyAssemblySource,
            string[] legacyAssemblyAliases,
            string[] currentApplicationAssemblyAliases,
            string[] assemblyDeclaredTypeNames)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(legacyAssemblyAliases != null, "legacyAssemblyAliases must not be null");
            Debug.Assert(
                currentApplicationAssemblyAliases != null,
                "currentApplicationAssemblyAliases must not be null");
            Debug.Assert(assemblyDeclaredTypeNames != null, "assemblyDeclaredTypeNames must not be null");

            string[] legacyNamespaceAliases = GetCombinedLegacyNamespaceAliases(source, legacyAssemblyAliases);
            string[] currentApplicationNamespaceAliases = GetCombinedCurrentApplicationNamespaceAliases(
                source,
                currentApplicationAssemblyAliases);
            return ContainsLegacyApplicationReference(
                source,
                hasLegacyAssemblySource,
                legacyNamespaceAliases,
                currentApplicationNamespaceAliases,
                assemblyDeclaredTypeNames);
        }

        public static bool ContainsCurrentApplicationApi(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            bool hasCurrentApplicationNamespaceUsage = RegexMatchesCode(source, CurrentApplicationNamespaceRegex);
            return ContainsCurrentApplicationApiForAssembly(
                source,
                hasCurrentApplicationNamespaceUsage,
                Array.Empty<string>(),
                GetDeclaredTypeNames(source));
        }

        public static bool ContainsCurrentApplicationApiForAssembly(
            string source,
            bool hasAssemblyScopedCurrentApplicationUsing,
            string[] currentApplicationAssemblyAliases,
            string[] assemblyDeclaredTypeNames)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(
                currentApplicationAssemblyAliases != null,
                "currentApplicationAssemblyAliases must not be null");
            Debug.Assert(assemblyDeclaredTypeNames != null, "assemblyDeclaredTypeNames must not be null");

            bool hasCurrentApplicationNamespaceUsage = RegexMatchesCode(source, CurrentApplicationNamespaceRegex);
            string[] currentApplicationNamespaceAliases = GetCombinedCurrentApplicationNamespaceAliases(
                source,
                currentApplicationAssemblyAliases);
            return ContainsCurrentApplicationReference(
                source,
                hasAssemblyScopedCurrentApplicationUsing || hasCurrentApplicationNamespaceUsage,
                currentApplicationNamespaceAliases,
                assemblyDeclaredTypeNames);
        }

        public static bool ContainsRegistrarDomainReturnApi(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return ContainsRegistrarDomainReturnApiForAssembly(
                source,
                hasLegacyAssemblySource: ContainsLegacyToolMigrationMarker(source),
                legacyAssemblyAliases: Array.Empty<string>());
        }

        public static bool ContainsRegistrarDomainReturnApiForAssembly(
            string source,
            bool hasLegacyAssemblySource,
            string[] legacyAssemblyAliases)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(legacyAssemblyAliases != null, "legacyAssemblyAliases must not be null");

            if (RegexMatchesCode(source, CurrentRegistrarDomainReturnRegex))
            {
                return true;
            }

            string[] legacyNamespaceAliases = GetCombinedLegacyNamespaceAliases(source, legacyAssemblyAliases);
            bool canMigrateBareLegacyRegistrar =
                hasLegacyAssemblySource ||
                ContainsLegacyToolMigrationMarker(source) ||
                legacyNamespaceAliases.Length > 0;
            return ContainsLegacyRegistrarDomainReturnReference(
                source,
                canMigrateBareLegacyRegistrar,
                legacyNamespaceAliases);
        }

        public static bool ContainsCurrentToolContractsApi(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return RegexMatchesCode(source, CurrentToolContractsNamespaceRegex);
        }

        public static bool ContainsCurrentDomainMetadataApi(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return ContainsCurrentDomainMetadataApiForAssembly(
                source,
                hasAssemblyScopedCurrentDomainUsing: false);
        }

        public static bool ContainsCurrentDomainMetadataApiForAssembly(
            string source,
            bool hasAssemblyScopedCurrentDomainUsing)
        {
            Debug.Assert(source != null, "source must not be null");

            bool hasCurrentDomainNamespaceUsage = RegexMatchesCode(source, CurrentDomainNamespaceRegex);
            bool canUseBareCurrentDomainType = hasAssemblyScopedCurrentDomainUsing || hasCurrentDomainNamespaceUsage;
            return RegexMatchesCode(source, CurrentDomainMetadataRegex) ||
                ContainsCurrentDomainHelperApiForAssembly(source, canUseBareCurrentDomainType) ||
                (canUseBareCurrentDomainType && RegexMatchesCode(source, LegacyDomainMetadataRegex));
        }

        public static bool ContainsLegacyFirstPartyScreenshotApiForAssembly(
            string source,
            bool hasLegacyAssemblySource,
            string[] legacyAssemblyAliases,
            string[] assemblyDeclaredTypeNames)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(legacyAssemblyAliases != null, "legacyAssemblyAliases must not be null");
            Debug.Assert(assemblyDeclaredTypeNames != null, "assemblyDeclaredTypeNames must not be null");

            string[] legacyNamespaceAliases = GetCombinedLegacyNamespaceAliases(source, legacyAssemblyAliases);
            return ContainsLegacyFirstPartyScreenshotReference(
                source,
                hasLegacyAssemblySource,
                legacyNamespaceAliases,
                assemblyDeclaredTypeNames);
        }

        public static bool ContainsLegacyEditorWindowCaptureUtilityMigrationForAssembly(
            string source,
            bool hasLegacyAssemblySource,
            bool hasAssemblyScopedCurrentToolContractsUsing,
            bool hasAssemblyScopedCurrentFirstPartyToolsUsing,
            string[] legacyAssemblyAliases,
            string[] currentFirstPartyToolsAssemblyAliases,
            string[] assemblyDeclaredTypeNames)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(legacyAssemblyAliases != null, "legacyAssemblyAliases must not be null");
            Debug.Assert(
                currentFirstPartyToolsAssemblyAliases != null,
                "currentFirstPartyToolsAssemblyAliases must not be null");
            Debug.Assert(assemblyDeclaredTypeNames != null, "assemblyDeclaredTypeNames must not be null");

            string[] legacyNamespaceAliases = GetCombinedLegacyNamespaceAliases(source, legacyAssemblyAliases);
            string[] currentFirstPartyToolsNamespaceAliases = GetCombinedCurrentFirstPartyToolsNamespaceAliases(
                source,
                currentFirstPartyToolsAssemblyAliases);
            bool canMigrateBareLegacyEditorWindowCaptureUtility =
                CanMigrateBareLegacyEditorWindowCaptureUtilityForAssembly(
                    source,
                    hasLegacyAssemblySource,
                    hasAssemblyScopedCurrentToolContractsUsing,
                    hasAssemblyScopedCurrentFirstPartyToolsUsing,
                    legacyNamespaceAliases);
            return ContainsLegacyEditorWindowCaptureUtilityMigration(
                source,
                legacyNamespaceAliases,
                currentFirstPartyToolsNamespaceAliases,
                canMigrateBareLegacyEditorWindowCaptureUtility,
                assemblyDeclaredTypeNames,
                requiresTimeoutArgumentMigration: false);
        }

        public static bool ContainsLegacyEditorWindowCaptureUtilityTimeoutMigrationForAssembly(
            string source,
            bool hasLegacyAssemblySource,
            bool hasAssemblyScopedCurrentToolContractsUsing,
            bool hasAssemblyScopedCurrentFirstPartyToolsUsing,
            string[] legacyAssemblyAliases,
            string[] currentFirstPartyToolsAssemblyAliases,
            string[] assemblyDeclaredTypeNames)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(legacyAssemblyAliases != null, "legacyAssemblyAliases must not be null");
            Debug.Assert(
                currentFirstPartyToolsAssemblyAliases != null,
                "currentFirstPartyToolsAssemblyAliases must not be null");
            Debug.Assert(assemblyDeclaredTypeNames != null, "assemblyDeclaredTypeNames must not be null");

            string[] legacyNamespaceAliases = GetCombinedLegacyNamespaceAliases(source, legacyAssemblyAliases);
            string[] currentFirstPartyToolsNamespaceAliases = GetCombinedCurrentFirstPartyToolsNamespaceAliases(
                source,
                currentFirstPartyToolsAssemblyAliases);
            bool canMigrateBareLegacyEditorWindowCaptureUtility =
                CanMigrateBareLegacyEditorWindowCaptureUtilityForAssembly(
                    source,
                    hasLegacyAssemblySource,
                    hasAssemblyScopedCurrentToolContractsUsing,
                    hasAssemblyScopedCurrentFirstPartyToolsUsing,
                    legacyNamespaceAliases);
            return ContainsLegacyEditorWindowCaptureUtilityMigration(
                source,
                legacyNamespaceAliases,
                currentFirstPartyToolsNamespaceAliases,
                canMigrateBareLegacyEditorWindowCaptureUtility,
                assemblyDeclaredTypeNames,
                requiresTimeoutArgumentMigration: true);
        }

        public static bool ContainsCurrentFirstPartyScreenshotApiForAssembly(
            string source,
            bool hasAssemblyScopedCurrentFirstPartyToolsUsing,
            string[] currentFirstPartyToolsAssemblyAliases,
            string[] assemblyDeclaredTypeNames)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(
                currentFirstPartyToolsAssemblyAliases != null,
                "currentFirstPartyToolsAssemblyAliases must not be null");
            Debug.Assert(assemblyDeclaredTypeNames != null, "assemblyDeclaredTypeNames must not be null");

            bool hasCurrentFirstPartyToolsNamespaceUsage =
                RegexMatchesCode(source, CurrentFirstPartyToolsNamespaceRegex);
            string[] currentFirstPartyToolsNamespaceAliases = GetCombinedCurrentFirstPartyToolsNamespaceAliases(
                source,
                currentFirstPartyToolsAssemblyAliases);
            bool canUseBareCurrentFirstPartyScreenshotType =
                hasAssemblyScopedCurrentFirstPartyToolsUsing ||
                hasCurrentFirstPartyToolsNamespaceUsage;
            return ContainsCurrentFirstPartyScreenshotReference(
                source,
                canUseBareCurrentFirstPartyScreenshotType,
                currentFirstPartyToolsNamespaceAliases,
                assemblyDeclaredTypeNames);
        }

        public static bool ContainsCurrentFirstPartyScreenshotContractApiForAssembly(
            string source,
            bool hasAssemblyScopedCurrentFirstPartyToolsUsing,
            string[] currentFirstPartyToolsAssemblyAliases,
            string[] assemblyDeclaredTypeNames)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(
                currentFirstPartyToolsAssemblyAliases != null,
                "currentFirstPartyToolsAssemblyAliases must not be null");
            Debug.Assert(assemblyDeclaredTypeNames != null, "assemblyDeclaredTypeNames must not be null");

            bool hasCurrentFirstPartyToolsNamespaceUsage =
                RegexMatchesCode(source, CurrentFirstPartyToolsNamespaceRegex);
            string[] currentFirstPartyToolsNamespaceAliases = GetCombinedCurrentFirstPartyToolsNamespaceAliases(
                source,
                currentFirstPartyToolsAssemblyAliases);
            bool canUseBareCurrentFirstPartyScreenshotType =
                hasAssemblyScopedCurrentFirstPartyToolsUsing ||
                hasCurrentFirstPartyToolsNamespaceUsage;
            return ContainsCurrentFirstPartyScreenshotContractReference(
                source,
                canUseBareCurrentFirstPartyScreenshotType,
                currentFirstPartyToolsNamespaceAliases,
                assemblyDeclaredTypeNames);
        }

        public static bool ContainsCurrentCaptureGameRenderingDeconstructionMigrationForAssembly(
            string source,
            bool hasAssemblyScopedCurrentFirstPartyToolsUsing,
            string[] currentFirstPartyToolsAssemblyAliases,
            string[] assemblyDeclaredTypeNames)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(
                currentFirstPartyToolsAssemblyAliases != null,
                "currentFirstPartyToolsAssemblyAliases must not be null");
            Debug.Assert(assemblyDeclaredTypeNames != null, "assemblyDeclaredTypeNames must not be null");

            bool hasCurrentFirstPartyToolsNamespaceUsage =
                RegexMatchesCode(source, CurrentFirstPartyToolsNamespaceRegex);
            string[] currentFirstPartyToolsNamespaceAliases = GetCombinedCurrentFirstPartyToolsNamespaceAliases(
                source,
                currentFirstPartyToolsAssemblyAliases);
            return ContainsCurrentCaptureGameRenderingDeconstructionMigration(
                source,
                hasAssemblyScopedCurrentFirstPartyToolsUsing || hasCurrentFirstPartyToolsNamespaceUsage,
                currentFirstPartyToolsNamespaceAliases,
                assemblyDeclaredTypeNames);
        }
    }
}
