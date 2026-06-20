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
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationMetadataConstructorRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationParsingRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationRegexRewriteRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationRuleCatalog;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationScreenshotArgumentRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationScreenshotDeconstructionRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationScreenshotDetectionRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationScreenshotRules;
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTimingArgumentRules;
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
    internal static class ThirdPartyToolMigrationTimingCallerRules
    {
        internal static ThirdPartyToolMigrationContentResult RemoveLegacyPlayerLoopTimingCallerArgumentsForLegacyAssembly(
            string source,
            string originalSource,
            RemovedLegacyPlayerLoopTimingSignature[] removedSignatures,
            string[] legacyAssemblyAliases)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(originalSource != null, "originalSource must not be null");
            Debug.Assert(removedSignatures != null, "removedSignatures must not be null");
            Debug.Assert(legacyAssemblyAliases != null, "legacyAssemblyAliases must not be null");

            string[] legacyNamespaceAliases = GetCombinedLegacyNamespaceAliases(
                originalSource,
                legacyAssemblyAliases);
            (string migratedContent, int replacementCount) = RemoveLegacyPlayerLoopTimingCallerArgumentsInCode(
                source,
                removedSignatures,
                legacyNamespaceAliases);
            return new ThirdPartyToolMigrationContentResult(
                migratedContent,
                replacementCount,
                Array.Empty<RemovedLegacyPlayerLoopTimingSignature>());
        }

        internal static ThirdPartyToolMigrationContentResult RemoveLegacyPlayerLoopTimingParametersForLegacyAssembly(
            string source,
            string originalSource,
            string[] legacyAssemblyAliases,
            bool canMigrateBareLegacyPlayerLoopTiming,
            string[] migratedCalleeMethodNames)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(originalSource != null, "originalSource must not be null");
            Debug.Assert(legacyAssemblyAliases != null, "legacyAssemblyAliases must not be null");
            Debug.Assert(migratedCalleeMethodNames != null, "migratedCalleeMethodNames must not be null");

            string[] legacyNamespaceAliases = GetCombinedLegacyNamespaceAliases(
                originalSource,
                legacyAssemblyAliases);
            (
                string migratedContent,
                int replacementCount,
                RemovedLegacyPlayerLoopTimingSignature[] removedSignatures) =
                RemoveLegacyPlayerLoopTimingParametersInCode(
                    source,
                    legacyNamespaceAliases,
                    canMigrateBareLegacyPlayerLoopTiming,
                    migratedCalleeMethodNames);
            return new ThirdPartyToolMigrationContentResult(
                migratedContent,
                replacementCount,
                removedSignatures);
        }

        internal static (string Content, int ReplacementCount) RemoveLegacyPlayerLoopTimingCallerArgumentsInCode(
            string source,
            RemovedLegacyPlayerLoopTimingSignature[] removedSignatures,
            string[] legacyNamespaceAliases)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(removedSignatures != null, "removedSignatures must not be null");
            Debug.Assert(legacyNamespaceAliases != null, "legacyNamespaceAliases must not be null");

            string migratedContent = source;
            int replacementCount = 0;
            foreach (RemovedLegacyPlayerLoopTimingSignature removedSignature in removedSignatures)
            {
                (string signatureMigratedContent, int signatureReplacementCount) =
                    RemoveLegacyPlayerLoopTimingCallerArgumentsForMethodInCode(
                        migratedContent,
                        removedSignature,
                        legacyNamespaceAliases);
                migratedContent = signatureMigratedContent;
                replacementCount += signatureReplacementCount;
            }

            return (migratedContent, replacementCount);
        }

        internal static (string Content, int ReplacementCount)
            RemoveLegacyPlayerLoopTimingCallerArgumentsForMethodInCode(
                string source,
                RemovedLegacyPlayerLoopTimingSignature removedSignature,
                string[] legacyNamespaceAliases)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(!string.IsNullOrEmpty(removedSignature.MethodName), "MethodName must not be null or empty");
            Debug.Assert(legacyNamespaceAliases != null, "legacyNamespaceAliases must not be null");

            Regex invocationRegex = new(
                $@"(?<![A-Za-z0-9_]){Regex.Escape(removedSignature.MethodName)}\b",
                RegexOptions.Compiled);
            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            MatchCollection matches = invocationRegex.Matches(source);
            StringBuilder builder = new(source.Length);
            int sourceCopyIndex = 0;
            int replacementCount = 0;
            foreach (Match match in matches)
            {
                if (match.Index < sourceCopyIndex || !codeTextMask.IsCodeAt(match.Index))
                {
                    continue;
                }

                int openParenthesisIndex = FindInvocationOpenParenthesisIndex(
                    source,
                    codeTextMask,
                    match.Index + match.Length);
                if (openParenthesisIndex < 0)
                {
                    continue;
                }

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
                if (!ShouldMigrateLegacyPlayerLoopTimingCaller(
                        source,
                        codeTextMask,
                        match.Index,
                        arguments,
                        removedSignature))
                {
                    continue;
                }

                (string[] migratedArguments, bool changed) =
                    RemoveLegacyPlayerLoopTimingCallerArguments(
                        arguments,
                        removedSignature.RemovedParameters,
                        legacyNamespaceAliases);
                if (!changed)
                {
                    continue;
                }

                builder.Append(source, sourceCopyIndex, openParenthesisIndex + 1 - sourceCopyIndex);
                builder.Append(string.Join(", ", migratedArguments));
                sourceCopyIndex = closingParenthesisIndex;
                replacementCount++;
            }

            if (replacementCount == 0)
            {
                return (source, 0);
            }

            builder.Append(source, sourceCopyIndex, source.Length - sourceCopyIndex);
            return (builder.ToString(), replacementCount);
        }

    }
}
