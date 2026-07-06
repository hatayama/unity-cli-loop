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

namespace io.github.hatayama.UnityCliLoop.Domain
{
    public static class ThirdPartyToolMigrationMetadataConstructorRules
    {
        public static string ReplaceLegacyToolInfoConstructorsInCode(
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

        public static string ReplaceLegacyToolSettingsCatalogItemConstructorsInCode(
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

        public static bool IsLegacyToolSettingsCatalogItemConstructorMatch(
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
