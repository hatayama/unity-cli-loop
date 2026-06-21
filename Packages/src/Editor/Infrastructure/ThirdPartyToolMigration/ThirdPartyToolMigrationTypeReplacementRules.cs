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

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    internal static class ThirdPartyToolMigrationTypeReplacementRules
    {
        internal static string ReplaceLegacyRegistrarAliasesInCode(
            string source,
            string[] aliases,
            ref int replacementCount)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(aliases != null, "aliases must not be null");

            string migratedContent = source;
            foreach (string alias in aliases)
            {
                Regex aliasRegistrarRegex = new(
                    $@"(?<!\w){Regex.Escape(alias)}\.CustomToolManager\b",
                    RegexOptions.Compiled);
                migratedContent = ReplaceRegexInCode(
                    migratedContent,
                    aliasRegistrarRegex,
                    _ => $"{CurrentNamespace}.UnityCliLoopToolRegistrar",
                    ref replacementCount);

                Regex aliasToolInfoRegex = new(
                    $@"(?<!\w){Regex.Escape(alias)}\.ToolInfo\b",
                    RegexOptions.Compiled);
                migratedContent = ReplaceRegexInCode(
                    migratedContent,
                    aliasToolInfoRegex,
                    _ => $"{CurrentNamespace}.ToolInfo",
                    ref replacementCount);
            }

            return migratedContent;
        }

        internal static string ReplaceCurrentPublicContractNamespacesInCode(string source, ref int replacementCount)
        {
            Debug.Assert(source != null, "source must not be null");

            // Do not blanket-rewrite current package namespaces because many first-party/internal types still live there.
            return source;
        }

        internal static string ReplaceUnqualifiedLegacyRegistrarReferencesInCode(
            string source,
            ref int replacementCount)
        {
            Debug.Assert(source != null, "source must not be null");

            Regex unqualifiedRegistrarRegex =
                new(@"(?<![\.:])\bCustomToolManager\b(?!\s*=)", RegexOptions.Compiled);
            return ReplaceRegexInCode(
                source,
                unqualifiedRegistrarRegex,
                match => ShouldMigrateLegacyTypeReference(source, "CustomToolManager", match.Index)
                    ? $"{CurrentNamespace}.UnityCliLoopToolRegistrar"
                    : match.Value,
                ref replacementCount);
        }

        internal static string ReplaceLegacyContractTypeNamesInCode(
            string source,
            string[] aliases,
            ref int replacementCount)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(aliases != null, "aliases must not be null");

            string migratedContent = source;
            foreach (TypeReplacementRule rule in ToolContractTypeReplacementRules)
            {
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
                        _ => $"{alias}.{rule.CurrentName}",
                        ref replacementCount);
                }

                Regex unqualifiedRegex = new(
                    $@"(?<![\.:])\b{Regex.Escape(rule.LegacyName)}\b(?!\s*=)",
                    RegexOptions.Compiled);
                migratedContent = ReplaceRegexInCode(
                    migratedContent,
                    unqualifiedRegex,
                    match => ShouldMigrateLegacyTypeReference(migratedContent, rule.LegacyName, match.Index)
                        ? rule.CurrentName
                        : match.Value,
                    ref replacementCount);
            }

            return migratedContent;
        }

        internal static string ReplaceLegacyDomainTypeNamesInCode(
            string source,
            string[] aliases,
            ref int replacementCount)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(aliases != null, "aliases must not be null");

            string migratedContent = source;
            foreach (TypeReplacementRule rule in DomainTypeReplacementRules)
            {
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

                Regex unqualifiedRegex = new(
                    $@"(?<![\.:])\b{Regex.Escape(rule.LegacyName)}\b(?!\s*=)",
                    RegexOptions.Compiled);
                migratedContent = ReplaceRegexInCode(
                    migratedContent,
                    unqualifiedRegex,
                    match => ShouldMigrateLegacyTypeReference(migratedContent, rule.LegacyName, match.Index)
                        ? $"{CurrentNamespace}.{rule.CurrentName}"
                        : match.Value,
                    ref replacementCount);
            }

            return migratedContent;
        }

        internal static string ReplaceLegacyApplicationTypeNamesInCode(
            string source,
            string[] aliases,
            bool canMigrateBareLegacyApplicationApi,
            bool canPreserveBareCurrentToolContractsReferences,
            string[] assemblyDeclaredTypeNames,
            ref int replacementCount)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(aliases != null, "aliases must not be null");
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
                        ShouldMigrateLegacyTypeReference(migratedContent, rule.LegacyName, match.Index)
                            ? unqualifiedReplacement
                            : match.Value,
                    ref replacementCount);
            }

            return migratedContent;
        }

        internal static string ReplaceLegacyFirstPartyScreenshotTypeNamesInCode(
            string source,
            string[] aliases,
            bool canMigrateBareLegacyFirstPartyScreenshotApi,
            bool canPreserveBareCurrentToolContractsReferences,
            string[] assemblyDeclaredTypeNames,
            ref int replacementCount)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(aliases != null, "aliases must not be null");
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

                Regex unqualifiedRegex = new(
                    $@"(?<![\.:])\b{Regex.Escape(rule.LegacyName)}\b(?!\s*=)",
                    RegexOptions.Compiled);
                string unqualifiedReplacement = canPreserveBareCurrentToolContractsReferences
                    ? rule.CurrentName
                    : $"{CurrentNamespace}.{rule.CurrentName}";
                migratedContent = ReplaceRegexInCode(
                    migratedContent,
                    unqualifiedRegex,
                    match => canMigrateBareLegacyFirstPartyScreenshotApi &&
                        !hasProtectedTypeDeclaration &&
                        ShouldMigrateLegacyTypeReference(migratedContent, rule.LegacyName, match.Index)
                            ? unqualifiedReplacement
                            : match.Value,
                    ref replacementCount);
            }

            return migratedContent;
        }

        internal static string ReplaceLegacyToolInfoTypeReferencesInCode(string source, ref int replacementCount)
        {
            Debug.Assert(source != null, "source must not be null");

            return ReplaceRegexInCode(
                source,
                UnqualifiedToolInfoRegex,
                match => ShouldMigrateLegacyToolInfoTypeReference(source, match.Index)
                    ? $"{CurrentNamespace}.ToolInfo"
                    : match.Value,
                ref replacementCount);
        }

        internal static bool ShouldMigrateLegacyToolInfoTypeReference(string source, int toolInfoIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(toolInfoIndex >= 0, "toolInfoIndex must not be negative");

            return ShouldMigrateLegacyTypeReference(source, "ToolInfo", toolInfoIndex);
        }

        internal static bool ShouldMigrateLegacyTypeReference(string source, string typeName, int typeNameIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(!string.IsNullOrEmpty(typeName), "typeName must not be empty");
            Debug.Assert(typeNameIndex >= 0, "typeNameIndex must not be negative");

            if (IsLegacyAssemblyScopedTypeDeclaration(source, typeNameIndex))
            {
                return false;
            }

            char nextCharacter = ReadNextNonWhitespaceCharacter(source, typeNameIndex + typeName.Length);
            char previousCharacter = ReadPreviousNonWhitespaceCharacter(source, typeNameIndex);
            if (nextCharacter == '(' && !PreviousCodeTokenEquals(source, typeNameIndex, "new"))
            {
                return false;
            }

            return !IsDeclarationIdentifierTerminator(nextCharacter) ||
                !CanPrecedeDeclarationIdentifier(previousCharacter);
        }
    }
}
