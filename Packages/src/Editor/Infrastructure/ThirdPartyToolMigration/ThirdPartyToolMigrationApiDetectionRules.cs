using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using CodeTextMask = io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationParsingRules.CodeTextMask;
using ReplacementRule = io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationParsingRules.ReplacementRule;
using TypeReplacementRule = io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationParsingRules.TypeReplacementRule;
using LegacyPlayerLoopTimingParameterDeclaration = io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationRules.LegacyPlayerLoopTimingParameterDeclaration;
using RemovedLegacyPlayerLoopTimingParameter = io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationRules.RemovedLegacyPlayerLoopTimingParameter;
using RemovedLegacyPlayerLoopTimingSignature = io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationRules.RemovedLegacyPlayerLoopTimingSignature;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationAliasRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationApplicationDetectionRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationArgumentRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationAsmdefRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationAttributeRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationCSharpRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationCodeTextMaskBuilder;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationCodeTextMaskInterpolationRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationConstructorArgumentRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationDelayRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationDetectionRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationDomainDetectionRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationEditorDelayRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationMetadataConstructorRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationParsingRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationRegexRewriteRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationRuleCatalog;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationScreenshotArgumentRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationScreenshotDeconstructionRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationScreenshotDetectionRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationScreenshotRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTimingArgumentRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTimingCallerRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTimingCleanupRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTimingDeclarationRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTimingInvocationRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTimingMethodBodyRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTimingMethodDeclarationRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTimingTypeNameRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTimingTypeResolutionRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTimingTypeScopeRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationToolContractDetectionRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTypeReplacementRules;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    internal static class ThirdPartyToolMigrationApiDetectionRules
    {
        internal static bool ContainsLegacyRegistrarApi(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return ContainsLegacyRegistrarApiForAssembly(
                source,
                hasLegacyAssemblySource: ContainsLegacyToolMigrationMarker(source),
                legacyAssemblyAliases: Array.Empty<string>());
        }

        internal static bool ContainsLegacyRegistrarApiForAssembly(
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

        internal static bool ContainsCurrentRegistrarApi(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return RegexMatchesCode(source, CurrentRegistrarRegex);
        }

        internal static bool ContainsLegacyApplicationApiForAssembly(
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

        internal static bool ContainsCurrentApplicationApi(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            bool hasCurrentApplicationNamespaceUsage = RegexMatchesCode(source, CurrentApplicationNamespaceRegex);
            return ContainsCurrentApplicationApiForAssembly(
                source,
                hasCurrentApplicationNamespaceUsage,
                Array.Empty<string>(),
                GetDeclaredTypeNames(source));
        }

        internal static bool ContainsCurrentApplicationApiForAssembly(
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

        internal static bool ContainsRegistrarDomainReturnApi(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return ContainsRegistrarDomainReturnApiForAssembly(
                source,
                hasLegacyAssemblySource: ContainsLegacyToolMigrationMarker(source),
                legacyAssemblyAliases: Array.Empty<string>());
        }

        internal static bool ContainsRegistrarDomainReturnApiForAssembly(
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

        internal static bool ContainsCurrentToolContractsApi(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return RegexMatchesCode(source, CurrentToolContractsNamespaceRegex);
        }

        internal static bool ContainsLegacyDomainMetadataApi(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return RegexMatchesCode(source, LegacyDomainMetadataRegex) ||
                ContainsLegacyDomainHelperApiForAssembly(
                    source,
                    hasLegacyAssemblySource: ContainsLegacyToolMigrationMarker(source),
                    legacyAssemblyAliases: Array.Empty<string>());
        }

        internal static bool ContainsLegacyDomainHelperApiForAssembly(
            string source,
            bool hasLegacyAssemblySource,
            string[] legacyAssemblyAliases)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(legacyAssemblyAliases != null, "legacyAssemblyAliases must not be null");

            string[] legacyNamespaceAliases = GetCombinedLegacyNamespaceAliases(source, legacyAssemblyAliases);
            return ContainsLegacyDomainHelperReference(
                source,
                hasLegacyAssemblySource,
                legacyNamespaceAliases);
        }

        internal static bool ContainsCurrentDomainMetadataApi(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return ContainsCurrentDomainMetadataApiForAssembly(
                source,
                hasAssemblyScopedCurrentDomainUsing: false);
        }

        internal static bool ContainsCurrentDomainMetadataApiForAssembly(
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

        internal static bool ContainsLegacyFirstPartyScreenshotApiForAssembly(
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

        internal static bool ContainsLegacyEditorWindowCaptureUtilityMigrationForAssembly(
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

        internal static bool ContainsLegacyEditorWindowCaptureUtilityTimeoutMigrationForAssembly(
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

        internal static bool ContainsCurrentFirstPartyScreenshotApi(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            bool hasCurrentFirstPartyToolsNamespaceUsage =
                RegexMatchesCode(source, CurrentFirstPartyToolsNamespaceRegex);
            return ContainsCurrentFirstPartyScreenshotApiForAssembly(
                source,
                hasCurrentFirstPartyToolsNamespaceUsage,
                Array.Empty<string>(),
                Array.Empty<string>());
        }

        internal static bool ContainsCurrentFirstPartyScreenshotApiForAssembly(
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

        internal static bool ContainsCurrentFirstPartyScreenshotContractApiForAssembly(
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

        internal static bool ContainsCurrentCaptureGameRenderingDeconstructionMigrationForAssembly(
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
