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
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationScreenshotArgumentRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationScreenshotDeconstructionRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationScreenshotDetectionRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationScreenshotRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingArgumentRules;
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
    public static class ThirdPartyToolMigrationTimingCallerRules
    {
        public static ThirdPartyToolMigrationContentResult RemoveLegacyPlayerLoopTimingCallerArgumentsForLegacyAssembly(
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

        public static ThirdPartyToolMigrationContentResult RemoveLegacyPlayerLoopTimingParametersForLegacyAssembly(
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

        public static (string Content, int ReplacementCount) RemoveLegacyPlayerLoopTimingCallerArgumentsInCode(
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

        public static (string Content, int ReplacementCount)
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
