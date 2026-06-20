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
    internal static class ThirdPartyToolMigrationMetadataConstructorRules
    {
        internal static string ReplaceLegacyToolInfoConstructorsInCode(
            string source,
            string[] legacyNamespaceAliases,
            bool canMigrateBareLegacyToolInfoConstructor,
            bool canMigrateAmbiguousBareLegacyToolInfoConstructor,
            string[] legacyAssemblyToolInfoAliases,
            ref int replacementCount)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(legacyNamespaceAliases != null, "legacyNamespaceAliases must not be null");
            Debug.Assert(legacyAssemblyToolInfoAliases != null, "legacyAssemblyToolInfoAliases must not be null");

            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            string[] legacyToolInfoTypeAliases =
                GetCombinedLegacyToolInfoTypeAliases(source, legacyAssemblyToolInfoAliases);
            MatchCollection matches = LegacyToolInfoConstructorRegex.Matches(source);
            StringBuilder builder = new(source.Length);
            int sourceCopyIndex = 0;
            int localReplacementCount = 0;
            foreach (Match match in matches)
            {
                if (match.Index < sourceCopyIndex ||
                    !codeTextMask.IsCodeAt(match.Index) ||
                    !IsLegacyToolInfoConstructorMatch(
                        match,
                        legacyNamespaceAliases,
                        legacyToolInfoTypeAliases,
                        canMigrateBareLegacyToolInfoConstructor))
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
                if (!ShouldMigrateLegacyToolInfoConstructorArguments(
                        match,
                        arguments,
                        canMigrateAmbiguousBareLegacyToolInfoConstructor))
                {
                    continue;
                }

                string[] migratedArguments = GetMigratedToolInfoConstructorArguments(arguments);
                if (migratedArguments.Length == arguments.Length)
                {
                    continue;
                }

                builder.Append(source, sourceCopyIndex, match.Index - sourceCopyIndex);
                builder.Append($"new {CurrentNamespace}.ToolInfo(");
                builder.Append(string.Join(", ", migratedArguments));
                builder.Append(')');
                sourceCopyIndex = closingParenthesisIndex + 1;
                localReplacementCount++;
            }

            if (localReplacementCount == 0)
            {
                return source;
            }

            builder.Append(source, sourceCopyIndex, source.Length - sourceCopyIndex);
            replacementCount += localReplacementCount;
            return builder.ToString();
        }

        internal static string ReplaceLegacyToolSettingsCatalogItemConstructorsInCode(
            string source,
            string[] legacyNamespaceAliases,
            bool canMigrateBareLegacyConstructor,
            ref int replacementCount)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(legacyNamespaceAliases != null, "legacyNamespaceAliases must not be null");

            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            MatchCollection matches = LegacyToolSettingsCatalogItemConstructorRegex.Matches(source);
            StringBuilder builder = new(source.Length);
            int sourceCopyIndex = 0;
            int localReplacementCount = 0;
            foreach (Match match in matches)
            {
                if (match.Index < sourceCopyIndex ||
                    !codeTextMask.IsCodeAt(match.Index) ||
                    !IsLegacyToolSettingsCatalogItemConstructorMatch(
                        match,
                        legacyNamespaceAliases,
                        canMigrateBareLegacyConstructor))
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
                string[] migratedArguments = GetMigratedToolSettingsCatalogItemConstructorArguments(arguments);
                if (migratedArguments.Length == arguments.Length)
                {
                    continue;
                }

                builder.Append(source, sourceCopyIndex, match.Index - sourceCopyIndex);
                builder.Append($"new {CurrentNamespace}.ToolSettingsCatalogItem(");
                builder.Append(string.Join(", ", migratedArguments));
                builder.Append(')');
                sourceCopyIndex = closingParenthesisIndex + 1;
                localReplacementCount++;
            }

            if (localReplacementCount == 0)
            {
                return source;
            }

            builder.Append(source, sourceCopyIndex, source.Length - sourceCopyIndex);
            replacementCount += localReplacementCount;
            return builder.ToString();
        }

        internal static bool IsLegacyToolSettingsCatalogItemConstructorMatch(
            Match match,
            string[] legacyNamespaceAliases,
            bool canMigrateBareLegacyConstructor)
        {
            Debug.Assert(match != null, "match must not be null");
            Debug.Assert(legacyNamespaceAliases != null, "legacyNamespaceAliases must not be null");

            if (match.Groups["qualifier"].Success)
            {
                return true;
            }

            if (match.Groups["alias"].Success)
            {
                return legacyNamespaceAliases.Contains(match.Groups["alias"].Value);
            }

            return match.Groups["toolSettingsCatalogItem"].Success && canMigrateBareLegacyConstructor;
        }
    }
}
