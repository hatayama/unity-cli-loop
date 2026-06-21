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
    internal static class ThirdPartyToolMigrationDetectionRules
    {
        internal static bool ContainsLegacyCSharpApi(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return ContainsLegacyToolMigrationMarker(source);
        }

        internal static bool ContainsMigrationCandidateText(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            if (ContainsTextFragment(source, LegacyNamespace) ||
                ContainsTextFragment(source, CurrentNamespace) ||
                ContainsTextFragment(source, CurrentApplicationNamespace) ||
                ContainsTextFragment(source, CurrentDomainNamespace) ||
                ContainsTextFragment(source, CurrentFirstPartyToolsNamespace) ||
                ContainsTextFragment(source, "McpTool") ||
                ContainsTextFragment(source, "CustomToolManager") ||
                ContainsTextFragment(source, LegacyEditorDelayTypeName) ||
                ContainsTextFragment(source, LegacyTimerDelayTypeName) ||
                ContainsTextFragment(source, LegacyMainThreadSwitcherTypeName) ||
                ContainsTextFragment(source, LegacyPlayerLoopTimingTypeName) ||
                ContainsTextFragment(source, LegacyEditorWindowCaptureUtilityTypeName) ||
                ContainsTextFragment(source, "UnityCliLoopToolRegistrar") ||
                ContainsTextFragment(source, "ToolInfo"))
            {
                return true;
            }

            foreach (TypeReplacementRule rule in ToolContractTypeReplacementRules)
            {
                if (ContainsTextFragment(source, rule.LegacyName) ||
                    ContainsTextFragment(source, rule.CurrentName))
                {
                    return true;
                }
            }

            foreach (TypeReplacementRule rule in DomainTypeReplacementRules)
            {
                if (ContainsTextFragment(source, rule.LegacyName) ||
                    ContainsTextFragment(source, rule.CurrentName))
                {
                    return true;
                }
            }

            foreach (TypeReplacementRule rule in ApplicationTypeReplacementRules)
            {
                if (ContainsTextFragment(source, rule.LegacyName) ||
                    ContainsTextFragment(source, rule.CurrentName))
                {
                    return true;
                }
            }

            foreach (TypeReplacementRule rule in FirstPartyScreenshotTypeReplacementRules)
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

        internal static string[] GetDeclaredTypeNames(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            HashSet<string> typeNames = new(StringComparer.Ordinal);
            MatchCollection matches = TypeDeclarationNameRegex.Matches(source);
            foreach (Match match in matches)
            {
                if (!codeTextMask.IsCodeAt(match.Index))
                {
                    continue;
                }

                typeNames.Add(match.Groups["name"].Value);
            }

            return typeNames
                .OrderBy(typeName => typeName, StringComparer.Ordinal)
                .ToArray();
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

        internal static bool RegexMatchesCode(string source, Regex regex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(regex != null, "regex must not be null");

            MatchCollection matches = regex.Matches(source);
            if (matches.Count == 0)
            {
                return false;
            }

            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            foreach (Match match in matches)
            {
                if (codeTextMask.IsCodeAt(match.Index))
                {
                    return true;
                }
            }

            return false;
        }

    }
}
