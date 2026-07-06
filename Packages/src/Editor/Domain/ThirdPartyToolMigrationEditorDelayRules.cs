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

namespace io.github.hatayama.UnityCliLoop.Domain
{
    public static class ThirdPartyToolMigrationEditorDelayRules
    {
        public static (string Content, int ReplacementCount) ReplaceLegacyEditorDelayFrameCallsInCode(
            string source,
            string[] legacyNamespaceAliases,
            bool canMigrateBareLegacyEditorDelay,
            bool shouldQualifyBareToolContractsReferences)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(legacyNamespaceAliases != null, "legacyNamespaceAliases must not be null");

            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            MatchCollection matches = LegacyEditorDelayFrameRegex.Matches(source);
            StringBuilder builder = new(source.Length);
            int sourceCopyIndex = 0;
            int replacementCount = 0;
            foreach (Match match in matches)
            {
                if (match.Index < sourceCopyIndex ||
                    !codeTextMask.IsCodeAt(match.Index) ||
                    !IsLegacyEditorDelayFrameCallMatch(
                        match,
                        legacyNamespaceAliases,
                        canMigrateBareLegacyEditorDelay))
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
                string[] migratedArguments = GetMigratedEditorDelayFrameArguments(
                    arguments,
                    GetMigratedEditorFrameWaitTimeoutExpression(
                        match,
                        shouldQualifyBareToolContractsReferences,
                        Array.Empty<string>()));
                if (migratedArguments.Length == 0)
                {
                    continue;
                }

                builder.Append(source, sourceCopyIndex, match.Index - sourceCopyIndex);
                builder.Append(GetMigratedEditorFrameWaiterInvocationTarget(
                    match,
                    shouldQualifyBareToolContractsReferences));
                builder.Append('.');
                builder.Append(CurrentEditorFrameWaiterMethodName);
                builder.Append('(');
                builder.Append(string.Join(", ", migratedArguments));
                builder.Append(')');
                sourceCopyIndex = closingParenthesisIndex + 1;
                replacementCount++;
            }

            if (replacementCount == 0)
            {
                return (source, 0);
            }

            builder.Append(source, sourceCopyIndex, source.Length - sourceCopyIndex);
            return (builder.ToString(), replacementCount);
        }

        public static bool IsLegacyEditorDelayFrameCallMatch(
            Match match,
            string[] legacyNamespaceAliases,
            bool canMigrateBareLegacyEditorDelay)
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

