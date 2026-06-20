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
    internal static class ThirdPartyToolMigrationCodeTextMaskBuilder
    {
        internal static bool[] CreateCodeCharacters(string source)
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

        internal static int GetIgnoredTextEndIndex(string source, int startIndex)
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

        internal static int FindLineCommentEndIndex(string source, int startIndex)
        {
            int index = startIndex;
            while (index < source.Length && source[index] != '\n')
            {
                index++;
            }

            return index;
        }

        internal static int FindBlockCommentEndIndex(string source, int startIndex)
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

        internal static int FindRegularStringEndIndex(string source, int quoteIndex)
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

        internal static int FindVerbatimStringEndIndex(string source, int quoteIndex)
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

        internal static int FindRawStringEndIndex(string source, int quoteIndex)
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

        internal static int FindCharLiteralEndIndex(string source, int quoteIndex)
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

        internal static void MarkRangeAsIgnored(bool[] codeCharacters, int startIndex, int endIndex)
        {
            Debug.Assert(codeCharacters != null, "codeCharacters must not be null");
            Debug.Assert(startIndex >= 0, "startIndex must not be negative");
            Debug.Assert(endIndex >= startIndex, "endIndex must not be less than startIndex");

            for (int i = startIndex; i < endIndex && i < codeCharacters.Length; i++)
            {
                codeCharacters[i] = false;
            }
        }

        internal static bool IsInterpolatedRawStringStart(string source, int startIndex)
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
