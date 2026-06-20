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
    internal static class ThirdPartyToolMigrationScreenshotDeconstructionRules
    {
        internal static void AppendCaptureGameRenderingLegacyTupleProjection(StringBuilder builder)
        {
            Debug.Assert(builder != null, "builder must not be null");

            builder.Append(".ContinueWith(");
            builder.Append(CaptureGameRenderingProjectionTaskVariableName);
            builder.Append(" => (");
            builder.Append(CaptureGameRenderingProjectionTaskVariableName);
            builder.Append(".GetAwaiter().GetResult().texture, ");
            builder.Append(CaptureGameRenderingProjectionTaskVariableName);
            builder.Append(".GetAwaiter().GetResult().yOffset))");
        }

        internal static string AddDiscardToCaptureGameRenderingDeconstructionsInCode(
            string source,
            bool canUseBareCurrentFirstPartyTools,
            string[] currentFirstPartyToolsNamespaceAliases,
            string[] assemblyDeclaredTypeNames,
            ref int replacementCount)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(
                currentFirstPartyToolsNamespaceAliases != null,
                "currentFirstPartyToolsNamespaceAliases must not be null");
            Debug.Assert(assemblyDeclaredTypeNames != null, "assemblyDeclaredTypeNames must not be null");

            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            MatchCollection matches = CurrentCaptureGameRenderingDeconstructionRegex.Matches(source);
            StringBuilder builder = new(source.Length);
            int sourceCopyIndex = 0;
            int localReplacementCount = 0;
            foreach (Match match in matches)
            {
                if (match.Index < sourceCopyIndex || !codeTextMask.IsCodeAt(match.Index))
                {
                    continue;
                }

                if (!IsCurrentFirstPartyCaptureGameRenderingDeconstruction(
                        source,
                        match,
                        canUseBareCurrentFirstPartyTools,
                        currentFirstPartyToolsNamespaceAliases,
                        assemblyDeclaredTypeNames))
                {
                    continue;
                }

                int openParenthesisIndex = match.Index + match.Length - 1;
                int closingParenthesisIndex = FindInvocationClosingParenthesisIndex(
                    source,
                    codeTextMask,
                    openParenthesisIndex);
                if (closingParenthesisIndex >= 0 &&
                    IsCaptureGameRenderingLegacyTupleProjection(source, closingParenthesisIndex + 1))
                {
                    continue;
                }

                Group itemsGroup = match.Groups["items"];
                string[] items = SplitAttributeArguments(itemsGroup.Value)
                    .Select(item => item.Trim())
                    .Where(item => item.Length > 0)
                    .ToArray();
                if (items.Length != 2)
                {
                    continue;
                }

                builder.Append(source, sourceCopyIndex, itemsGroup.Index - sourceCopyIndex);
                builder.Append(itemsGroup.Value);
                builder.Append(", _");
                sourceCopyIndex = itemsGroup.Index + itemsGroup.Length;
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

        internal static bool ContainsCurrentCaptureGameRenderingDeconstructionMigration(
            string source,
            bool canUseBareCurrentFirstPartyTools,
            string[] currentFirstPartyToolsNamespaceAliases,
            string[] assemblyDeclaredTypeNames)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(
                currentFirstPartyToolsNamespaceAliases != null,
                "currentFirstPartyToolsNamespaceAliases must not be null");
            Debug.Assert(assemblyDeclaredTypeNames != null, "assemblyDeclaredTypeNames must not be null");

            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            MatchCollection matches = CurrentCaptureGameRenderingDeconstructionRegex.Matches(source);
            foreach (Match match in matches)
            {
                if (!codeTextMask.IsCodeAt(match.Index))
                {
                    continue;
                }

                if (!IsCurrentFirstPartyCaptureGameRenderingDeconstruction(
                        source,
                        match,
                        canUseBareCurrentFirstPartyTools,
                        currentFirstPartyToolsNamespaceAliases,
                        assemblyDeclaredTypeNames))
                {
                    continue;
                }

                int openParenthesisIndex = match.Index + match.Length - 1;
                int closingParenthesisIndex = FindInvocationClosingParenthesisIndex(
                    source,
                    codeTextMask,
                    openParenthesisIndex);
                if (closingParenthesisIndex >= 0 &&
                    IsCaptureGameRenderingLegacyTupleProjection(source, closingParenthesisIndex + 1))
                {
                    continue;
                }

                Group itemsGroup = match.Groups["items"];
                string[] items = SplitAttributeArguments(itemsGroup.Value)
                    .Select(item => item.Trim())
                    .Where(item => item.Length > 0)
                    .ToArray();
                if (items.Length == 2)
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool IsCurrentFirstPartyCaptureGameRenderingDeconstruction(
            string source,
            Match match,
            bool canUseBareCurrentFirstPartyTools,
            string[] currentFirstPartyToolsNamespaceAliases,
            string[] assemblyDeclaredTypeNames)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(match != null, "match must not be null");
            Debug.Assert(
                currentFirstPartyToolsNamespaceAliases != null,
                "currentFirstPartyToolsNamespaceAliases must not be null");
            Debug.Assert(assemblyDeclaredTypeNames != null, "assemblyDeclaredTypeNames must not be null");

            if (match.Groups["qualifier"].Success)
            {
                return true;
            }

            if (match.Groups["alias"].Success)
            {
                return currentFirstPartyToolsNamespaceAliases.Contains(match.Groups["alias"].Value);
            }

            bool hasProtectedTypeDeclaration =
                DeclaresLocalType(source, LegacyEditorWindowCaptureUtilityTypeName) ||
                assemblyDeclaredTypeNames.Contains(LegacyEditorWindowCaptureUtilityTypeName);
            return canUseBareCurrentFirstPartyTools && !hasProtectedTypeDeclaration;
        }

        internal static bool IsCaptureGameRenderingLegacyTupleProjection(string source, int startIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(startIndex >= 0, "startIndex must not be negative");

            int nextCodeIndex = ReadNextNonWhitespaceIndex(source, startIndex);
            return nextCodeIndex < source.Length &&
                StartsWith(source, nextCodeIndex, ".ContinueWith");
        }

        internal static bool IsLegacyEditorWindowCaptureUtilityCallMatch(
            Match match,
            string[] legacyNamespaceAliases,
            string[] currentFirstPartyToolsNamespaceAliases,
            bool canMigrateBareLegacyEditorWindowCaptureUtility,
            bool hasProtectedBareEditorWindowCaptureUtility)
        {
            Debug.Assert(match != null, "match must not be null");
            Debug.Assert(legacyNamespaceAliases != null, "legacyNamespaceAliases must not be null");
            Debug.Assert(
                currentFirstPartyToolsNamespaceAliases != null,
                "currentFirstPartyToolsNamespaceAliases must not be null");

            if (match.Groups["qualifier"].Success)
            {
                return true;
            }

            if (match.Groups["currentQualifier"].Success)
            {
                return true;
            }

            if (match.Groups["alias"].Success)
            {
                string alias = match.Groups["alias"].Value;
                return legacyNamespaceAliases.Contains(alias) ||
                    currentFirstPartyToolsNamespaceAliases.Contains(alias);
            }

            return match.Groups["editorWindowCaptureUtility"].Success &&
                canMigrateBareLegacyEditorWindowCaptureUtility &&
                !hasProtectedBareEditorWindowCaptureUtility;
        }

        internal static bool HasProtectedEditorWindowCaptureUtilityDeclaration(
            string source,
            string[] assemblyDeclaredTypeNames)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(assemblyDeclaredTypeNames != null, "assemblyDeclaredTypeNames must not be null");

            return DeclaresLocalType(source, LegacyEditorWindowCaptureUtilityTypeName) ||
                assemblyDeclaredTypeNames.Contains(LegacyEditorWindowCaptureUtilityTypeName);
        }

        internal static bool ShouldExtractCaptureWindowTexture(
            string source,
            CodeTextMask codeTextMask,
            int awaitIndex,
            int expressionEndIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(awaitIndex >= 0, "awaitIndex must not be negative");
            Debug.Assert(expressionEndIndex >= 0, "expressionEndIndex must not be negative");

            int nextCodeIndex = ReadNextNonWhitespaceIndex(source, expressionEndIndex);
            if (nextCodeIndex >= source.Length || source[nextCodeIndex] != ';' || !codeTextMask.IsCodeAt(nextCodeIndex))
            {
                return true;
            }

            if (PreviousCodeTokenEquals(source, awaitIndex, "return"))
            {
                return true;
            }

            if (PreviousCodeTokenIsArrow(source, awaitIndex))
            {
                return true;
            }

            char previousCharacter = ReadPreviousNonWhitespaceCharacter(source, awaitIndex);
            return previousCharacter == '=';
        }

        internal static (string ConfigureAwaitSuffix, int EndIndex) ReadOptionalConfigureAwaitSuffix(
            string source,
            CodeTextMask codeTextMask,
            int startIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(startIndex >= 0, "startIndex must not be negative");

            int dotIndex = startIndex;
            while (dotIndex < source.Length && char.IsWhiteSpace(source[dotIndex]))
            {
                dotIndex++;
            }

            if (dotIndex >= source.Length || source[dotIndex] != '.' || !codeTextMask.IsCodeAt(dotIndex))
            {
                return (string.Empty, startIndex);
            }

            const string configureAwaitMemberName = "ConfigureAwait";
            int memberNameIndex = dotIndex + 1;
            if (memberNameIndex + configureAwaitMemberName.Length > source.Length)
            {
                return (string.Empty, startIndex);
            }

            string memberName = source.Substring(memberNameIndex, configureAwaitMemberName.Length);
            if (!string.Equals(memberName, configureAwaitMemberName, StringComparison.Ordinal))
            {
                return (string.Empty, startIndex);
            }

            int openParenthesisIndex = memberNameIndex + configureAwaitMemberName.Length;
            while (openParenthesisIndex < source.Length && char.IsWhiteSpace(source[openParenthesisIndex]))
            {
                openParenthesisIndex++;
            }

            if (openParenthesisIndex >= source.Length || source[openParenthesisIndex] != '(')
            {
                return (string.Empty, startIndex);
            }

            int closingParenthesisIndex = FindInvocationClosingParenthesisIndex(
                source,
                codeTextMask,
                openParenthesisIndex);
            if (closingParenthesisIndex < 0)
            {
                return (string.Empty, startIndex);
            }

            return (
                source.Substring(startIndex, closingParenthesisIndex - startIndex + 1),
                closingParenthesisIndex + 1);
        }
    }
}
