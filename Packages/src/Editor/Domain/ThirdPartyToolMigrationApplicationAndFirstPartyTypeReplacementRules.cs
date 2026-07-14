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
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationApiDetectionRules;
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

namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Rewrites legacy application and first-party screenshot type names during migration.
    /// </summary>
    public static class ThirdPartyToolMigrationApplicationAndFirstPartyTypeReplacementRules
    {
        public static string ReplaceLegacyApplicationTypeNamesInCode(
            string source,
            string[] aliases,
            string[] currentApplicationNamespaceAliases,
            bool canMigrateBareLegacyApplicationApi,
            bool canPreserveBareCurrentToolContractsReferences,
            string[] assemblyDeclaredTypeNames,
            ref int replacementCount)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(aliases != null, "aliases must not be null");
            Debug.Assert(
                currentApplicationNamespaceAliases != null,
                "currentApplicationNamespaceAliases must not be null");
            Debug.Assert(assemblyDeclaredTypeNames != null, "assemblyDeclaredTypeNames must not be null");

            string migratedContent = source;
            foreach (TypeReplacementRule rule in ApplicationTypeReplacementRules)
            {
                bool hasProtectedTypeDeclaration = DeclaresLocalType(migratedContent, rule.LegacyName) ||
                    assemblyDeclaredTypeNames.Contains(rule.LegacyName);

                Regex fullyQualifiedRegex = new(
                    $@"(?:(?:global::)?{Regex.Escape(LegacyNamespace)}\.){Regex.Escape(rule.LegacyName)}\b",
                    RegexOptions.Compiled);
                migratedContent = ReplaceRegexInCode(
                    migratedContent,
                    fullyQualifiedRegex,
                    _ => $"{CurrentNamespace}.{rule.CurrentName}",
                    ref replacementCount);

                Regex currentApplicationFullyQualifiedRegex = new(
                    $@"(?:(?:global::)?{Regex.Escape(CurrentApplicationNamespace)}\.){Regex.Escape(rule.CurrentName)}\b",
                    RegexOptions.Compiled);
                migratedContent = ReplaceRegexInCode(
                    migratedContent,
                    currentApplicationFullyQualifiedRegex,
                    _ => $"{CurrentNamespace}.{rule.CurrentName}",
                    ref replacementCount);

                foreach (string alias in aliases)
                {
                    Regex aliasRegex = new(
                        $@"(?<!\w){Regex.Escape(alias)}\.{Regex.Escape(rule.LegacyName)}\b",
                        RegexOptions.Compiled);
                    migratedContent = ReplaceRegexInCode(
                        migratedContent,
                        aliasRegex,
                        _ => $"{CurrentNamespace}.{rule.CurrentName}",
                        ref replacementCount);
                }

                foreach (string alias in currentApplicationNamespaceAliases)
                {
                    Regex currentApplicationAliasRegex = new(
                        $@"(?<!\w){Regex.Escape(alias)}\.{Regex.Escape(rule.CurrentName)}\b",
                        RegexOptions.Compiled);
                    migratedContent = ReplaceRegexInCode(
                        migratedContent,
                        currentApplicationAliasRegex,
                        _ => $"{CurrentNamespace}.{rule.CurrentName}",
                        ref replacementCount);
                }

                Regex unqualifiedRegex = new(
                    $@"(?<![\.:])\b{Regex.Escape(rule.LegacyName)}\b(?!\s*=)",
                    RegexOptions.Compiled);
                string unqualifiedReplacement = canPreserveBareCurrentToolContractsReferences
                    ? rule.CurrentName
                    : $"{CurrentNamespace}.{rule.CurrentName}";
                migratedContent = ReplaceRegexInCode(
                    migratedContent,
                    unqualifiedRegex,
                    match => canMigrateBareLegacyApplicationApi &&
                        !hasProtectedTypeDeclaration &&
                        ThirdPartyToolMigrationTypeReplacementRules.ShouldMigrateLegacyTypeReference(migratedContent, rule.LegacyName, match.Index)
                            ? unqualifiedReplacement
                            : match.Value,
                    ref replacementCount);
            }

            return migratedContent;
        }

        public static string ReplaceLegacyFirstPartyScreenshotTypeNamesInCode(
            string source,
            string[] aliases,
            string[] currentFirstPartyToolsNamespaceAliases,
            bool canMigrateBareLegacyFirstPartyScreenshotApi,
            bool canPreserveBareCurrentToolContractsReferences,
            string[] assemblyDeclaredTypeNames,
            ref int replacementCount)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(aliases != null, "aliases must not be null");
            Debug.Assert(
                currentFirstPartyToolsNamespaceAliases != null,
                "currentFirstPartyToolsNamespaceAliases must not be null");
            Debug.Assert(assemblyDeclaredTypeNames != null, "assemblyDeclaredTypeNames must not be null");

            string migratedContent = source;
            foreach (TypeReplacementRule rule in FirstPartyScreenshotTypeReplacementRules)
            {
                bool hasProtectedTypeDeclaration = DeclaresLocalType(migratedContent, rule.LegacyName) ||
                    assemblyDeclaredTypeNames.Contains(rule.LegacyName);

                Regex fullyQualifiedRegex = new(
                    $@"(?:(?:global::)?{Regex.Escape(LegacyNamespace)}\.){Regex.Escape(rule.LegacyName)}\b",
                    RegexOptions.Compiled);
                migratedContent = ReplaceRegexInCode(
                    migratedContent,
                    fullyQualifiedRegex,
                    _ => $"{CurrentNamespace}.{rule.CurrentName}",
                    ref replacementCount);

                foreach (string alias in aliases)
                {
                    Regex aliasRegex = new(
                        $@"(?<!\w){Regex.Escape(alias)}\.{Regex.Escape(rule.LegacyName)}\b",
                        RegexOptions.Compiled);
                    migratedContent = ReplaceRegexInCode(
                        migratedContent,
                        aliasRegex,
                        _ => $"{CurrentNamespace}.{rule.CurrentName}",
                        ref replacementCount);
                }

                if (!string.Equals(
                        rule.CurrentName,
                        LegacyEditorWindowCaptureUtilityTypeName,
                        StringComparison.Ordinal))
                {
                    Regex currentFirstPartyFullyQualifiedRegex = new(
                        $@"(?:(?:global::)?{Regex.Escape(CurrentFirstPartyToolsNamespace)}\.){Regex.Escape(rule.CurrentName)}\b",
                        RegexOptions.Compiled);
                    migratedContent = ReplaceRegexInCode(
                        migratedContent,
                        currentFirstPartyFullyQualifiedRegex,
                        _ => $"{CurrentNamespace}.{rule.CurrentName}",
                        ref replacementCount);

                    foreach (string alias in currentFirstPartyToolsNamespaceAliases)
                    {
                        Regex currentFirstPartyAliasRegex = new(
                            $@"(?<!\w){Regex.Escape(alias)}\.{Regex.Escape(rule.CurrentName)}\b",
                            RegexOptions.Compiled);
                        migratedContent = ReplaceRegexInCode(
                            migratedContent,
                            currentFirstPartyAliasRegex,
                            _ => $"{CurrentNamespace}.{rule.CurrentName}",
                            ref replacementCount);
                    }
                }

                Regex unqualifiedRegex = new(
                    $@"(?<![\.:])\b{Regex.Escape(rule.LegacyName)}\b(?!\s*=)",
                    RegexOptions.Compiled);
                if (string.Equals(
                        rule.CurrentName,
                        LegacyEditorWindowCaptureUtilityTypeName,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                string unqualifiedReplacement = canPreserveBareCurrentToolContractsReferences
                    ? rule.CurrentName
                    : $"{CurrentNamespace}.{rule.CurrentName}";
                migratedContent = ReplaceRegexInCode(
                    migratedContent,
                    unqualifiedRegex,
                    match => canMigrateBareLegacyFirstPartyScreenshotApi &&
                        !hasProtectedTypeDeclaration &&
                        ThirdPartyToolMigrationTypeReplacementRules.ShouldMigrateLegacyTypeReference(migratedContent, rule.LegacyName, match.Index)
                            ? unqualifiedReplacement
                            : match.Value,
                    ref replacementCount);
            }

            return migratedContent;
        }

    }
}
