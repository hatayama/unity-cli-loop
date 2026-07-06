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
    public static class ThirdPartyToolMigrationCodeTextMaskBuilder
    {
        public static bool[] CreateCodeCharacters(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            bool[] codeCharacters = new bool[source.Length];
            for (int i = 0; i < codeCharacters.Length; i++)
            {
                codeCharacters[i] = true;
            }

            int index = 0;
            while (index < source.Length)
            {
                if (IsInterpolatedRawStringStart(source, index))
                {
                    index = MarkInterpolatedRawStringAsIgnored(codeCharacters, source, index);
                    continue;
                }

                if (StartsWith(source, index, "$@\"") || StartsWith(source, index, "@$\""))
                {
                    index = MarkInterpolatedVerbatimStringAsIgnored(codeCharacters, source, index);
                    continue;
                }

                if (StartsWith(source, index, "$\"") && !StartsWith(source, index, "$\"\"\""))
                {
                    index = MarkInterpolatedRegularStringAsIgnored(codeCharacters, source, index);
                    continue;
                }

                int ignoredTextEndIndex = GetIgnoredTextEndIndex(source, index);
                if (ignoredTextEndIndex == index)
                {
                    index++;
                    continue;
                }

                MarkRangeAsIgnored(codeCharacters, index, ignoredTextEndIndex);
                index = ignoredTextEndIndex;
            }

            return codeCharacters;
        }

        public static int GetIgnoredTextEndIndex(string source, int startIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(startIndex >= 0, "startIndex must not be negative");

            if (StartsWith(source, startIndex, "//"))
            {
                return FindLineCommentEndIndex(source, startIndex);
            }

            if (StartsWith(source, startIndex, "/*"))
            {
                return FindBlockCommentEndIndex(source, startIndex);
            }

            if (StartsWith(source, startIndex, "$@\"") || StartsWith(source, startIndex, "@$\""))
            {
                return FindVerbatimStringEndIndex(source, startIndex + 2);
            }

            if (StartsWith(source, startIndex, "@\""))
            {
                return FindVerbatimStringEndIndex(source, startIndex + 1);
            }

            if (IsInterpolatedRawStringStart(source, startIndex))
            {
                int dollarCount = CountRepeatedCharacter(source, startIndex, '$');
                return FindRawStringEndIndex(source, startIndex + dollarCount);
            }

            if (StartsWith(source, startIndex, "$\""))
            {
                return FindRegularStringEndIndex(source, startIndex + 1);
            }

            if (StartsWith(source, startIndex, "\"\"\""))
            {
                return FindRawStringEndIndex(source, startIndex);
            }

            if (source[startIndex] == '"')
            {
                return FindRegularStringEndIndex(source, startIndex);
            }

            if (source[startIndex] == '\'')
            {
                return FindCharLiteralEndIndex(source, startIndex);
            }

            return startIndex;
        }

        public static int FindLineCommentEndIndex(string source, int startIndex)
        {
            int index = startIndex;
            while (index < source.Length && source[index] != '\n')
            {
                index++;
            }

            return index;
        }

        public static int FindBlockCommentEndIndex(string source, int startIndex)
        {
            int index = startIndex + 2;
            while (index + 1 < source.Length)
            {
                if (source[index] == '*' && source[index + 1] == '/')
                {
                    return index + 2;
                }

                index++;
            }

            return source.Length;
        }

        public static int FindRegularStringEndIndex(string source, int quoteIndex)
        {
            int index = quoteIndex + 1;
            while (index < source.Length)
            {
                if (source[index] == '\\')
                {
                    index += 2;
                    continue;
                }

                if (source[index] == '"')
                {
                    return index + 1;
                }

                index++;
            }

            return source.Length;
        }

        public static int FindVerbatimStringEndIndex(string source, int quoteIndex)
        {
            int index = quoteIndex + 1;
            while (index < source.Length)
            {
                if (source[index] != '"')
                {
                    index++;
                    continue;
                }

                if (index + 1 < source.Length && source[index + 1] == '"')
                {
                    index += 2;
                    continue;
                }

                return index + 1;
            }

            return source.Length;
        }

        public static int FindRawStringEndIndex(string source, int quoteIndex)
        {
            int quoteCount = CountRepeatedCharacter(source, quoteIndex, '"');
            Debug.Assert(
                quoteCount >= MinimumRawStringDelimiterQuoteCount,
                "quoteIndex must point to a raw string delimiter");

            int index = quoteIndex + quoteCount;
            while (index + quoteCount <= source.Length)
            {
                if (HasRepeatedCharacterAt(source, index, '"', quoteCount))
                {
                    return index + quoteCount;
                }

                index++;
            }

            return source.Length;
        }

        public static int FindCharLiteralEndIndex(string source, int quoteIndex)
        {
            int index = quoteIndex + 1;
            while (index < source.Length)
            {
                if (source[index] == '\\')
                {
                    index += 2;
                    continue;
                }

                if (source[index] == '\'')
                {
                    return index + 1;
                }

                index++;
            }

            return source.Length;
        }

        public static void MarkRangeAsIgnored(bool[] codeCharacters, int startIndex, int endIndex)
        {
            Debug.Assert(codeCharacters != null, "codeCharacters must not be null");
            Debug.Assert(startIndex >= 0, "startIndex must not be negative");
            Debug.Assert(endIndex >= startIndex, "endIndex must not be less than startIndex");

            for (int i = startIndex; i < endIndex && i < codeCharacters.Length; i++)
            {
                codeCharacters[i] = false;
            }
        }

        public static bool IsInterpolatedRawStringStart(string source, int startIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(startIndex >= 0, "startIndex must not be negative");

            if (startIndex >= source.Length || source[startIndex] != '$')
            {
                return false;
            }

            int dollarCount = CountRepeatedCharacter(source, startIndex, '$');
            int quoteIndex = startIndex + dollarCount;
            return CountRepeatedCharacter(source, quoteIndex, '"') >= MinimumRawStringDelimiterQuoteCount;
        }
    }
}
