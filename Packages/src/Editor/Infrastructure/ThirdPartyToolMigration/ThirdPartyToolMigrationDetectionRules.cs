using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationAsmdefRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationAttributeRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationCSharpRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationCodeTextMaskBuilder;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationCodeTextMaskInterpolationRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationCodeTextDetectionRules;
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
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTypeReplacementRules;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    internal static class ThirdPartyToolMigrationDetectionRules
    {
        private static readonly string[] MigrationCandidateFragments =
        {
            LegacyNamespace,
            CurrentNamespace,
            CurrentApplicationNamespace,
            CurrentDomainNamespace,
            CurrentFirstPartyToolsNamespace,
            "McpTool",
            "CustomToolManager",
            LegacyEditorDelayTypeName,
            LegacyTimerDelayTypeName,
            LegacyMainThreadSwitcherTypeName,
            LegacyPlayerLoopTimingTypeName,
            LegacyEditorWindowCaptureUtilityTypeName,
            "UnityCliLoopToolRegistrar",
            "ToolInfo"
        };

        internal static bool ContainsLegacyCSharpApi(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return ContainsLegacyToolMigrationMarker(source);
        }

        internal static bool ContainsMigrationCandidateText(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return ContainsAnyTextFragment(source, MigrationCandidateFragments) ||
                ContainsAnyReplacementRuleName(source, ToolContractTypeReplacementRules) ||
                ContainsAnyReplacementRuleName(source, DomainTypeReplacementRules) ||
                ContainsAnyReplacementRuleName(source, ApplicationTypeReplacementRules) ||
                ContainsAnyReplacementRuleName(source, FirstPartyScreenshotTypeReplacementRules);
        }

        private static bool ContainsAnyTextFragment(string source, string[] fragments)
        {
            foreach (string fragment in fragments)
            {
                if (ContainsTextFragment(source, fragment))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsAnyReplacementRuleName(string source, TypeReplacementRule[] rules)
        {
            foreach (TypeReplacementRule rule in rules)
            {
                if (ContainsTextFragment(source, rule.LegacyName) ||
                    ContainsTextFragment(source, rule.CurrentName))
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool ContainsLegacyMigrationCandidateText(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return ContainsTextFragment(source, LegacyNamespace) ||
                ContainsTextFragment(source, LegacyEditorAssemblyName) ||
                ContainsTextFragment(source, LegacyRuntimeAssemblyName);
        }

        internal static bool ContainsLegacyAsmdefNameReference(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            if (!ContainsTextFragment(source, LegacyEditorAssemblyName) &&
                !ContainsTextFragment(source, LegacyRuntimeAssemblyName))
            {
                return false;
            }

            JObject asmdef = JObject.Parse(source);
            if (asmdef["references"] is not JArray references)
            {
                return false;
            }

            foreach (JToken reference in references)
            {
                string referenceValue = reference.Value<string>() ?? string.Empty;
                if (string.Equals(referenceValue, LegacyEditorAssemblyName, StringComparison.Ordinal) ||
                    string.Equals(referenceValue, LegacyRuntimeAssemblyName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool ContainsLegacyAssemblyScopedApi(string source, string[] legacyAssemblyAliases)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(legacyAssemblyAliases != null, "legacyAssemblyAliases must not be null");

            return RegexMatchesCode(source, LegacyBaseTypeUsageRegex) ||
                RegexMatchesCode(source, LegacyAssemblyScopedApiUsageRegex) ||
                ContainsLegacyAssemblyScopedTypeReference(source) ||
                ContainsLegacyAliasQualifiedAssemblyScopedApi(source, legacyAssemblyAliases) ||
                ContainsLegacyEditorDelayFrameCall(
                    source,
                    legacyAssemblyAliases,
                    canMigrateBareLegacyEditorDelay: true) ||
                ContainsLegacyEditorWindowCaptureUtilityCall(
                    source,
                    legacyAssemblyAliases,
                    Array.Empty<string>(),
                    canMigrateBareLegacyEditorWindowCaptureUtility: true,
                    GetDeclaredTypeNames(source)) ||
                ContainsLegacyTimerDelayInvocation(
                    source,
                    legacyAssemblyAliases,
                    canMigrateBareLegacyTimerDelay: true) ||
                ContainsLegacyApplicationReference(
                    source,
                    true,
                    legacyAssemblyAliases,
                    Array.Empty<string>(),
                    GetDeclaredTypeNames(source)) ||
                ContainsLegacyFirstPartyScreenshotReference(
                    source,
                    true,
                    legacyAssemblyAliases,
                    Array.Empty<string>()) ||
                ContainsLegacyToolAttributeList(
                    source,
                    legacyAssemblyAliases,
                    canMigrateBareLegacyToolAttribute: true);
        }

        internal static bool ContainsLegacyGlobalUsing(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return RegexMatchesCode(source, LegacyGlobalUsingRegex);
        }

        internal static bool ContainsCurrentDomainGlobalUsing(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return RegexMatchesCode(source, CurrentDomainGlobalUsingRegex);
        }

        internal static bool ContainsCurrentDomainUsing(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return RegexMatchesCode(source, CurrentDomainUsingRegex);
        }

        internal static bool ContainsCurrentDomainNamespaceAlias(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return RegexMatchesCode(source, CurrentDomainNamespaceAliasRegex);
        }

        internal static bool ContainsCurrentToolContractsGlobalUsing(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return RegexMatchesCode(source, CurrentToolContractsGlobalUsingRegex);
        }

        internal static bool ContainsCurrentApplicationGlobalUsing(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return RegexMatchesCode(source, CurrentApplicationGlobalUsingRegex);
        }

        internal static bool ContainsCurrentApplicationUsing(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return RegexMatchesCode(source, CurrentApplicationUsingRegex);
        }

        internal static bool ContainsCurrentApplicationNamespaceAlias(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return RegexMatchesCode(source, CurrentApplicationNamespaceAliasRegex);
        }

        internal static bool ContainsCurrentFirstPartyToolsGlobalUsing(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return RegexMatchesCode(source, CurrentFirstPartyToolsGlobalUsingRegex);
        }

        internal static bool ContainsCurrentFirstPartyToolsNamespaceAlias(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return RegexMatchesCode(source, CurrentFirstPartyToolsNamespaceAliasRegex);
        }

        internal static string[] GetLegacyGlobalNamespaceAliases(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return GetRegexGroupValuesInCode(source, LegacyGlobalNamespaceAliasRegex, "alias");
        }

        internal static string[] GetCurrentDomainGlobalNamespaceAliases(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return GetRegexGroupValuesInCode(source, CurrentDomainGlobalNamespaceAliasRegex, "alias");
        }

        internal static string[] GetLegacyGlobalToolInfoTypeAliases(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return GetRegexGroupValuesInCode(source, LegacyGlobalToolInfoTypeAliasRegex, "alias");
        }

        internal static string[] GetCurrentApplicationGlobalNamespaceAliases(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return GetRegexGroupValuesInCode(source, CurrentApplicationGlobalNamespaceAliasRegex, "alias");
        }

        internal static string[] GetCurrentFirstPartyToolsGlobalNamespaceAliases(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return GetRegexGroupValuesInCode(source, CurrentFirstPartyToolsGlobalNamespaceAliasRegex, "alias");
        }

        internal static bool ContainsLegacyGlobalToolInfoTypeAlias(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return RegexMatchesCode(source, LegacyGlobalToolInfoTypeAliasRegex);
        }

        internal static bool ContainsLegacyTypeAliasReference(string source, string[] aliases)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(aliases != null, "aliases must not be null");

            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            foreach (string alias in aliases)
            {
                Regex aliasRegex = new($@"(?<![\w.]){Regex.Escape(alias)}\b", RegexOptions.Compiled);
                MatchCollection matches = aliasRegex.Matches(source);
                foreach (Match match in matches)
                {
                    if (codeTextMask.IsCodeAt(match.Index))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        internal static bool IsExcludedDirectoryName(string directoryName)
        {
            Debug.Assert(!string.IsNullOrEmpty(directoryName), "directoryName must not be null or empty");

            return ExcludedDirectoryNames.Any(
                excludedDirectoryName => string.Equals(
                    excludedDirectoryName,
                    directoryName,
                    StringComparison.OrdinalIgnoreCase));
        }

        internal static string[] GetExcludedDirectoryNames()
        {
            string[] names = new string[ExcludedDirectoryNames.Length];
            Array.Copy(ExcludedDirectoryNames, names, ExcludedDirectoryNames.Length);
            return names;
        }

    }
}
