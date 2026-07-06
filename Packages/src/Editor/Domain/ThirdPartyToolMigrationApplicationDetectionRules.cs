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
    public static class ThirdPartyToolMigrationApplicationDetectionRules
    {
        public static bool ContainsLegacyTimerDelayInvocation(
            string source,
            string[] legacyNamespaceAliases,
            bool canMigrateBareLegacyTimerDelay)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(legacyNamespaceAliases != null, "legacyNamespaceAliases must not be null");

            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            MatchCollection matches = LegacyTimerDelayInvocationRegex.Matches(source);
            foreach (Match match in matches)
            {
                if (codeTextMask.IsCodeAt(match.Index) &&
                    IsLegacyTimerDelayInvocationMatch(
                        match,
                        legacyNamespaceAliases,
                        canMigrateBareLegacyTimerDelay))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool ContainsLegacyApplicationReference(
            string source,
            bool canMigrateBareLegacyApplicationApi,
            string[] aliases,
            string[] currentApplicationNamespaceAliases,
            string[] assemblyDeclaredTypeNames)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(aliases != null, "aliases must not be null");
            Debug.Assert(
                currentApplicationNamespaceAliases != null,
                "currentApplicationNamespaceAliases must not be null");
            Debug.Assert(assemblyDeclaredTypeNames != null, "assemblyDeclaredTypeNames must not be null");

            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            foreach (TypeReplacementRule rule in ApplicationTypeReplacementRules)
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

                if (canMigrateBareLegacyApplicationApi &&
                    !hasProtectedTypeDeclaration &&
                    ContainsLegacyAssemblyScopedTypeName(source, codeTextMask, rule.LegacyName))
                {
                    return true;
                }
            }

            if (ContainsLegacyMainThreadSwitcherSwitchCall(
                    source,
                    aliases,
                    currentApplicationNamespaceAliases,
                    canMigrateBareLegacyApplicationApi,
                    assemblyDeclaredTypeNames))
            {
                return true;
            }

            return false;
        }

        public static bool ContainsLegacyMainThreadSwitcherSwitchCall(
            string source,
            string[] legacyNamespaceAliases,
            string[] currentApplicationNamespaceAliases,
            bool canMigrateBareLegacyMainThreadSwitcher,
            string[] assemblyDeclaredTypeNames)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(legacyNamespaceAliases != null, "legacyNamespaceAliases must not be null");
            Debug.Assert(
                currentApplicationNamespaceAliases != null,
                "currentApplicationNamespaceAliases must not be null");
            Debug.Assert(assemblyDeclaredTypeNames != null, "assemblyDeclaredTypeNames must not be null");

            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            MatchCollection matches = LegacyMainThreadSwitcherSwitchRegex.Matches(source);
            foreach (Match match in matches)
            {
                if (codeTextMask.IsCodeAt(match.Index) &&
                    IsLegacyMainThreadSwitcherCallMatch(
                        source,
                        match,
                        legacyNamespaceAliases,
                        currentApplicationNamespaceAliases,
                        canMigrateBareLegacyMainThreadSwitcher,
                        assemblyDeclaredTypeNames))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool ContainsCurrentApplicationReference(
            string source,
            bool canUseBareCurrentApplicationType,
            string[] currentApplicationNamespaceAliases,
            string[] assemblyDeclaredTypeNames)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(
                currentApplicationNamespaceAliases != null,
                "currentApplicationNamespaceAliases must not be null");
            Debug.Assert(assemblyDeclaredTypeNames != null, "assemblyDeclaredTypeNames must not be null");

            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            foreach (TypeReplacementRule rule in ApplicationTypeReplacementRules)
            {
                bool hasProtectedTypeDeclaration = DeclaresLocalType(source, rule.CurrentName) ||
                    assemblyDeclaredTypeNames.Contains(rule.CurrentName);

                Regex fullyQualifiedRegex = new(
                    $@"(?:(?:global::)?{Regex.Escape(CurrentApplicationNamespace)}\.){Regex.Escape(rule.CurrentName)}\b",
                    RegexOptions.Compiled);
                if (RegexMatchesCode(source, fullyQualifiedRegex))
                {
                    return true;
                }

                foreach (string alias in currentApplicationNamespaceAliases)
                {
                    if (ContainsAliasQualifiedName(source, alias, rule.CurrentName))
                    {
                        return true;
                    }
                }

                if (canUseBareCurrentApplicationType &&
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
