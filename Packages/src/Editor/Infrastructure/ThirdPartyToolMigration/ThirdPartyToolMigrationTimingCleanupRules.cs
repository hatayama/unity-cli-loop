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
using static io.github.hatayama.UnityCliLoop.Infrastructure.ThirdPartyToolMigrationTimingCallerRules;
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
    internal static class ThirdPartyToolMigrationTimingCleanupRules
    {
        internal static bool CanRemoveLegacyPlayerLoopTimingParameterFromMethod(
            string methodBody,
            string[] migratedCalleeMethodNames)
        {
            Debug.Assert(methodBody != null, "methodBody must not be null");
            Debug.Assert(migratedCalleeMethodNames != null, "migratedCalleeMethodNames must not be null");

            if (ContainsMigratedMainThreadSwitcherSwitchCall(methodBody))
            {
                return true;
            }

            foreach (string migratedCalleeMethodName in migratedCalleeMethodNames)
            {
                if (ContainsIdentifierInCode(methodBody, migratedCalleeMethodName))
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool ContainsMigratedMainThreadSwitcherSwitchCall(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return RegexMatchesCode(source, MigratedMainThreadSwitcherSwitchRegex);
        }

        internal static (string Content, int ReplacementCount) RemoveUnusedLegacyPlayerLoopTimingDeclarationsInCode(
            string source)
        {
            Debug.Assert(source != null, "source must not be null");

            if (!ContainsTextFragment(source, LegacyPlayerLoopTimingTypeName))
            {
                return (source, 0);
            }

            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            MatchCollection matches = LegacyPlayerLoopTimingDeclarationRegex.Matches(source);
            StringBuilder builder = new(source.Length);
            int sourceCopyIndex = 0;
            int replacementCount = 0;
            foreach (Match match in matches)
            {
                if (match.Index < sourceCopyIndex || !codeTextMask.IsCodeAt(match.Index))
                {
                    continue;
                }

                int removalStartIndex = ReadLegacyPlayerLoopTimingDeclarationRemovalStart(
                    source,
                    match.Index,
                    codeTextMask);
                if (removalStartIndex < sourceCopyIndex)
                {
                    continue;
                }

                string declarationName = match.Groups["name"].Value;
                int removalLength = match.Index + match.Length - removalStartIndex;
                string sourceWithoutDeclaration = source.Remove(removalStartIndex, removalLength);
                if (ContainsIdentifierInCode(sourceWithoutDeclaration, declarationName))
                {
                    continue;
                }

                builder.Append(source, sourceCopyIndex, removalStartIndex - sourceCopyIndex);
                sourceCopyIndex = match.Index + match.Length;
                replacementCount++;
            }

            if (replacementCount == 0)
            {
                return (source, 0);
            }

            builder.Append(source, sourceCopyIndex, source.Length - sourceCopyIndex);
            return (builder.ToString(), replacementCount);
        }

        internal static int ReadLegacyPlayerLoopTimingDeclarationRemovalStart(
            string source,
            int declarationStartIndex,
            CodeTextMask codeTextMask)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(declarationStartIndex >= 0, "declarationStartIndex must not be negative");

            int scanIndex = declarationStartIndex - 1;
            int removalStartIndex = declarationStartIndex;
            while (true)
            {
                scanIndex = SkipWhitespaceBackward(source, scanIndex);
                if (scanIndex < 0 || source[scanIndex] != ']' || !codeTextMask.IsCodeAt(scanIndex))
                {
                    return removalStartIndex;
                }

                int openingBracketIndex = FindOpeningAttributeBracket(source, scanIndex, codeTextMask);
                if (openingBracketIndex < 0 || !HasOnlyWhitespaceBeforeIndexOnLine(source, openingBracketIndex))
                {
                    return removalStartIndex;
                }

                removalStartIndex = GetLineStartIndex(source, openingBracketIndex);
                scanIndex = removalStartIndex - 1;
            }
        }

        internal static int SkipWhitespaceBackward(string source, int startIndex)
        {
            Debug.Assert(source != null, "source must not be null");

            int index = startIndex;
            while (index >= 0 && char.IsWhiteSpace(source[index]))
            {
                index--;
            }

            return index;
        }

        internal static int FindOpeningAttributeBracket(
            string source,
            int closingBracketIndex,
            CodeTextMask codeTextMask)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(closingBracketIndex >= 0, "closingBracketIndex must not be negative");

            int depth = 0;
            for (int index = closingBracketIndex; index >= 0; index--)
            {
                if (!codeTextMask.IsCodeAt(index))
                {
                    continue;
                }

                char character = source[index];
                if (character == ']')
                {
                    depth++;
                    continue;
                }

                if (character != '[')
                {
                    continue;
                }

                depth--;
                if (depth == 0)
                {
                    return index;
                }
            }

            return -1;
        }

        internal static bool HasOnlyWhitespaceBeforeIndexOnLine(string source, int index)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(index >= 0, "index must not be negative");

            int lineStartIndex = GetLineStartIndex(source, index);
            for (int currentIndex = lineStartIndex; currentIndex < index; currentIndex++)
            {
                if (!char.IsWhiteSpace(source[currentIndex]))
                {
                    return false;
                }
            }

            return true;
        }

        internal static int GetLineStartIndex(string source, int index)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(index >= 0, "index must not be negative");

            int previousNewLineIndex = source.LastIndexOf('\n', index);
            return previousNewLineIndex < 0 ? 0 : previousNewLineIndex + 1;
        }

        internal static LegacyPlayerLoopTimingParameterDeclaration[] ReadLegacyPlayerLoopTimingParameterDeclarations(
            string[] parameters)
        {
            Debug.Assert(parameters != null, "parameters must not be null");

            List<LegacyPlayerLoopTimingParameterDeclaration> declarations = new();
            int parameterIndex = 0;
            foreach (string parameter in parameters)
            {
                string trimmedParameter = parameter.Trim();
                if (trimmedParameter.Length == 0)
                {
                    continue;
                }

                (string typeName, string parameterName, bool hasDefaultValue) =
                    ReadParameterTypeNameAndDefaultState(trimmedParameter);
                if (typeName.Length > 0 && parameterName.Length > 0)
                {
                    declarations.Add(
                        new LegacyPlayerLoopTimingParameterDeclaration(
                            parameterIndex,
                            typeName,
                            parameterName,
                            hasDefaultValue));
                }

                parameterIndex++;
            }

            return declarations.ToArray();
        }

        internal static (string TypeName, string ParameterName, bool HasDefaultValue)
            ReadParameterTypeNameAndDefaultState(string parameter)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(parameter), "parameter must not be null or whitespace");

            Regex parameterRegex = new(
                @"^(?:\[[^\]]+\]\s*)*(?:(?:this|in|ref|out|params)\s+)*(?<type>.+?)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)(?<default>\s*=.+)?$",
                RegexOptions.Compiled);
            Match match = parameterRegex.Match(parameter);
            if (!match.Success)
            {
                return (string.Empty, string.Empty, false);
            }

            return (match.Groups["type"].Value.Trim(), match.Groups["name"].Value, match.Groups["default"].Success);
        }
    }
}
