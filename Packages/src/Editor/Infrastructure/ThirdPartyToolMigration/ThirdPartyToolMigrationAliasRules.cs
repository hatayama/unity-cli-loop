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
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTypeReplacementRules;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    internal static class ThirdPartyToolMigrationAliasRules
    {
        internal static string[] GetLegacyNamespaceAliases(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return GetRegexGroupValuesInCode(source, LegacyNamespaceAliasRegex, "alias");
        }

        internal static string[] GetLegacyToolInfoTypeAliases(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return GetRegexGroupValuesInCode(source, LegacyToolInfoTypeAliasRegex, "alias");
        }

        internal static string[] GetCurrentFirstPartyToolsNamespaceAliases(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return GetRegexGroupValuesInCode(source, CurrentFirstPartyToolsNamespaceAliasRegex, "alias");
        }

        internal static string[] GetCurrentApplicationNamespaceAliases(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return GetRegexGroupValuesInCode(source, CurrentApplicationNamespaceAliasRegex, "alias");
        }

        internal static string[] GetCurrentDomainNamespaceAliases(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return GetRegexGroupValuesInCode(source, CurrentDomainNamespaceAliasRegex, "alias");
        }

        internal static string[] GetCombinedLegacyNamespaceAliases(
            string source,
            string[] legacyAssemblyAliases)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(legacyAssemblyAliases != null, "legacyAssemblyAliases must not be null");

            return GetLegacyNamespaceAliases(source)
                .Concat(legacyAssemblyAliases)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        internal static string[] GetCombinedLegacyToolInfoTypeAliases(
            string source,
            string[] legacyAssemblyToolInfoAliases)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(legacyAssemblyToolInfoAliases != null, "legacyAssemblyToolInfoAliases must not be null");

            return GetLegacyToolInfoTypeAliases(source)
                .Concat(legacyAssemblyToolInfoAliases)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        internal static string[] GetCombinedCurrentApplicationNamespaceAliases(
            string source,
            string[] currentApplicationAssemblyAliases)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(
                currentApplicationAssemblyAliases != null,
                "currentApplicationAssemblyAliases must not be null");

            return GetCurrentApplicationNamespaceAliases(source)
                .Concat(currentApplicationAssemblyAliases)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        internal static string[] GetCombinedCurrentDomainNamespaceAliases(
            string source,
            string[] currentDomainAssemblyAliases)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(
                currentDomainAssemblyAliases != null,
                "currentDomainAssemblyAliases must not be null");

            return GetCurrentDomainNamespaceAliases(source)
                .Concat(currentDomainAssemblyAliases)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        internal static string[] GetCombinedCurrentFirstPartyToolsNamespaceAliases(
            string source,
            string[] currentFirstPartyToolsAssemblyAliases)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(
                currentFirstPartyToolsAssemblyAliases != null,
                "currentFirstPartyToolsAssemblyAliases must not be null");

            return GetCurrentFirstPartyToolsNamespaceAliases(source)
                .Concat(currentFirstPartyToolsAssemblyAliases)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        internal static string[] GetRegexGroupValuesInCode(string source, Regex regex, string groupName)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(regex != null, "regex must not be null");
            Debug.Assert(!string.IsNullOrEmpty(groupName), "groupName must not be null or empty");

            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            List<string> values = new();
            MatchCollection matches = regex.Matches(source);
            foreach (Match match in matches)
            {
                if (!codeTextMask.IsCodeAt(match.Index))
                {
                    continue;
                }

                values.Add(match.Groups[groupName].Value);
            }

            return values.ToArray();
        }
    }
}
