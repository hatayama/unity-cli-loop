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
    public static class ThirdPartyToolMigrationTypeReplacementRules
    {
        public static string ReplaceLegacyRegistrarAliasesInCode(
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

        public static string ReplaceCurrentPublicContractNamespacesInCode(string source, ref int replacementCount)
        {
            Debug.Assert(source != null, "source must not be null");

            // Do not blanket-rewrite current package namespaces because many first-party/internal types still live there.
            return source;
        }

        public static string ReplaceUnqualifiedLegacyRegistrarReferencesInCode(
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

        public static string ReplaceLegacyContractTypeNamesInCode(
            string source,
            string[] aliases,
            string[] assemblyDeclaredTypeNames,
            ref int replacementCount)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(aliases != null, "aliases must not be null");
            Debug.Assert(assemblyDeclaredTypeNames != null, "assemblyDeclaredTypeNames must not be null");

            string migratedContent = source;
            foreach (TypeReplacementRule rule in ToolContractTypeReplacementRules)
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
                        _ => $"{alias}.{rule.CurrentName}",
                        ref replacementCount);
                }

                Regex unqualifiedRegex = new(
                    $@"(?<![\.:])\b{Regex.Escape(rule.LegacyName)}\b(?!\s*=)",
                    RegexOptions.Compiled);
                migratedContent = ReplaceRegexInCode(
                    migratedContent,
                    unqualifiedRegex,
                    match => !hasProtectedTypeDeclaration &&
                        ShouldMigrateLegacyTypeReference(migratedContent, rule.LegacyName, match.Index)
                        ? rule.CurrentName
                        : match.Value,
                    ref replacementCount);
            }

            return migratedContent;
        }

        public static string ReplaceLegacyDomainTypeNamesInCode(
            string source,
            string[] aliases,
            string[] assemblyDeclaredTypeNames,
            ref int replacementCount)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(aliases != null, "aliases must not be null");
            Debug.Assert(assemblyDeclaredTypeNames != null, "assemblyDeclaredTypeNames must not be null");

            string migratedContent = source;
            foreach (TypeReplacementRule rule in DomainTypeReplacementRules)
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
                migratedContent = ReplaceRegexInCode(
                    migratedContent,
                    unqualifiedRegex,
                    match => !hasProtectedTypeDeclaration &&
                        ShouldMigrateLegacyTypeReference(migratedContent, rule.LegacyName, match.Index)
                        ? $"{CurrentNamespace}.{rule.CurrentName}"
                        : match.Value,
                    ref replacementCount);
            }

            return migratedContent;
        }

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
                        ShouldMigrateLegacyTypeReference(migratedContent, rule.LegacyName, match.Index)
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
                        ShouldMigrateLegacyTypeReference(migratedContent, rule.LegacyName, match.Index)
                            ? unqualifiedReplacement
                            : match.Value,
                    ref replacementCount);
            }

            return migratedContent;
        }

        public static string ReplaceCurrentDomainContractTypeNamesInCode(
            string source,
            string[] currentDomainNamespaceAliases,
            bool canMigrateBareCurrentDomainContractType,
            bool canPreserveBareCurrentToolContractsReferences,
            string[] assemblyDeclaredTypeNames,
            ref int replacementCount)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(
                currentDomainNamespaceAliases != null,
                "currentDomainNamespaceAliases must not be null");
            Debug.Assert(assemblyDeclaredTypeNames != null, "assemblyDeclaredTypeNames must not be null");

            string migratedContent = source;
            foreach (TypeReplacementRule rule in DomainTypeReplacementRules)
            {
                migratedContent = ReplaceCurrentDomainContractTypeNameInCode(
                    migratedContent,
                    rule.CurrentName,
                    currentDomainNamespaceAliases,
                    canMigrateBareCurrentDomainContractType,
                    canPreserveBareCurrentToolContractsReferences,
                    assemblyDeclaredTypeNames,
                    ref replacementCount);
            }

            return ReplaceCurrentDomainContractTypeNameInCode(
                migratedContent,
                "ToolInfo",
                currentDomainNamespaceAliases,
                canMigrateBareCurrentDomainContractType,
                canPreserveBareCurrentToolContractsReferences,
                assemblyDeclaredTypeNames,
                ref replacementCount);
        }

        public static string ReplaceCurrentDomainContractTypeNameInCode(
            string source,
            string typeName,
            string[] currentDomainNamespaceAliases,
            bool canMigrateBareCurrentDomainContractType,
            bool canPreserveBareCurrentToolContractsReferences,
            string[] assemblyDeclaredTypeNames,
            ref int replacementCount)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(!string.IsNullOrEmpty(typeName), "typeName must not be null or empty");
            Debug.Assert(
                currentDomainNamespaceAliases != null,
                "currentDomainNamespaceAliases must not be null");
            Debug.Assert(assemblyDeclaredTypeNames != null, "assemblyDeclaredTypeNames must not be null");

            bool hasProtectedTypeDeclaration = DeclaresLocalType(source, typeName) ||
                assemblyDeclaredTypeNames.Contains(typeName);
            string migratedContent = source;
            Regex fullyQualifiedRegex = new(
                $@"(?:(?:global::)?{Regex.Escape(CurrentDomainNamespace)}\.){Regex.Escape(typeName)}\b",
                RegexOptions.Compiled);
            migratedContent = ReplaceRegexInCode(
                migratedContent,
                fullyQualifiedRegex,
                _ => $"{CurrentNamespace}.{typeName}",
                ref replacementCount);

            foreach (string alias in currentDomainNamespaceAliases)
            {
                Regex aliasRegex = new(
                    $@"(?<!\w){Regex.Escape(alias)}\.{Regex.Escape(typeName)}\b",
                    RegexOptions.Compiled);
                migratedContent = ReplaceRegexInCode(
                    migratedContent,
                    aliasRegex,
                    _ => $"{CurrentNamespace}.{typeName}",
                    ref replacementCount);
            }

            Regex unqualifiedRegex = new(
                $@"(?<![\.:])\b{Regex.Escape(typeName)}\b(?!\s*=)",
                RegexOptions.Compiled);
            string unqualifiedReplacement = canPreserveBareCurrentToolContractsReferences
                ? typeName
                : $"{CurrentNamespace}.{typeName}";
            return ReplaceRegexInCode(
                migratedContent,
                unqualifiedRegex,
                match => canMigrateBareCurrentDomainContractType &&
                    !hasProtectedTypeDeclaration &&
                    ShouldMigrateLegacyTypeReference(migratedContent, typeName, match.Index)
                        ? unqualifiedReplacement
                        : match.Value,
                ref replacementCount);
        }

        public static string ReplaceLegacyToolInfoTypeReferencesInCode(
            string source,
            string[] assemblyDeclaredTypeNames,
            ref int replacementCount)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(assemblyDeclaredTypeNames != null, "assemblyDeclaredTypeNames must not be null");

            bool hasProtectedTypeDeclaration = DeclaresLocalType(source, "ToolInfo") ||
                assemblyDeclaredTypeNames.Contains("ToolInfo");
            return ReplaceRegexInCode(
                source,
                UnqualifiedToolInfoRegex,
                match => !hasProtectedTypeDeclaration &&
                    ShouldMigrateLegacyToolInfoTypeReference(source, match.Index)
                    ? $"{CurrentNamespace}.ToolInfo"
                    : match.Value,
                ref replacementCount);
        }

        public static bool ShouldMigrateLegacyToolInfoTypeReference(string source, int toolInfoIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(toolInfoIndex >= 0, "toolInfoIndex must not be negative");

            return ShouldMigrateLegacyTypeReference(source, "ToolInfo", toolInfoIndex);
        }

        public static bool ShouldMigrateLegacyTypeReference(string source, string typeName, int typeNameIndex)
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
