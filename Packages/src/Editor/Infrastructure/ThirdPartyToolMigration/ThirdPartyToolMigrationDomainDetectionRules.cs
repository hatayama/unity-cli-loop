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
    internal static class ThirdPartyToolMigrationDomainDetectionRules
    {
        internal static bool ContainsAliasQualifiedName(string source, string alias, string typeName)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(!string.IsNullOrEmpty(alias), "alias must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(typeName), "typeName must not be null or empty");

            Regex aliasQualifiedRegex = new(
                $@"(?<!\w){Regex.Escape(alias)}\.{Regex.Escape(typeName)}\b",
                RegexOptions.Compiled);
            return RegexMatchesCode(source, aliasQualifiedRegex);
        }

        internal static bool ContainsLegacyRegistrarReference(
            string source,
            bool canMigrateBareLegacyRegistrar,
            string[] aliases)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(aliases != null, "aliases must not be null");

            if (RegexMatchesCode(source, LegacyQualifiedRegistrarRegex))
            {
                return true;
            }

            foreach (string alias in aliases)
            {
                if (ContainsAliasQualifiedName(source, alias, "CustomToolManager"))
                {
                    return true;
                }
            }

            return canMigrateBareLegacyRegistrar && ContainsMigratableUnqualifiedLegacyRegistrarReference(source);
        }

        internal static bool ContainsLegacyRegistrarDomainReturnReference(
            string source,
            bool canMigrateBareLegacyRegistrar,
            string[] aliases)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(aliases != null, "aliases must not be null");

            if (RegexMatchesCode(source, LegacyQualifiedRegistrarDomainReturnRegex))
            {
                return true;
            }

            foreach (string alias in aliases)
            {
                if (ContainsLegacyAliasQualifiedRegistrarDomainReturn(source, alias))
                {
                    return true;
                }
            }

            return canMigrateBareLegacyRegistrar &&
                ContainsMigratableUnqualifiedLegacyRegistrarDomainReturn(source);
        }

        internal static bool ContainsLegacyAliasQualifiedRegistrarDomainReturn(string source, string alias)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(!string.IsNullOrEmpty(alias), "alias must not be null or empty");

            Regex aliasQualifiedRegex = new(
                $@"(?<!\w){Regex.Escape(alias)}\.CustomToolManager\s*\.\s*GetRegisteredCustomTools\s*\(",
                RegexOptions.Compiled);
            return RegexMatchesCode(source, aliasQualifiedRegex);
        }

        internal static bool ContainsMigratableUnqualifiedLegacyRegistrarReference(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            MatchCollection matches = LegacyRegistrarRegex.Matches(source);
            foreach (Match match in matches)
            {
                if (codeTextMask.IsCodeAt(match.Index) &&
                    ShouldMigrateLegacyTypeReference(source, "CustomToolManager", match.Index))
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool ContainsMigratableUnqualifiedLegacyRegistrarDomainReturn(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            MatchCollection matches = LegacyRegistrarDomainReturnRegex.Matches(source);
            foreach (Match match in matches)
            {
                if (codeTextMask.IsCodeAt(match.Index) &&
                    ShouldMigrateLegacyTypeReference(source, "CustomToolManager", match.Index))
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool ContainsLegacyDomainHelperReference(
            string source,
            bool canMigrateBareLegacyDomainHelper,
            string[] aliases)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(aliases != null, "aliases must not be null");

            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            foreach (TypeReplacementRule rule in DomainTypeReplacementRules)
            {
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

                if (canMigrateBareLegacyDomainHelper &&
                    ContainsLegacyAssemblyScopedTypeName(source, codeTextMask, rule.LegacyName))
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool ContainsCurrentDomainHelperApi(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            bool hasCurrentDomainNamespaceUsage = RegexMatchesCode(source, CurrentDomainNamespaceRegex);
            return ContainsCurrentDomainHelperApiForAssembly(source, hasCurrentDomainNamespaceUsage);
        }

        internal static bool ContainsCurrentDomainHelperApiForAssembly(
            string source,
            bool canUseBareCurrentDomainType)
        {
            Debug.Assert(source != null, "source must not be null");

            foreach (TypeReplacementRule rule in DomainTypeReplacementRules)
            {
                Regex fullyQualifiedRegex = new(
                    $@"(?:(?:global::)?{Regex.Escape(CurrentDomainNamespace)}\.){Regex.Escape(rule.CurrentName)}\b",
                    RegexOptions.Compiled);
                if (RegexMatchesCode(source, fullyQualifiedRegex))
                {
                    return true;
                }

                Regex unqualifiedRegex = new(
                    $@"(?<![\.:])\b{Regex.Escape(rule.CurrentName)}\b",
                    RegexOptions.Compiled);
                if (canUseBareCurrentDomainType && RegexMatchesCode(source, unqualifiedRegex))
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool ContainsCurrentDomainContractAliasReference(
            string source,
            string[] currentDomainNamespaceAliases)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(
                currentDomainNamespaceAliases != null,
                "currentDomainNamespaceAliases must not be null");

            foreach (string alias in currentDomainNamespaceAliases)
            {
                if (ContainsAliasQualifiedName(source, alias, "ToolInfo"))
                {
                    return true;
                }

                foreach (TypeReplacementRule rule in DomainTypeReplacementRules)
                {
                    if (ContainsAliasQualifiedName(source, alias, rule.CurrentName))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        internal static bool ContainsLegacyAssemblyScopedTypeName(
            string source,
            CodeTextMask codeTextMask,
            string typeName)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(!string.IsNullOrEmpty(typeName), "typeName must not be null or empty");

            Regex typeNameRegex = new($@"(?<![\.:])\b{Regex.Escape(typeName)}\b", RegexOptions.Compiled);
            MatchCollection matches = typeNameRegex.Matches(source);
            foreach (Match match in matches)
            {
                if (!codeTextMask.IsCodeAt(match.Index))
                {
                    continue;
                }

                if (IsLegacyAssemblyScopedTypeDeclaration(source, match.Index))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        internal static bool DeclaresLocalType(string source, string typeName)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(!string.IsNullOrEmpty(typeName), "typeName must not be null or empty");

            return GetDeclaredTypeNames(source).Contains(typeName);
        }
    }
}
