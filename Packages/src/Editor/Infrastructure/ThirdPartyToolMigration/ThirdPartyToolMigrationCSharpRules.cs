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
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationApiDetectionRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationApplicationDetectionRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationArgumentRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationAsmdefRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationAttributeRules;
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
    internal static class ThirdPartyToolMigrationCSharpRules
    {
        internal static ThirdPartyToolMigrationContentResult MigrateCSharpSource(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return MigrateCSharpSourceForLegacyAssembly(
                source,
                hasLegacyAssemblySource: ContainsLegacyToolMigrationMarker(source),
                hasAssemblyScopedCurrentToolContractsUsing: false,
                hasAssemblyScopedCurrentApplicationUsing: false,
                hasAssemblyScopedCurrentFirstPartyToolsUsing: false,
                legacyAssemblyAliases: Array.Empty<string>(),
                legacyAssemblyToolInfoAliases: Array.Empty<string>(),
                currentApplicationAssemblyAliases: Array.Empty<string>(),
                currentFirstPartyToolsAssemblyAliases: Array.Empty<string>(),
                assemblyDeclaredTypeNames: Array.Empty<string>());
        }

        internal static ThirdPartyToolMigrationContentResult MigrateCSharpSourceForLegacyAssembly(
            string source,
            bool hasLegacyAssemblySource,
            bool hasAssemblyScopedCurrentToolContractsUsing,
            bool hasAssemblyScopedCurrentApplicationUsing,
            bool hasAssemblyScopedCurrentFirstPartyToolsUsing,
            string[] legacyAssemblyAliases,
            string[] legacyAssemblyToolInfoAliases,
            string[] currentApplicationAssemblyAliases,
            string[] currentFirstPartyToolsAssemblyAliases,
            string[] assemblyDeclaredTypeNames)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(legacyAssemblyAliases != null, "legacyAssemblyAliases must not be null");
            Debug.Assert(legacyAssemblyToolInfoAliases != null, "legacyAssemblyToolInfoAliases must not be null");
            Debug.Assert(
                currentApplicationAssemblyAliases != null,
                "currentApplicationAssemblyAliases must not be null");
            Debug.Assert(
                currentFirstPartyToolsAssemblyAliases != null,
                "currentFirstPartyToolsAssemblyAliases must not be null");
            Debug.Assert(assemblyDeclaredTypeNames != null, "assemblyDeclaredTypeNames must not be null");

            string migratedContent = source;
            string[] legacyNamespaceAliases = GetCombinedLegacyNamespaceAliases(source, legacyAssemblyAliases);
            string[] currentApplicationNamespaceAliases = GetCombinedCurrentApplicationNamespaceAliases(
                source,
                currentApplicationAssemblyAliases);
            string[] currentFirstPartyToolsNamespaceAliases = GetCombinedCurrentFirstPartyToolsNamespaceAliases(
                source,
                currentFirstPartyToolsAssemblyAliases);
            bool hasLegacyNamespaceUsage = RegexMatchesCode(source, LegacyNamespaceRegex);
            bool hasLegacyNamespaceUsingDirective = RegexMatchesCode(source, LegacyNamespaceUsingRegex);
            bool hasCurrentApplicationNamespaceUsage = RegexMatchesCode(source, CurrentApplicationNamespaceRegex);
            bool hasCurrentDomainNamespaceUsage = RegexMatchesCode(source, CurrentDomainNamespaceRegex);
            bool hasCurrentToolContractsNamespaceUsage = RegexMatchesCode(source, CurrentToolContractsNamespaceRegex);
            bool hasCurrentToolContractsUsingDirective =
                RegexMatchesCode(source, CurrentToolContractsUsingRegex);
            bool hasCurrentFirstPartyToolsNamespaceUsage =
                RegexMatchesCode(source, CurrentFirstPartyToolsNamespaceRegex);
            bool canUseCurrentToolContracts =
                hasCurrentToolContractsNamespaceUsage ||
                hasAssemblyScopedCurrentToolContractsUsing;
            bool canUseBareCurrentToolContracts =
                hasLegacyAssemblySource ||
                hasLegacyNamespaceUsingDirective ||
                hasCurrentToolContractsUsingDirective ||
                hasAssemblyScopedCurrentToolContractsUsing;
            bool canMigrateBareLegacyToolAttribute =
                hasLegacyAssemblySource ||
                hasLegacyNamespaceUsage ||
                legacyNamespaceAliases.Length > 0;
            bool canMigrateBareLegacyEditorWindowCaptureUtility =
                canMigrateBareLegacyToolAttribute ||
                canUseCurrentToolContracts ||
                hasAssemblyScopedCurrentFirstPartyToolsUsing ||
                hasCurrentFirstPartyToolsNamespaceUsage;
            bool canUseBareCurrentFirstPartyTools =
                hasAssemblyScopedCurrentFirstPartyToolsUsing ||
                hasCurrentFirstPartyToolsNamespaceUsage;
            bool shouldQualifyBareEditorWindowCaptureUtilityTimeout =
                !canUseBareCurrentToolContracts;
            bool canMigrateBareLegacyFirstPartyScreenshotApi =
                canMigrateBareLegacyToolAttribute ||
                canUseCurrentToolContracts ||
                hasCurrentFirstPartyToolsNamespaceUsage;
            bool canMigrateBareLegacyApplicationApi =
                canMigrateBareLegacyToolAttribute ||
                canUseCurrentToolContracts ||
                hasAssemblyScopedCurrentApplicationUsing ||
                hasCurrentApplicationNamespaceUsage;
            bool canMigrateBareLegacyApplicationTypeName =
                canMigrateBareLegacyToolAttribute ||
                canUseCurrentToolContracts ||
                hasCurrentApplicationNamespaceUsage;
            bool canMigrateBareLegacyToolInfoConstructor =
                canMigrateBareLegacyToolAttribute;
            bool canMigrateAmbiguousBareLegacyToolInfoConstructor =
                canMigrateBareLegacyToolAttribute &&
                !hasCurrentDomainNamespaceUsage;
            bool hasLocalLegacyMarker = ContainsLegacyToolMigrationMarker(source);
            bool shouldApplyContractRenames = hasLegacyAssemblySource || hasLocalLegacyMarker;
            bool shouldApplyRegistrarRenames = shouldApplyContractRenames &&
                RegexMatchesCode(source, LegacyRegistrarRegex);
            bool shouldApplyDomainMetadataRenames = shouldApplyContractRenames &&
                RegexMatchesCode(source, LegacyDomainMetadataRegex);
            int replacementCount = 0;
            List<RemovedLegacyPlayerLoopTimingSignature> removedPlayerLoopTimingSignatures = new();
            migratedContent = ReplaceLegacyToolAttributesInCode(
                migratedContent,
                legacyNamespaceAliases,
                canMigrateBareLegacyToolAttribute,
                ref replacementCount);
            migratedContent = ReplaceLegacyToolInfoConstructorsInCode(
                migratedContent,
                legacyNamespaceAliases,
                canMigrateBareLegacyToolInfoConstructor,
                canMigrateAmbiguousBareLegacyToolInfoConstructor,
                legacyAssemblyToolInfoAliases,
                ref replacementCount);
            migratedContent = ReplaceLegacyToolSettingsCatalogItemConstructorsInCode(
                migratedContent,
                legacyNamespaceAliases,
                canMigrateBareLegacyToolAttribute,
                ref replacementCount);
            (string editorDelayMigratedContent, int editorDelayReplacementCount) =
                ReplaceLegacyEditorDelayFrameCallsInCode(
                    migratedContent,
                    legacyNamespaceAliases,
                    canMigrateBareLegacyToolAttribute || canUseCurrentToolContracts,
                    !canUseBareCurrentToolContracts);
            migratedContent = editorDelayMigratedContent;
            replacementCount += editorDelayReplacementCount;
            (string timerDelayMigratedContent, int timerDelayReplacementCount) =
                ReplaceLegacyTimerDelayNamedArgumentsInCode(
                    migratedContent,
                    legacyNamespaceAliases,
                    canMigrateBareLegacyToolAttribute || canUseCurrentToolContracts);
            migratedContent = timerDelayMigratedContent;
            replacementCount += timerDelayReplacementCount;
            (string mainThreadSwitcherMigratedContent, int mainThreadSwitcherReplacementCount) =
                ReplaceLegacyMainThreadSwitcherCallsInCode(
                    migratedContent,
                    legacyNamespaceAliases,
                    currentApplicationNamespaceAliases,
                    canMigrateBareLegacyApplicationApi,
                    assemblyDeclaredTypeNames);
            migratedContent = mainThreadSwitcherMigratedContent;
            replacementCount += mainThreadSwitcherReplacementCount;
            bool hasMainThreadSwitcherMigrationContext =
                mainThreadSwitcherReplacementCount > 0 ||
                ContainsMigratedMainThreadSwitcherSwitchCall(migratedContent);
            if (hasMainThreadSwitcherMigrationContext)
            {
                string[] migratedCalleeMethodNames = Array.Empty<string>();
                while (true)
                {
                    (
                        string playerLoopTimingMigratedContent,
                        int playerLoopTimingReplacementCount,
                        RemovedLegacyPlayerLoopTimingSignature[] localRemovedTimingSignatures) =
                        RemoveLegacyPlayerLoopTimingParametersInCode(
                            migratedContent,
                            legacyNamespaceAliases,
                            canMigrateBareLegacyApplicationApi,
                            migratedCalleeMethodNames);
                    if (playerLoopTimingReplacementCount == 0)
                    {
                        break;
                    }

                    migratedContent = playerLoopTimingMigratedContent;
                    replacementCount += playerLoopTimingReplacementCount;
                    removedPlayerLoopTimingSignatures.AddRange(localRemovedTimingSignatures);
                    (string timingCallerMigratedContent, int timingCallerReplacementCount) =
                        RemoveLegacyPlayerLoopTimingCallerArgumentsInCode(
                            migratedContent,
                            localRemovedTimingSignatures,
                            legacyNamespaceAliases);
                    migratedContent = timingCallerMigratedContent;
                    replacementCount += timingCallerReplacementCount;
                    migratedCalleeMethodNames = localRemovedTimingSignatures
                        .Select(signature => signature.MethodName)
                        .ToArray();
                }

                (string unusedTimingMigratedContent, int unusedTimingReplacementCount) =
                    RemoveUnusedLegacyPlayerLoopTimingDeclarationsInCode(migratedContent);
                migratedContent = unusedTimingMigratedContent;
                replacementCount += unusedTimingReplacementCount;
            }

            (string editorWindowCaptureMigratedContent, int editorWindowCaptureReplacementCount) =
                ReplaceLegacyEditorWindowCaptureUtilityCallsInCode(
                    migratedContent,
                    legacyNamespaceAliases,
                    currentFirstPartyToolsNamespaceAliases,
                    canMigrateBareLegacyEditorWindowCaptureUtility,
                    shouldQualifyBareEditorWindowCaptureUtilityTimeout,
                    canUseBareCurrentFirstPartyTools,
                    assemblyDeclaredTypeNames);
            migratedContent = editorWindowCaptureMigratedContent;
            replacementCount += editorWindowCaptureReplacementCount;
            migratedContent = ReplaceLegacyFirstPartyScreenshotTypeNamesInCode(
                migratedContent,
                legacyNamespaceAliases,
                canMigrateBareLegacyFirstPartyScreenshotApi,
                assemblyDeclaredTypeNames,
                ref replacementCount);
            migratedContent = ReplaceLegacyApplicationTypeNamesInCode(
                migratedContent,
                legacyNamespaceAliases,
                canMigrateBareLegacyApplicationTypeName,
                assemblyDeclaredTypeNames,
                ref replacementCount);
            migratedContent = ReplaceLegacyRegistrarAliasesInCode(
                migratedContent,
                legacyNamespaceAliases,
                ref replacementCount);

            if (shouldApplyContractRenames)
            {
                migratedContent = ReplaceLegacyDomainTypeNamesInCode(
                    migratedContent,
                    legacyNamespaceAliases,
                    ref replacementCount);

                migratedContent = ReplaceLegacyContractTypeNamesInCode(
                    migratedContent,
                    legacyNamespaceAliases,
                    ref replacementCount);

                foreach (ReplacementRule rule in CSharpReplacementRules)
                {
                    migratedContent = ReplaceRegexInCode(
                        migratedContent,
                        rule.PatternRegex,
                        _ => rule.Replacement,
                    ref replacementCount);
                }
            }

            if (shouldApplyRegistrarRenames || shouldApplyDomainMetadataRenames)
            {
                if (shouldApplyRegistrarRenames)
                {
                    migratedContent = ReplaceUnqualifiedLegacyRegistrarReferencesInCode(
                        migratedContent,
                        ref replacementCount);
                }

                foreach (ReplacementRule rule in RegistrarReplacementRules)
                {
                    migratedContent = ReplaceRegexInCode(
                        migratedContent,
                        rule.PatternRegex,
                        _ => rule.Replacement,
                        ref replacementCount);
                }

                migratedContent = ReplaceLegacyToolInfoTypeReferencesInCode(
                    migratedContent,
                    ref replacementCount);
            }

            return new ThirdPartyToolMigrationContentResult(
                migratedContent,
                replacementCount,
                removedPlayerLoopTimingSignatures.ToArray());
        }
    }
}
