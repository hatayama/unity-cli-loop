using System;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;

using CodeTextMask = io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationParsingRules.CodeTextMask;
using TypeReplacementRule = io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationParsingRules.TypeReplacementRule;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationAliasRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationApplicationDetectionRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationCodeTextDetectionRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationEditorDelayRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationParsingRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationRuleCatalog;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationScreenshotDetectionRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationToolContractDetectionRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTypeReplacementRules;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Detects source-level third-party migration policies without infrastructure dependencies.
    /// </summary>
    public static class ThirdPartyToolMigrationSourceDetectionRules
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

        public static bool ContainsLegacyCSharpApi(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return ContainsLegacyToolMigrationMarker(source);
        }

        public static bool ContainsMigrationCandidateText(string source)
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

        public static bool ContainsLegacyMigrationCandidateText(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return ContainsTextFragment(source, LegacyNamespace) ||
                ContainsTextFragment(source, LegacyEditorAssemblyName) ||
                ContainsTextFragment(source, LegacyRuntimeAssemblyName);
        }

        public static bool ContainsLegacyAssemblyScopedApi(string source, string[] legacyAssemblyAliases)
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

        public static bool ContainsLegacyGlobalUsing(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return RegexMatchesCode(source, LegacyGlobalUsingRegex);
        }

        public static bool ContainsCurrentDomainGlobalUsing(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return RegexMatchesCode(source, CurrentDomainGlobalUsingRegex);
        }

        public static bool ContainsCurrentDomainUsing(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return RegexMatchesCode(source, CurrentDomainUsingRegex);
        }

        public static bool ContainsCurrentDomainNamespaceAlias(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return RegexMatchesCode(source, CurrentDomainNamespaceAliasRegex);
        }

        public static bool ContainsCurrentToolContractsGlobalUsing(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return RegexMatchesCode(source, CurrentToolContractsGlobalUsingRegex);
        }

        public static bool ContainsCurrentApplicationGlobalUsing(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return RegexMatchesCode(source, CurrentApplicationGlobalUsingRegex);
        }

        public static bool ContainsCurrentApplicationUsing(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return RegexMatchesCode(source, CurrentApplicationUsingRegex);
        }

        public static bool ContainsCurrentApplicationNamespaceAlias(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return RegexMatchesCode(source, CurrentApplicationNamespaceAliasRegex);
        }

        public static bool ContainsCurrentFirstPartyToolsGlobalUsing(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return RegexMatchesCode(source, CurrentFirstPartyToolsGlobalUsingRegex);
        }

        public static bool ContainsCurrentFirstPartyToolsNamespaceAlias(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return RegexMatchesCode(source, CurrentFirstPartyToolsNamespaceAliasRegex);
        }

        public static string[] GetLegacyGlobalNamespaceAliases(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return GetRegexGroupValuesInCode(source, LegacyGlobalNamespaceAliasRegex, "alias");
        }

        public static string[] GetCurrentDomainGlobalNamespaceAliases(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return GetRegexGroupValuesInCode(source, CurrentDomainGlobalNamespaceAliasRegex, "alias");
        }

        public static string[] GetLegacyGlobalToolInfoTypeAliases(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return GetRegexGroupValuesInCode(source, LegacyGlobalToolInfoTypeAliasRegex, "alias");
        }

        public static string[] GetCurrentApplicationGlobalNamespaceAliases(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return GetRegexGroupValuesInCode(source, CurrentApplicationGlobalNamespaceAliasRegex, "alias");
        }

        public static string[] GetCurrentFirstPartyToolsGlobalNamespaceAliases(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return GetRegexGroupValuesInCode(source, CurrentFirstPartyToolsGlobalNamespaceAliasRegex, "alias");
        }

        public static bool ContainsLegacyGlobalToolInfoTypeAlias(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return RegexMatchesCode(source, LegacyGlobalToolInfoTypeAliasRegex);
        }

        public static bool ContainsLegacyTypeAliasReference(string source, string[] aliases)
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

        public static bool IsExcludedDirectoryName(string directoryName)
        {
            Debug.Assert(!string.IsNullOrEmpty(directoryName), "directoryName must not be null or empty");

            return ExcludedDirectoryNames.Any(
                excludedDirectoryName => string.Equals(
                    excludedDirectoryName,
                    directoryName,
                    StringComparison.OrdinalIgnoreCase));
        }

        public static string[] GetExcludedDirectoryNames()
        {
            string[] names = new string[ExcludedDirectoryNames.Length];
            Array.Copy(ExcludedDirectoryNames, names, ExcludedDirectoryNames.Length);
            return names;
        }

    }
}
