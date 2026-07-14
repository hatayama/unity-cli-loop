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
    /// <summary>
    /// Rewrites legacy EditorWindowCaptureUtility CaptureWindow call sites during migration.
    /// </summary>
    public static class ThirdPartyToolMigrationScreenshotCaptureWindowRewriteRules
    {
        public static (string Content, int ReplacementCount)
            ReplaceLegacyEditorWindowCaptureUtilityCaptureWindowCallsInCode(
                string source,
                string[] legacyNamespaceAliases,
                string[] currentFirstPartyToolsNamespaceAliases,
                bool canMigrateBareLegacyEditorWindowCaptureUtility,
                bool shouldQualifyBareEditorWindowCaptureUtilityTimeout,
                bool canPreserveBareCurrentToolContractsReferences,
                bool canPreserveBareCurrentFirstPartyToolsReferences,
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
                AppendMigratedEditorWindowCaptureUtilityReference(
                    builder,
                    match,
                    currentFirstPartyToolsNamespaceAliases,
                    canPreserveBareCurrentToolContractsReferences,
                    canPreserveBareCurrentFirstPartyToolsReferences);
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

        public static (string Content, int ReplacementCount)
            ReplaceLegacyEditorWindowCaptureUtilityCaptureWindowTaskCallsInCode(
                string source,
                string[] legacyNamespaceAliases,
                string[] currentFirstPartyToolsNamespaceAliases,
                bool canMigrateBareLegacyEditorWindowCaptureUtility,
                bool shouldQualifyBareEditorWindowCaptureUtilityTimeout,
                bool canPreserveBareCurrentToolContractsReferences,
                bool canPreserveBareCurrentFirstPartyToolsReferences,
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
                AppendMigratedEditorWindowCaptureUtilityReference(
                    builder,
                    match,
                    currentFirstPartyToolsNamespaceAliases,
                    canPreserveBareCurrentToolContractsReferences,
                    canPreserveBareCurrentFirstPartyToolsReferences);
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

        internal static void AppendMigratedEditorWindowCaptureUtilityReference(
            StringBuilder builder,
            Match match,
            string[] currentFirstPartyToolsNamespaceAliases,
            bool canPreserveBareCurrentToolContractsReferences,
            bool canPreserveBareCurrentFirstPartyToolsReferences)
        {
            Debug.Assert(builder != null, "builder must not be null");
            Debug.Assert(match != null, "match must not be null");
            Debug.Assert(
                currentFirstPartyToolsNamespaceAliases != null,
                "currentFirstPartyToolsNamespaceAliases must not be null");

            if (match.Groups["currentQualifier"].Success)
            {
                string qualifier = match.Groups["currentQualifier"].Value;
                if (IsCurrentFirstPartyToolsQualifier(qualifier))
                {
                    builder.Append(qualifier);
                    builder.Append(LegacyEditorWindowCaptureUtilityTypeName);
                    return;
                }
            }

            if (match.Groups["alias"].Success &&
                currentFirstPartyToolsNamespaceAliases.Contains(match.Groups["alias"].Value))
            {
                builder.Append(match.Groups["alias"].Value);
                builder.Append('.');
                builder.Append(LegacyEditorWindowCaptureUtilityTypeName);
                return;
            }

            if (match.Groups["editorWindowCaptureUtility"].Success &&
                (canPreserveBareCurrentToolContractsReferences ||
                    canPreserveBareCurrentFirstPartyToolsReferences))
            {
                builder.Append(LegacyEditorWindowCaptureUtilityTypeName);
                return;
            }

            builder.Append(CurrentNamespace);
            builder.Append('.');
            builder.Append(LegacyEditorWindowCaptureUtilityTypeName);
        }

        internal static bool IsCurrentFirstPartyToolsQualifier(string qualifier)
        {
            Debug.Assert(qualifier != null, "qualifier must not be null");

            string normalizedQualifier = qualifier.StartsWith("global::", StringComparison.Ordinal)
                ? qualifier.Substring("global::".Length)
                : qualifier;
            return string.Equals(
                normalizedQualifier,
                CurrentFirstPartyToolsNamespace + ".",
                StringComparison.Ordinal);
        }

    }
}
