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
    internal static class ThirdPartyToolMigrationScreenshotRules
    {
        internal static (string Content, int ReplacementCount) ReplaceLegacyEditorWindowCaptureUtilityCallsInCode(
            string source,
            string[] legacyNamespaceAliases,
            string[] currentFirstPartyToolsNamespaceAliases,
            bool canMigrateBareLegacyEditorWindowCaptureUtility,
            bool shouldQualifyBareEditorWindowCaptureUtilityTimeout,
            bool canUseBareCurrentFirstPartyTools,
            string[] assemblyDeclaredTypeNames)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(legacyNamespaceAliases != null, "legacyNamespaceAliases must not be null");
            Debug.Assert(
                currentFirstPartyToolsNamespaceAliases != null,
                "currentFirstPartyToolsNamespaceAliases must not be null");
            Debug.Assert(assemblyDeclaredTypeNames != null, "assemblyDeclaredTypeNames must not be null");

            (string captureWindowContent, int captureWindowReplacementCount) =
                ReplaceLegacyEditorWindowCaptureUtilityCaptureWindowCallsInCode(
                    source,
                    legacyNamespaceAliases,
                    currentFirstPartyToolsNamespaceAliases,
                    canMigrateBareLegacyEditorWindowCaptureUtility,
                    shouldQualifyBareEditorWindowCaptureUtilityTimeout,
                    HasProtectedEditorWindowCaptureUtilityDeclaration(source, assemblyDeclaredTypeNames));
            (string captureWindowTaskContent, int captureWindowTaskReplacementCount) =
                ReplaceLegacyEditorWindowCaptureUtilityCaptureWindowTaskCallsInCode(
                    captureWindowContent,
                    legacyNamespaceAliases,
                    currentFirstPartyToolsNamespaceAliases,
                    canMigrateBareLegacyEditorWindowCaptureUtility,
                    shouldQualifyBareEditorWindowCaptureUtilityTimeout,
                    HasProtectedEditorWindowCaptureUtilityDeclaration(captureWindowContent, assemblyDeclaredTypeNames));
            (string captureGameRenderingContent, int captureGameRenderingReplacementCount) =
                ReplaceLegacyEditorWindowCaptureUtilityCaptureGameRenderingCallsInCode(
                    captureWindowTaskContent,
                    legacyNamespaceAliases,
                    currentFirstPartyToolsNamespaceAliases,
                    canMigrateBareLegacyEditorWindowCaptureUtility,
                    shouldQualifyBareEditorWindowCaptureUtilityTimeout,
                    HasProtectedEditorWindowCaptureUtilityDeclaration(
                        captureWindowTaskContent,
                        assemblyDeclaredTypeNames));
            (string captureGameRenderingTaskContent, int captureGameRenderingTaskReplacementCount) =
                ReplaceLegacyEditorWindowCaptureUtilityCaptureGameRenderingTaskCallsInCode(
                    captureGameRenderingContent,
                    legacyNamespaceAliases,
                    currentFirstPartyToolsNamespaceAliases,
                    canMigrateBareLegacyEditorWindowCaptureUtility,
                    shouldQualifyBareEditorWindowCaptureUtilityTimeout,
                    HasProtectedEditorWindowCaptureUtilityDeclaration(
                        captureGameRenderingContent,
                        assemblyDeclaredTypeNames));
            int deconstructionReplacementCount = 0;
            string migratedContent = AddDiscardToCaptureGameRenderingDeconstructionsInCode(
                captureGameRenderingTaskContent,
                canUseBareCurrentFirstPartyTools,
                currentFirstPartyToolsNamespaceAliases,
                assemblyDeclaredTypeNames,
                ref deconstructionReplacementCount);
            return (
                migratedContent,
                captureWindowReplacementCount +
                captureWindowTaskReplacementCount +
                captureGameRenderingReplacementCount +
                captureGameRenderingTaskReplacementCount +
                deconstructionReplacementCount);
        }

        internal static (string Content, int ReplacementCount)
            ReplaceLegacyEditorWindowCaptureUtilityCaptureWindowCallsInCode(
                string source,
                string[] legacyNamespaceAliases,
                string[] currentFirstPartyToolsNamespaceAliases,
                bool canMigrateBareLegacyEditorWindowCaptureUtility,
                bool shouldQualifyBareEditorWindowCaptureUtilityTimeout,
                bool hasProtectedBareEditorWindowCaptureUtility)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(legacyNamespaceAliases != null, "legacyNamespaceAliases must not be null");
            Debug.Assert(
                currentFirstPartyToolsNamespaceAliases != null,
                "currentFirstPartyToolsNamespaceAliases must not be null");

            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            MatchCollection matches = LegacyEditorWindowCaptureUtilityCaptureWindowRegex.Matches(source);
            StringBuilder builder = new(source.Length);
            int sourceCopyIndex = 0;
            int replacementCount = 0;
            foreach (Match match in matches)
            {
                if (match.Index < sourceCopyIndex ||
                    !codeTextMask.IsCodeAt(match.Index) ||
                    !IsLegacyEditorWindowCaptureUtilityCallMatch(
                        match,
                        legacyNamespaceAliases,
                        currentFirstPartyToolsNamespaceAliases,
                        canMigrateBareLegacyEditorWindowCaptureUtility,
                        hasProtectedBareEditorWindowCaptureUtility))
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
                string[] migratedArguments = GetMigratedEditorWindowCaptureUtilityArguments(
                    SplitAttributeArguments(argumentsSource),
                    GetMigratedEditorWindowCaptureUtilityTimeoutExpression(
                        match,
                        shouldQualifyBareEditorWindowCaptureUtilityTimeout,
                        currentFirstPartyToolsNamespaceAliases));
                if (migratedArguments.Length == 0)
                {
                    continue;
                }

                (string configureAwaitSuffix, int replacementEndIndex) =
                    ReadOptionalConfigureAwaitSuffix(source, codeTextMask, closingParenthesisIndex + 1);
                bool shouldExtractTexture = ShouldExtractCaptureWindowTexture(
                    source,
                    codeTextMask,
                    match.Index,
                    replacementEndIndex);

                builder.Append(source, sourceCopyIndex, match.Index - sourceCopyIndex);
                builder.Append(shouldExtractTexture ? "(await " : "await ");
                builder.Append(CurrentNamespace);
                builder.Append('.');
                builder.Append(LegacyEditorWindowCaptureUtilityTypeName);
                builder.Append('.');
                builder.Append(EditorWindowCaptureUtilityCaptureWindowMethodName);
                builder.Append('(');
                builder.Append(string.Join(", ", migratedArguments));
                builder.Append(')');
                builder.Append(configureAwaitSuffix);
                if (shouldExtractTexture)
                {
                    builder.Append(").texture");
                }

                sourceCopyIndex = replacementEndIndex;
                replacementCount++;
            }

            if (replacementCount == 0)
            {
                return (source, 0);
            }

            builder.Append(source, sourceCopyIndex, source.Length - sourceCopyIndex);
            return (builder.ToString(), replacementCount);
        }

        internal static (string Content, int ReplacementCount)
            ReplaceLegacyEditorWindowCaptureUtilityCaptureWindowTaskCallsInCode(
                string source,
                string[] legacyNamespaceAliases,
                string[] currentFirstPartyToolsNamespaceAliases,
                bool canMigrateBareLegacyEditorWindowCaptureUtility,
                bool shouldQualifyBareEditorWindowCaptureUtilityTimeout,
                bool hasProtectedBareEditorWindowCaptureUtility)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(legacyNamespaceAliases != null, "legacyNamespaceAliases must not be null");
            Debug.Assert(
                currentFirstPartyToolsNamespaceAliases != null,
                "currentFirstPartyToolsNamespaceAliases must not be null");

            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            MatchCollection matches = LegacyEditorWindowCaptureUtilityCaptureWindowInvocationRegex.Matches(source);
            StringBuilder builder = new(source.Length);
            int sourceCopyIndex = 0;
            int replacementCount = 0;
            foreach (Match match in matches)
            {
                if (match.Index < sourceCopyIndex ||
                    !codeTextMask.IsCodeAt(match.Index) ||
                    PreviousCodeTokenEquals(source, match.Index, "await") ||
                    !IsLegacyEditorWindowCaptureUtilityCallMatch(
                        match,
                        legacyNamespaceAliases,
                        currentFirstPartyToolsNamespaceAliases,
                        canMigrateBareLegacyEditorWindowCaptureUtility,
                        hasProtectedBareEditorWindowCaptureUtility))
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
                string[] migratedArguments = GetMigratedEditorWindowCaptureUtilityArguments(
                    SplitAttributeArguments(argumentsSource),
                    GetMigratedEditorWindowCaptureUtilityTimeoutExpression(
                        match,
                        shouldQualifyBareEditorWindowCaptureUtilityTimeout,
                        currentFirstPartyToolsNamespaceAliases));
                if (migratedArguments.Length == 0)
                {
                    continue;
                }

                builder.Append(source, sourceCopyIndex, match.Index - sourceCopyIndex);
                builder.Append(CurrentNamespace);
                builder.Append('.');
                builder.Append(LegacyEditorWindowCaptureUtilityTypeName);
                builder.Append('.');
                builder.Append(EditorWindowCaptureUtilityCaptureWindowMethodName);
                builder.Append('(');
                builder.Append(string.Join(", ", migratedArguments));
                builder.Append(").ContinueWith(__unityCliLoopCaptureTask => ");
                builder.Append("__unityCliLoopCaptureTask.GetAwaiter().GetResult().texture)");
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

        internal static (string Content, int ReplacementCount)
            ReplaceLegacyEditorWindowCaptureUtilityCaptureGameRenderingCallsInCode(
                string source,
                string[] legacyNamespaceAliases,
                string[] currentFirstPartyToolsNamespaceAliases,
                bool canMigrateBareLegacyEditorWindowCaptureUtility,
                bool shouldQualifyBareEditorWindowCaptureUtilityTimeout,
                bool hasProtectedBareEditorWindowCaptureUtility)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(legacyNamespaceAliases != null, "legacyNamespaceAliases must not be null");
            Debug.Assert(
                currentFirstPartyToolsNamespaceAliases != null,
                "currentFirstPartyToolsNamespaceAliases must not be null");

            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            MatchCollection matches = LegacyEditorWindowCaptureUtilityCaptureGameRenderingRegex.Matches(source);
            StringBuilder builder = new(source.Length);
            int sourceCopyIndex = 0;
            int replacementCount = 0;
            foreach (Match match in matches)
            {
                if (match.Index < sourceCopyIndex ||
                    !codeTextMask.IsCodeAt(match.Index) ||
                    !IsLegacyEditorWindowCaptureUtilityCallMatch(
                        match,
                        legacyNamespaceAliases,
                        currentFirstPartyToolsNamespaceAliases,
                        canMigrateBareLegacyEditorWindowCaptureUtility,
                        hasProtectedBareEditorWindowCaptureUtility))
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
                string[] migratedArguments = GetMigratedEditorWindowCaptureUtilityGameRenderingArguments(
                    SplitAttributeArguments(argumentsSource),
                    GetMigratedEditorWindowCaptureUtilityTimeoutExpression(
                        match,
                        shouldQualifyBareEditorWindowCaptureUtilityTimeout,
                        currentFirstPartyToolsNamespaceAliases));
                if (migratedArguments.Length == 0)
                {
                    continue;
                }

                builder.Append(source, sourceCopyIndex, match.Index - sourceCopyIndex);
                builder.Append("await ");
                builder.Append(CurrentNamespace);
                builder.Append('.');
                builder.Append(LegacyEditorWindowCaptureUtilityTypeName);
                builder.Append('.');
                builder.Append(EditorWindowCaptureUtilityCaptureGameRenderingMethodName);
                builder.Append('(');
                builder.Append(string.Join(", ", migratedArguments));
                builder.Append(')');
                AppendCaptureGameRenderingLegacyTupleProjection(builder);
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

        internal static (string Content, int ReplacementCount)
            ReplaceLegacyEditorWindowCaptureUtilityCaptureGameRenderingTaskCallsInCode(
                string source,
                string[] legacyNamespaceAliases,
                string[] currentFirstPartyToolsNamespaceAliases,
                bool canMigrateBareLegacyEditorWindowCaptureUtility,
                bool shouldQualifyBareEditorWindowCaptureUtilityTimeout,
                bool hasProtectedBareEditorWindowCaptureUtility)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(legacyNamespaceAliases != null, "legacyNamespaceAliases must not be null");
            Debug.Assert(
                currentFirstPartyToolsNamespaceAliases != null,
                "currentFirstPartyToolsNamespaceAliases must not be null");

            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            MatchCollection matches = LegacyEditorWindowCaptureUtilityCaptureGameRenderingInvocationRegex.Matches(source);
            StringBuilder builder = new(source.Length);
            int sourceCopyIndex = 0;
            int replacementCount = 0;
            foreach (Match match in matches)
            {
                if (match.Index < sourceCopyIndex ||
                    !codeTextMask.IsCodeAt(match.Index) ||
                    PreviousCodeTokenEquals(source, match.Index, "await") ||
                    !IsLegacyEditorWindowCaptureUtilityCallMatch(
                        match,
                        legacyNamespaceAliases,
                        currentFirstPartyToolsNamespaceAliases,
                        canMigrateBareLegacyEditorWindowCaptureUtility,
                        hasProtectedBareEditorWindowCaptureUtility))
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
                string[] migratedArguments = GetMigratedEditorWindowCaptureUtilityGameRenderingArguments(
                    SplitAttributeArguments(argumentsSource),
                    GetMigratedEditorWindowCaptureUtilityTimeoutExpression(
                        match,
                        shouldQualifyBareEditorWindowCaptureUtilityTimeout,
                        currentFirstPartyToolsNamespaceAliases));
                if (migratedArguments.Length == 0)
                {
                    continue;
                }

                builder.Append(source, sourceCopyIndex, match.Index - sourceCopyIndex);
                builder.Append(CurrentNamespace);
                builder.Append('.');
                builder.Append(LegacyEditorWindowCaptureUtilityTypeName);
                builder.Append('.');
                builder.Append(EditorWindowCaptureUtilityCaptureGameRenderingMethodName);
                builder.Append('(');
                builder.Append(string.Join(", ", migratedArguments));
                builder.Append(')');
                AppendCaptureGameRenderingLegacyTupleProjection(builder);
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

    }
}
