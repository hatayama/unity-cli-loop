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
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationCodeTextDetectionRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationScreenshotArgumentRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationScreenshotDeconstructionRules;
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
    public static class ThirdPartyToolMigrationScreenshotDetectionRules
    {
        public static bool ContainsLegacyEditorWindowCaptureUtilityCall(
            string source,
            string[] legacyNamespaceAliases,
            string[] currentFirstPartyToolsNamespaceAliases,
            bool canMigrateBareLegacyEditorWindowCaptureUtility,
            string[] assemblyDeclaredTypeNames)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(legacyNamespaceAliases != null, "legacyNamespaceAliases must not be null");
            Debug.Assert(
                currentFirstPartyToolsNamespaceAliases != null,
                "currentFirstPartyToolsNamespaceAliases must not be null");
            Debug.Assert(assemblyDeclaredTypeNames != null, "assemblyDeclaredTypeNames must not be null");

            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            bool hasProtectedBareEditorWindowCaptureUtility =
                HasProtectedEditorWindowCaptureUtilityDeclaration(source, assemblyDeclaredTypeNames);
            if (ContainsLegacyEditorWindowCaptureUtilityCall(
                    source,
                    codeTextMask,
                    LegacyEditorWindowCaptureUtilityCaptureWindowRegex,
                    legacyNamespaceAliases,
                    currentFirstPartyToolsNamespaceAliases,
                    canMigrateBareLegacyEditorWindowCaptureUtility,
                    hasProtectedBareEditorWindowCaptureUtility))
            {
                return true;
            }

            if (ContainsLegacyEditorWindowCaptureUtilityCall(
                    source,
                    codeTextMask,
                    LegacyEditorWindowCaptureUtilityCaptureWindowInvocationRegex,
                    legacyNamespaceAliases,
                    currentFirstPartyToolsNamespaceAliases,
                    canMigrateBareLegacyEditorWindowCaptureUtility,
                    hasProtectedBareEditorWindowCaptureUtility))
            {
                return true;
            }

            if (ContainsLegacyEditorWindowCaptureUtilityCall(
                    source,
                    codeTextMask,
                    LegacyEditorWindowCaptureUtilityCaptureGameRenderingRegex,
                    legacyNamespaceAliases,
                    currentFirstPartyToolsNamespaceAliases,
                    canMigrateBareLegacyEditorWindowCaptureUtility,
                    hasProtectedBareEditorWindowCaptureUtility))
            {
                return true;
            }

            return ContainsLegacyEditorWindowCaptureUtilityCall(
                source,
                codeTextMask,
                LegacyEditorWindowCaptureUtilityCaptureGameRenderingInvocationRegex,
                legacyNamespaceAliases,
                currentFirstPartyToolsNamespaceAliases,
                canMigrateBareLegacyEditorWindowCaptureUtility,
                hasProtectedBareEditorWindowCaptureUtility);
        }

        public static bool ContainsLegacyEditorWindowCaptureUtilityCall(
            string source,
            CodeTextMask codeTextMask,
            Regex regex,
            string[] legacyNamespaceAliases,
            string[] currentFirstPartyToolsNamespaceAliases,
            bool canMigrateBareLegacyEditorWindowCaptureUtility,
            bool hasProtectedBareEditorWindowCaptureUtility)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(regex != null, "regex must not be null");
            Debug.Assert(legacyNamespaceAliases != null, "legacyNamespaceAliases must not be null");
            Debug.Assert(
                currentFirstPartyToolsNamespaceAliases != null,
                "currentFirstPartyToolsNamespaceAliases must not be null");

            MatchCollection matches = regex.Matches(source);
            foreach (Match match in matches)
            {
                if (codeTextMask.IsCodeAt(match.Index) &&
                    IsLegacyEditorWindowCaptureUtilityCallMatch(
                        match,
                        legacyNamespaceAliases,
                        currentFirstPartyToolsNamespaceAliases,
                        canMigrateBareLegacyEditorWindowCaptureUtility,
                        hasProtectedBareEditorWindowCaptureUtility))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool CanMigrateBareLegacyEditorWindowCaptureUtilityForAssembly(
            string source,
            bool hasLegacyAssemblySource,
            bool hasAssemblyScopedCurrentToolContractsUsing,
            bool hasAssemblyScopedCurrentFirstPartyToolsUsing,
            string[] legacyNamespaceAliases)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(legacyNamespaceAliases != null, "legacyNamespaceAliases must not be null");

            return hasLegacyAssemblySource ||
                RegexMatchesCode(source, LegacyNamespaceRegex) ||
                legacyNamespaceAliases.Length > 0 ||
                RegexMatchesCode(source, CurrentToolContractsNamespaceRegex) ||
                hasAssemblyScopedCurrentToolContractsUsing ||
                RegexMatchesCode(source, CurrentFirstPartyToolsNamespaceRegex) ||
                hasAssemblyScopedCurrentFirstPartyToolsUsing;
        }

        public static bool ContainsLegacyEditorWindowCaptureUtilityMigration(
            string source,
            string[] legacyNamespaceAliases,
            string[] currentFirstPartyToolsNamespaceAliases,
            bool canMigrateBareLegacyEditorWindowCaptureUtility,
            string[] assemblyDeclaredTypeNames,
            bool requiresTimeoutArgumentMigration)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(legacyNamespaceAliases != null, "legacyNamespaceAliases must not be null");
            Debug.Assert(
                currentFirstPartyToolsNamespaceAliases != null,
                "currentFirstPartyToolsNamespaceAliases must not be null");
            Debug.Assert(assemblyDeclaredTypeNames != null, "assemblyDeclaredTypeNames must not be null");

            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            bool hasProtectedBareEditorWindowCaptureUtility =
                HasProtectedEditorWindowCaptureUtilityDeclaration(source, assemblyDeclaredTypeNames);
            if (ContainsLegacyEditorWindowCaptureUtilityMigration(
                    source,
                    codeTextMask,
                    LegacyEditorWindowCaptureUtilityCaptureWindowRegex,
                    legacyNamespaceAliases,
                    currentFirstPartyToolsNamespaceAliases,
                    canMigrateBareLegacyEditorWindowCaptureUtility,
                    hasProtectedBareEditorWindowCaptureUtility,
                    isGameRenderingCapture: false,
                    requiresTimeoutArgumentMigration))
            {
                return true;
            }

            if (ContainsLegacyEditorWindowCaptureUtilityMigration(
                    source,
                    codeTextMask,
                    LegacyEditorWindowCaptureUtilityCaptureWindowInvocationRegex,
                    legacyNamespaceAliases,
                    currentFirstPartyToolsNamespaceAliases,
                    canMigrateBareLegacyEditorWindowCaptureUtility,
                    hasProtectedBareEditorWindowCaptureUtility,
                    isGameRenderingCapture: false,
                    requiresTimeoutArgumentMigration))
            {
                return true;
            }

            if (ContainsLegacyEditorWindowCaptureUtilityMigration(
                    source,
                    codeTextMask,
                    LegacyEditorWindowCaptureUtilityCaptureGameRenderingRegex,
                    legacyNamespaceAliases,
                    currentFirstPartyToolsNamespaceAliases,
                    canMigrateBareLegacyEditorWindowCaptureUtility,
                    hasProtectedBareEditorWindowCaptureUtility,
                    isGameRenderingCapture: true,
                    requiresTimeoutArgumentMigration))
            {
                return true;
            }

            return ContainsLegacyEditorWindowCaptureUtilityMigration(
                source,
                codeTextMask,
                LegacyEditorWindowCaptureUtilityCaptureGameRenderingInvocationRegex,
                legacyNamespaceAliases,
                currentFirstPartyToolsNamespaceAliases,
                canMigrateBareLegacyEditorWindowCaptureUtility,
                hasProtectedBareEditorWindowCaptureUtility,
                isGameRenderingCapture: true,
                requiresTimeoutArgumentMigration);
        }

        public static bool ContainsLegacyEditorWindowCaptureUtilityMigration(
            string source,
            CodeTextMask codeTextMask,
            Regex regex,
            string[] legacyNamespaceAliases,
            string[] currentFirstPartyToolsNamespaceAliases,
            bool canMigrateBareLegacyEditorWindowCaptureUtility,
            bool hasProtectedBareEditorWindowCaptureUtility,
            bool isGameRenderingCapture,
            bool requiresTimeoutArgumentMigration)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(regex != null, "regex must not be null");
            Debug.Assert(legacyNamespaceAliases != null, "legacyNamespaceAliases must not be null");
            Debug.Assert(
                currentFirstPartyToolsNamespaceAliases != null,
                "currentFirstPartyToolsNamespaceAliases must not be null");

            MatchCollection matches = regex.Matches(source);
            foreach (Match match in matches)
            {
                if (!codeTextMask.IsCodeAt(match.Index) ||
                    !IsLegacyEditorWindowCaptureUtilityCallMatch(
                        match,
                        legacyNamespaceAliases,
                        currentFirstPartyToolsNamespaceAliases,
                        canMigrateBareLegacyEditorWindowCaptureUtility,
                        hasProtectedBareEditorWindowCaptureUtility))
                {
                    continue;
                }

                int openParenthesisIndex = match.Index + match.Length - 1;
                int closingParenthesisIndex = FindInvocationClosingParenthesisIndex(
                    source,
                    codeTextMask,
                    openParenthesisIndex);
                if (closingParenthesisIndex < 0)
                {
                    continue;
                }

                string argumentsSource = source.Substring(
                    openParenthesisIndex + 1,
                    closingParenthesisIndex - openParenthesisIndex - 1);
                string[] arguments = SplitAttributeArguments(argumentsSource);
                string[] migratedArguments = isGameRenderingCapture
                    ? GetMigratedEditorWindowCaptureUtilityGameRenderingArguments(arguments, "timeout")
                    : GetMigratedEditorWindowCaptureUtilityArguments(arguments, "timeout");
                if (migratedArguments.Length == 0)
                {
                    continue;
                }

                if (!requiresTimeoutArgumentMigration)
                {
                    return true;
                }

                string[] trimmedArguments = GetTrimmedInvocationArguments(arguments);
                if (!isGameRenderingCapture || trimmedArguments.Length == 2)
                {
                    return true;
                }
            }

            return false;
        }
        public static bool ContainsLegacyFirstPartyScreenshotReference(
            string source,
            bool canMigrateBareLegacyFirstPartyScreenshotApi,
            string[] aliases,
            string[] assemblyDeclaredTypeNames)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(aliases != null, "aliases must not be null");
            Debug.Assert(assemblyDeclaredTypeNames != null, "assemblyDeclaredTypeNames must not be null");

            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            foreach (TypeReplacementRule rule in FirstPartyScreenshotTypeReplacementRules)
            {
                bool hasProtectedTypeDeclaration = DeclaresLocalType(source, rule.LegacyName) ||
                    assemblyDeclaredTypeNames.Contains(rule.LegacyName);
                Regex fullyQualifiedRegex = new(
                    $@"(?:(?:global::)?{Regex.Escape(LegacyNamespace)}\.){Regex.Escape(rule.LegacyName)}\b",
                    RegexOptions.Compiled);
                if (RegexMatchesCode(source, fullyQualifiedRegex))
                {
                    return true;
                }

                foreach (string alias in aliases)
                {
                    if (ContainsAliasQualifiedName(source, alias, rule.LegacyName))
                    {
                        return true;
                    }
                }

                if (canMigrateBareLegacyFirstPartyScreenshotApi &&
                    !hasProtectedTypeDeclaration &&
                    ContainsLegacyAssemblyScopedTypeName(source, codeTextMask, rule.LegacyName))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool ContainsCurrentFirstPartyScreenshotReference(
            string source,
            bool canUseBareCurrentFirstPartyScreenshotType,
            string[] currentFirstPartyToolsNamespaceAliases,
            string[] assemblyDeclaredTypeNames)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(
                currentFirstPartyToolsNamespaceAliases != null,
                "currentFirstPartyToolsNamespaceAliases must not be null");
            Debug.Assert(assemblyDeclaredTypeNames != null, "assemblyDeclaredTypeNames must not be null");

            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            foreach (TypeReplacementRule rule in FirstPartyScreenshotTypeReplacementRules)
            {
                bool hasProtectedTypeDeclaration = DeclaresLocalType(source, rule.CurrentName) ||
                    assemblyDeclaredTypeNames.Contains(rule.CurrentName);
                Regex fullyQualifiedRegex = new(
                    $@"(?:(?:global::)?{Regex.Escape(CurrentFirstPartyToolsNamespace)}\.){Regex.Escape(rule.CurrentName)}\b",
                    RegexOptions.Compiled);
                if (RegexMatchesCode(source, fullyQualifiedRegex))
                {
                    return true;
                }

                foreach (string alias in currentFirstPartyToolsNamespaceAliases)
                {
                    if (ContainsAliasQualifiedName(source, alias, rule.CurrentName))
                    {
                        return true;
                    }
                }

                if (canUseBareCurrentFirstPartyScreenshotType &&
                    !hasProtectedTypeDeclaration &&
                    ContainsLegacyAssemblyScopedTypeName(source, codeTextMask, rule.CurrentName))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool ContainsCurrentFirstPartyScreenshotContractReference(
            string source,
            bool canUseBareCurrentFirstPartyScreenshotType,
            string[] currentFirstPartyToolsNamespaceAliases,
            string[] assemblyDeclaredTypeNames)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(
                currentFirstPartyToolsNamespaceAliases != null,
                "currentFirstPartyToolsNamespaceAliases must not be null");
            Debug.Assert(assemblyDeclaredTypeNames != null, "assemblyDeclaredTypeNames must not be null");

            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            foreach (TypeReplacementRule rule in FirstPartyScreenshotTypeReplacementRules)
            {
                if (string.Equals(
                        rule.CurrentName,
                        LegacyEditorWindowCaptureUtilityTypeName,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                bool hasProtectedTypeDeclaration = DeclaresLocalType(source, rule.CurrentName) ||
                    assemblyDeclaredTypeNames.Contains(rule.CurrentName);
                Regex fullyQualifiedRegex = new(
                    $@"(?:(?:global::)?{Regex.Escape(CurrentFirstPartyToolsNamespace)}\.){Regex.Escape(rule.CurrentName)}\b",
                    RegexOptions.Compiled);
                if (RegexMatchesCode(source, fullyQualifiedRegex))
                {
                    return true;
                }

                foreach (string alias in currentFirstPartyToolsNamespaceAliases)
                {
                    if (ContainsAliasQualifiedName(source, alias, rule.CurrentName))
                    {
                        return true;
                    }
                }

                if (canUseBareCurrentFirstPartyScreenshotType &&
                    !hasProtectedTypeDeclaration &&
                    ContainsLegacyAssemblyScopedTypeName(source, codeTextMask, rule.CurrentName))
                {
                    return true;
                }
            }

            return false;
        }

    }
}
