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
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationCodeTextDetectionRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationScreenshotArgumentRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationScreenshotDeconstructionRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationScreenshotDetectionRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationScreenshotRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingArgumentRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingCallerRules;
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
    public static class ThirdPartyToolMigrationTimingCleanupRules
    {
        public static bool CanRemoveLegacyPlayerLoopTimingParameterFromMethod(
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

        public static bool ContainsMigratedMainThreadSwitcherSwitchCall(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            return RegexMatchesCode(source, MigratedMainThreadSwitcherSwitchRegex);
        }

        public static (string Content, int ReplacementCount) RemoveUnusedLegacyPlayerLoopTimingDeclarationsInCode(
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

                (int removalStartIndex, int removalEndIndex) = ReadLegacyPlayerLoopTimingDeclarationRemovalRange(
                    source,
                    match,
                    codeTextMask);
                if (removalStartIndex < sourceCopyIndex)
                {
                    (removalStartIndex, removalEndIndex) = ReadRemovalRangeAfterPreviousRemoval(
                        source,
                        builder,
                        sourceCopyIndex,
                        match);
                }

                string declarationName = match.Groups["name"].Value;
                int removalLength = removalEndIndex - removalStartIndex;
                string sourceWithoutDeclaration = source.Remove(removalStartIndex, removalLength);
                if (ContainsIdentifierInCode(sourceWithoutDeclaration, declarationName))
                {
                    continue;
                }

                builder.Append(source, sourceCopyIndex, removalStartIndex - sourceCopyIndex);
                sourceCopyIndex = removalEndIndex;
                replacementCount++;
            }

            if (replacementCount == 0)
            {
                return (source, 0);
            }

            builder.Append(source, sourceCopyIndex, source.Length - sourceCopyIndex);
            return (builder.ToString(), replacementCount);
        }

        /// <summary>
        /// Returns the source range that removing a timing declaration must cover, including its attributes.
        /// </summary>
        public static (int StartIndex, int EndIndex) ReadLegacyPlayerLoopTimingDeclarationRemovalRange(
            string source,
            Match declarationMatch,
            CodeTextMask codeTextMask)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(declarationMatch != null && declarationMatch.Success, "declarationMatch must be a successful match");

            return ReadDeclarationRemovalRange(
                source,
                declarationMatch.Index,
                declarationMatch.Index + declarationMatch.Length,
                codeTextMask);
        }

        /// <summary>
        /// Returns the source range that removing a member declaration must cover, including its attributes.
        /// The declaration range must start at its line start or at the declaration itself, and end after
        /// at most one line break.
        /// </summary>
        public static (int StartIndex, int EndIndex) ReadDeclarationRemovalRange(
            string source,
            int declarationStartIndex,
            int declarationEndIndex,
            CodeTextMask codeTextMask)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(
                declarationStartIndex >= 0 && declarationStartIndex <= declarationEndIndex,
                "declaration range must be ordered");

            int removalStartIndex = ReadLegacyPlayerLoopTimingDeclarationRemovalStart(
                source,
                declarationStartIndex,
                codeTextMask);
            int removalEndIndex = declarationEndIndex;
            (int attributeStartIndex, bool sharesLineWithCode) =
                ReadInlineAttributeRemovalStart(source, removalStartIndex, codeTextMask);
            if (!sharesLineWithCode)
            {
                return (attributeStartIndex, removalEndIndex);
            }

            // The attributes share a line with code that stays, so that line keeps its own line break
            // and the declaration's line break is kept in its place.
            return (attributeStartIndex, removalEndIndex - CountTrailingLineBreakLength(source, removalEndIndex));
        }

        /// <summary>
        /// Moves the removal start back over attributes left before it, together with the whitespace before them.
        /// Attributes that follow other code on the same line are taken up to the end of that code; a line that
        /// holds only attributes is taken whole, along with any attribute-only lines above it. The flag tells
        /// whether the final start follows code that stays on its line.
        /// </summary>
        public static (int StartIndex, bool SharesLineWithCode) ReadInlineAttributeRemovalStart(
            string source,
            int removalStartIndex,
            CodeTextMask codeTextMask)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(removalStartIndex >= 0, "removalStartIndex must not be negative");

            int startIndex = removalStartIndex;
            bool sharesLineWithCode = false;
            int scanIndex = SkipWhitespaceBackward(source, startIndex - 1);
            while (scanIndex >= 0 && source[scanIndex] == ']' && codeTextMask.IsCodeAt(scanIndex))
            {
                int openingBracketIndex = FindOpeningAttributeBracket(source, scanIndex, codeTextMask);
                if (openingBracketIndex < 0)
                {
                    break;
                }

                if (HasOnlyWhitespaceBeforeIndexOnLine(source, openingBracketIndex))
                {
                    startIndex = ReadLegacyPlayerLoopTimingDeclarationRemovalStart(
                        source,
                        GetLineStartIndex(source, openingBracketIndex),
                        codeTextMask);
                    sharesLineWithCode = false;
                }
                else
                {
                    startIndex = SkipWhitespaceBackward(source, openingBracketIndex - 1) + 1;
                    sharesLineWithCode = true;
                }

                scanIndex = SkipWhitespaceBackward(source, startIndex - 1);
            }

            return (startIndex, sharesLineWithCode);
        }

        // The previous removal already covers the inline attributes' line up to this declaration's attributes, so
        // only the rest is removed here. The declaration's line break stays only when code kept in the output
        // still shares that line; otherwise the line would be left empty.
        private static (int StartIndex, int EndIndex) ReadRemovalRangeAfterPreviousRemoval(
            string source,
            StringBuilder builder,
            int sourceCopyIndex,
            Match declarationMatch)
        {
            int removalEndIndex = declarationMatch.Index + declarationMatch.Length;
            if (!HasCodeOnLastOutputLine(builder))
            {
                return (sourceCopyIndex, removalEndIndex);
            }

            return (sourceCopyIndex, removalEndIndex - CountTrailingLineBreakLength(source, removalEndIndex));
        }

        private static bool HasCodeOnLastOutputLine(StringBuilder builder)
        {
            for (int index = builder.Length - 1; index >= 0 && builder[index] != '\n'; index--)
            {
                if (!char.IsWhiteSpace(builder[index]))
                {
                    return true;
                }
            }

            return false;
        }

        private static int CountTrailingLineBreakLength(string source, int endIndex)
        {
            if (endIndex >= 2 && source[endIndex - 2] == '\r' && source[endIndex - 1] == '\n')
            {
                return 2;
            }

            return endIndex >= 1 && source[endIndex - 1] == '\n' ? 1 : 0;
        }

        public static int ReadLegacyPlayerLoopTimingDeclarationRemovalStart(
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

        public static int SkipWhitespaceBackward(string source, int startIndex)
        {
            Debug.Assert(source != null, "source must not be null");

            int index = startIndex;
            while (index >= 0 && char.IsWhiteSpace(source[index]))
            {
                index--;
            }

            return index;
        }

        public static int FindOpeningAttributeBracket(
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

        public static bool HasOnlyWhitespaceBeforeIndexOnLine(string source, int index)
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

        public static int GetLineStartIndex(string source, int index)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(index >= 0, "index must not be negative");

            int previousNewLineIndex = source.LastIndexOf('\n', index);
            return previousNewLineIndex < 0 ? 0 : previousNewLineIndex + 1;
        }

        public static LegacyPlayerLoopTimingParameterDeclaration[] ReadLegacyPlayerLoopTimingParameterDeclarations(
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

        public static (string TypeName, string ParameterName, bool HasDefaultValue)
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
