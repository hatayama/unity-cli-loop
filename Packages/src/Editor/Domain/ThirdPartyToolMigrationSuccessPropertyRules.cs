using System;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

using CodeTextMask = io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationParsingRules.CodeTextMask;

using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationParsingRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingCleanupRules;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationTimingMethodBodyRules;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Detects and removes a derived class's own Success declaration when it hides UnityCliLoopToolResponse.Success.
    /// </summary>
    public static class ThirdPartyToolMigrationSuccessPropertyRules
    {
        private const string CurrentHidingEligibleBaseTypeName = "UnityCliLoopToolResponse";
        private const string LegacyHidingEligibleBaseTypeName = "BaseToolResponse";

        private static readonly Regex ClassBaseListRegex = new(
            @"\bclass\s+[A-Za-z_][A-Za-z0-9_]*\s*(?:<[^>{]*>)?\s*:\s*(?<baseList>[^{]+)\{",
            RegexOptions.Compiled);

        private static readonly Regex HidingEligibleBaseListRegex = new(
            $@"\b(?:{LegacyHidingEligibleBaseTypeName}|{CurrentHidingEligibleBaseTypeName})\b",
            RegexOptions.Compiled);

        // Matches only the auto-property shape; a getter with logic (e.g. "{ get { ... } }") never matches.
        private static readonly Regex SuccessAutoPropertyRegex = new(
            @"public\s+bool\s+Success\s*\{\s*get;\s*(?:set;\s*)?\}\s*(?:=\s*[^;]+;)?\s*(?:\r?\n)?",
            RegexOptions.Compiled);

        private static readonly Regex SuccessPropertyStartRegex = new(
            @"public\s+bool\s+Success\s*\{",
            RegexOptions.Compiled);

        public static bool ContainsSuccessPropertyHidingUnityCliLoopToolResponse(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            if (!ContainsTextFragment(source, "Success"))
            {
                return false;
            }

            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            return FindEligibleClassAutoPropertyMatch(source, codeTextMask) != null;
        }

        public static bool ContainsNonAutoPropertySuccessHidingUnityCliLoopToolResponse(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            if (!ContainsTextFragment(source, "Success"))
            {
                return false;
            }

            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            foreach (Match classMatch in ClassBaseListRegex.Matches(source))
            {
                (bool isEligible, int openBraceIndex, int closeBraceIndex) =
                    ReadEligibleClassBodyRange(source, codeTextMask, classMatch);
                if (!isEligible)
                {
                    continue;
                }

                int rangeLength = closeBraceIndex - openBraceIndex;
                Match autoPropertyMatch = SuccessAutoPropertyRegex.Match(source, openBraceIndex, rangeLength);
                if (autoPropertyMatch.Success && codeTextMask.IsCodeAt(autoPropertyMatch.Index))
                {
                    continue;
                }

                Match successStartMatch = SuccessPropertyStartRegex.Match(source, openBraceIndex, rangeLength);
                if (successStartMatch.Success && codeTextMask.IsCodeAt(successStartMatch.Index))
                {
                    return true;
                }
            }

            return false;
        }

        public static (string Content, int ReplacementCount) RemoveSuccessPropertyHidingDeclarationsInCode(
            string source)
        {
            Debug.Assert(source != null, "source must not be null");

            if (!ContainsTextFragment(source, "Success"))
            {
                return (source, 0);
            }

            CodeTextMask codeTextMask = CodeTextMask.Create(source);
            StringBuilder builder = new(source.Length);
            int sourceCopyIndex = 0;
            int replacementCount = 0;

            foreach (Match classMatch in ClassBaseListRegex.Matches(source))
            {
                (bool isEligible, int openBraceIndex, int closeBraceIndex) =
                    ReadEligibleClassBodyRange(source, codeTextMask, classMatch);
                if (!isEligible)
                {
                    continue;
                }

                Match propertyMatch = SuccessAutoPropertyRegex.Match(
                    source,
                    openBraceIndex,
                    closeBraceIndex - openBraceIndex);
                if (!propertyMatch.Success ||
                    !codeTextMask.IsCodeAt(propertyMatch.Index) ||
                    propertyMatch.Index < sourceCopyIndex)
                {
                    continue;
                }

                int removalStartIndex = ExtendRemovalStartOverDocComments(
                    source,
                    ReadLegacyPlayerLoopTimingDeclarationRemovalStart(source, propertyMatch.Index, codeTextMask));
                if (removalStartIndex < sourceCopyIndex)
                {
                    continue;
                }

                builder.Append(source, sourceCopyIndex, removalStartIndex - sourceCopyIndex);
                sourceCopyIndex = propertyMatch.Index + propertyMatch.Length;
                replacementCount++;
            }

            if (replacementCount == 0)
            {
                return (source, 0);
            }

            builder.Append(source, sourceCopyIndex, source.Length - sourceCopyIndex);
            return (builder.ToString(), replacementCount);
        }

        private static Match FindEligibleClassAutoPropertyMatch(string source, CodeTextMask codeTextMask)
        {
            foreach (Match classMatch in ClassBaseListRegex.Matches(source))
            {
                (bool isEligible, int openBraceIndex, int closeBraceIndex) =
                    ReadEligibleClassBodyRange(source, codeTextMask, classMatch);
                if (!isEligible)
                {
                    continue;
                }

                Match propertyMatch = SuccessAutoPropertyRegex.Match(
                    source,
                    openBraceIndex,
                    closeBraceIndex - openBraceIndex);
                if (propertyMatch.Success && codeTextMask.IsCodeAt(propertyMatch.Index))
                {
                    return propertyMatch;
                }
            }

            return null;
        }

        private static (bool IsEligible, int OpenBraceIndex, int CloseBraceIndex) ReadEligibleClassBodyRange(
            string source,
            CodeTextMask codeTextMask,
            Match classMatch)
        {
            if (!codeTextMask.IsCodeAt(classMatch.Index) ||
                !HidingEligibleBaseListRegex.IsMatch(classMatch.Groups["baseList"].Value))
            {
                return (false, -1, -1);
            }

            int openBraceIndex = classMatch.Index + classMatch.Length - 1;
            int closeBraceIndex = FindBlockClosingBraceIndex(source, codeTextMask, openBraceIndex);
            return closeBraceIndex < 0 ? (false, -1, -1) : (true, openBraceIndex, closeBraceIndex);
        }

        // Extends a removal start index over immediately preceding "///" XML doc comment lines,
        // so a property's doc comment is deleted along with the property it documents.
        private static int ExtendRemovalStartOverDocComments(string source, int removalStartIndex)
        {
            int index = removalStartIndex;
            while (index > 0)
            {
                int lineTerminatorIndex = index - 1;
                int searchFromIndex = lineTerminatorIndex - 1;
                int previousNewLineIndex = searchFromIndex < 0 ? -1 : source.LastIndexOf('\n', searchFromIndex);
                int previousLineStartIndex = previousNewLineIndex < 0 ? 0 : previousNewLineIndex + 1;
                string previousLine = source.Substring(
                    previousLineStartIndex,
                    lineTerminatorIndex - previousLineStartIndex + 1);
                if (!previousLine.TrimStart().StartsWith("///", StringComparison.Ordinal))
                {
                    return index;
                }

                index = previousLineStartIndex;
            }

            return index;
        }
    }
}