            return match.Groups["editorDelay"].Success && canMigrateBareLegacyEditorDelay;
        }

        public static string GetMigratedEditorFrameWaiterInvocationTarget(
            Match match,
            bool shouldQualifyBareToolContractsReferences)
        {
            Debug.Assert(match != null, "match must not be null");

            if (match.Groups["qualifier"].Success)
            {
                return $"{CurrentNamespace}.{CurrentEditorFrameWaiterTypeName}";
            }

            if (match.Groups["alias"].Success)
            {
                return $"{match.Groups["alias"].Value}.{CurrentEditorFrameWaiterTypeName}";
            }

            if (shouldQualifyBareToolContractsReferences)
            {
                return $"{CurrentNamespace}.{CurrentEditorFrameWaiterTypeName}";
            }

            return CurrentEditorFrameWaiterTypeName;
        }

        public static string GetMigratedEditorFrameWaitTimeoutExpression(
            Match match,
            bool shouldQualifyBareToolContractsReferences,
            string[] currentFirstPartyToolsNamespaceAliases)
        {
            Debug.Assert(match != null, "match must not be null");
            Debug.Assert(
                currentFirstPartyToolsNamespaceAliases != null,
                "currentFirstPartyToolsNamespaceAliases must not be null");

            if (match.Groups["qualifier"].Success)
            {
                return $"{CurrentNamespace}.{CurrentConstantsTypeName}.{CurrentEditorFrameWaitTimeoutMemberName}";
            }

            if (match.Groups["currentQualifier"].Success)
            {
                return $"{CurrentNamespace}.{CurrentConstantsTypeName}.{CurrentEditorFrameWaitTimeoutMemberName}";
            }

            if (match.Groups["alias"].Success)
            {
                string alias = match.Groups["alias"].Value;
                if (currentFirstPartyToolsNamespaceAliases.Contains(alias))
                {
                    return $"{CurrentNamespace}.{CurrentConstantsTypeName}.{CurrentEditorFrameWaitTimeoutMemberName}";
                }

                return $"{alias}.{CurrentConstantsTypeName}.{CurrentEditorFrameWaitTimeoutMemberName}";
            }

            if (shouldQualifyBareToolContractsReferences)
            {
                return $"{CurrentNamespace}.{CurrentConstantsTypeName}.{CurrentEditorFrameWaitTimeoutMemberName}";
            }

            return $"{CurrentConstantsTypeName}.{CurrentEditorFrameWaitTimeoutMemberName}";
        }

        public static string GetMigratedEditorWindowCaptureUtilityTimeoutExpression(
            Match match,
            bool shouldQualifyBareEditorWindowCaptureUtilityTimeout,
            string[] currentFirstPartyToolsNamespaceAliases)
        {
            Debug.Assert(match != null, "match must not be null");
            Debug.Assert(
                currentFirstPartyToolsNamespaceAliases != null,
                "currentFirstPartyToolsNamespaceAliases must not be null");

            if (shouldQualifyBareEditorWindowCaptureUtilityTimeout &&
                match.Groups["editorWindowCaptureUtility"].Success)
            {
                return $"{CurrentNamespace}.{CurrentConstantsTypeName}.{CurrentEditorFrameWaitTimeoutMemberName}";
            }

            return GetMigratedEditorFrameWaitTimeoutExpression(
                match,
                shouldQualifyBareEditorWindowCaptureUtilityTimeout,
                currentFirstPartyToolsNamespaceAliases);
        }

        public static string[] GetMigratedEditorDelayFrameArguments(
            string[] arguments,
            string timeoutExpression)
        {
            Debug.Assert(arguments != null, "arguments must not be null");
            Debug.Assert(!string.IsNullOrEmpty(timeoutExpression), "timeoutExpression must not be null or empty");

            string[] trimmedArguments = arguments
                .Select(argument => argument.Trim())
                .Where(argument => argument.Length > 0)
                .ToArray();
            if (trimmedArguments.Length > 2)
            {
                return Array.Empty<string>();
            }

            string frameCountArgument = "1";
            string cancellationTokenArgument = null;
            bool hasFrameCountArgument = false;
            bool hasCancellationTokenArgument = false;
            for (int i = 0; i < trimmedArguments.Length; i++)
            {
                string argument = trimmedArguments[i];
                string namedFrameCountValue = GetNamedArgumentValueOrNull(argument, "frameCount");
                if (namedFrameCountValue != null)
                {
                    if (hasFrameCountArgument)
                    {
                        return Array.Empty<string>();
                    }

                    frameCountArgument = namedFrameCountValue;
                    hasFrameCountArgument = true;
                    continue;
                }

                string namedCancellationTokenValue = GetNamedArgumentValueOrNull(argument, "cancellationToken");
                if (namedCancellationTokenValue != null)
                {
                    if (hasCancellationTokenArgument)
                    {
                        return Array.Empty<string>();
                    }

                    cancellationTokenArgument = namedCancellationTokenValue;
                    hasCancellationTokenArgument = true;
                    continue;
                }

                if (!hasFrameCountArgument)
                {
                    frameCountArgument = argument;
                    hasFrameCountArgument = true;
                    continue;
                }

                if (!hasCancellationTokenArgument)
                {
                    cancellationTokenArgument = argument;
                    hasCancellationTokenArgument = true;
                    continue;
                }

                return Array.Empty<string>();
            }

            List<string> migratedArguments = new()
            {
                frameCountArgument,
                timeoutExpression
            };
            if (cancellationTokenArgument != null)
            {
                migratedArguments.Add(cancellationTokenArgument);
            }

            return migratedArguments.ToArray();
        }
    }
}
