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
    internal static class ThirdPartyToolMigrationCodeTextMaskInterpolationRules
    {
        internal static int MarkInterpolatedRawStringAsIgnored(
            bool[] codeCharacters,
            string source,
            int dollarIndex)
        {
            Debug.Assert(codeCharacters != null, "codeCharacters must not be null");
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(dollarIndex >= 0, "dollarIndex must not be negative");

            int dollarCount = CountRepeatedCharacter(source, dollarIndex, '$');
            int quoteIndex = dollarIndex + dollarCount;
            int quoteCount = CountRepeatedCharacter(source, quoteIndex, '"');
            Debug.Assert(dollarCount > 0, "dollarIndex must point to an interpolated raw string prefix");
            Debug.Assert(
                quoteCount >= MinimumRawStringDelimiterQuoteCount,
                "quoteIndex must point to a raw string delimiter");

            int literalStartIndex = dollarIndex;
            int index = quoteIndex + quoteCount;
            while (index < source.Length)
            {
                if (HasRepeatedCharacterAt(source, index, '"', quoteCount))
                {
                    MarkRangeAsIgnored(codeCharacters, literalStartIndex, index + quoteCount);
                    return index + quoteCount;
                }

                if (source[index] == '{')
                {
                    int braceCount = CountRepeatedCharacter(source, index, '{');
                    if (braceCount < dollarCount)
                    {
                        index += braceCount;
                        continue;
                    }

                    MarkRangeAsIgnored(codeCharacters, literalStartIndex, index);
                    index = MarkRawInterpolationHoleNestedTextAsIgnored(
                        codeCharacters,
                        source,
                        index,
                        dollarCount);
                    literalStartIndex = index;
                    continue;
                }

                if (source[index] == '}')
                {
                    int braceCount = CountRepeatedCharacter(source, index, '}');
                    index += braceCount;
                    continue;
                }

                index++;
            }

            MarkRangeAsIgnored(codeCharacters, literalStartIndex, source.Length);
            return source.Length;
        }

        internal static int MarkInterpolatedRegularStringAsIgnored(
            bool[] codeCharacters,
            string source,
            int dollarIndex)
        {
            Debug.Assert(codeCharacters != null, "codeCharacters must not be null");
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(dollarIndex >= 0, "dollarIndex must not be negative");

            int quoteIndex = dollarIndex + 1;
            int literalStartIndex = dollarIndex;
            int index = quoteIndex + 1;
            while (index < source.Length)
            {
                if (source[index] == '\\')
                {
                    index += 2;
                    continue;
                }

                if (source[index] == '{')
                {
                    if (index + 1 < source.Length && source[index + 1] == '{')
                    {
                        index += 2;
                        continue;
                    }

                    MarkRangeAsIgnored(codeCharacters, literalStartIndex, index);
                    index = MarkInterpolationHoleNestedTextAsIgnored(codeCharacters, source, index);
                    literalStartIndex = index;
                    continue;
                }

                if (source[index] == '"')
                {
                    MarkRangeAsIgnored(codeCharacters, literalStartIndex, index + 1);
                    return index + 1;
                }

                index++;
            }

            MarkRangeAsIgnored(codeCharacters, literalStartIndex, source.Length);
            return source.Length;
        }

        internal static int MarkInterpolatedVerbatimStringAsIgnored(
            bool[] codeCharacters,
            string source,
            int prefixIndex)
        {
            Debug.Assert(codeCharacters != null, "codeCharacters must not be null");
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(prefixIndex >= 0, "prefixIndex must not be negative");

            int quoteIndex = prefixIndex + 2;
            int literalStartIndex = prefixIndex;
            int index = quoteIndex + 1;
            while (index < source.Length)
            {
                if (source[index] == '"')
                {
                    if (index + 1 < source.Length && source[index + 1] == '"')
                    {
                        index += 2;
                        continue;
                    }

                    MarkRangeAsIgnored(codeCharacters, literalStartIndex, index + 1);
                    return index + 1;
                }

                if (source[index] == '{')
                {
                    if (index + 1 < source.Length && source[index + 1] == '{')
                    {
                        index += 2;
                        continue;
                    }

                    MarkRangeAsIgnored(codeCharacters, literalStartIndex, index);
                    index = MarkInterpolationHoleNestedTextAsIgnored(codeCharacters, source, index);
                    literalStartIndex = index;
                    continue;
                }

                if (source[index] == '}' && index + 1 < source.Length && source[index + 1] == '}')
                {
                    index += 2;
                    continue;
                }

                index++;
            }

            MarkRangeAsIgnored(codeCharacters, literalStartIndex, source.Length);
            return source.Length;
        }

        internal static int MarkInterpolationHoleNestedTextAsIgnored(
            bool[] codeCharacters,
            string source,
            int openBraceIndex)
        {
            Debug.Assert(codeCharacters != null, "codeCharacters must not be null");
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(openBraceIndex >= 0, "openBraceIndex must not be negative");

            int nestedBraceDepth = 0;
            int index = openBraceIndex + 1;
            while (index < source.Length)
            {
                int ignoredTextEndIndex = MarkIgnoredTextInInterpolationHole(
                    codeCharacters,
                    source,
                    index);
                if (ignoredTextEndIndex != index)
                {
                    index = ignoredTextEndIndex;
                    continue;
                }

                if (source[index] == '{')
                {
                    nestedBraceDepth++;
                    index++;
                    continue;
                }

                if (source[index] == '}')
                {
                    if (nestedBraceDepth == 0)
                    {
                        return index + 1;
                    }

                    nestedBraceDepth--;
                }

                index++;
            }

            return source.Length;
        }

        internal static int MarkRawInterpolationHoleNestedTextAsIgnored(
            bool[] codeCharacters,
            string source,
            int openBraceIndex,
            int interpolationBraceCount)
        {
            Debug.Assert(codeCharacters != null, "codeCharacters must not be null");
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(openBraceIndex >= 0, "openBraceIndex must not be negative");
            Debug.Assert(interpolationBraceCount > 0, "interpolationBraceCount must be positive");

            int nestedBraceDepth = 0;
            int index = openBraceIndex + interpolationBraceCount;
            while (index < source.Length)
            {
                int ignoredTextEndIndex = MarkIgnoredTextInInterpolationHole(
                    codeCharacters,
                    source,
                    index);
                if (ignoredTextEndIndex != index)
                {
                    index = ignoredTextEndIndex;
                    continue;
                }

                if (source[index] == '{')
                {
                    nestedBraceDepth++;
                    index++;
                    continue;
                }

                if (source[index] == '}')
                {
                    bool isClosingInterpolation =
                        nestedBraceDepth == 0 &&
                        HasRepeatedCharacterAt(source, index, '}', interpolationBraceCount);
                    if (isClosingInterpolation)
                    {
                        return index + interpolationBraceCount;
                    }

                    if (nestedBraceDepth > 0)
                    {
                        nestedBraceDepth--;
                    }
                }

                index++;
            }

            return source.Length;
        }

        internal static int MarkIgnoredTextInInterpolationHole(
            bool[] codeCharacters,
            string source,
            int startIndex)
        {
            Debug.Assert(codeCharacters != null, "codeCharacters must not be null");
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(startIndex >= 0, "startIndex must not be negative");

            if (IsInterpolatedRawStringStart(source, startIndex))
            {
                return MarkInterpolatedRawStringAsIgnored(codeCharacters, source, startIndex);
            }

            if (StartsWith(source, startIndex, "$@\"") || StartsWith(source, startIndex, "@$\""))
            {
                return MarkInterpolatedVerbatimStringAsIgnored(codeCharacters, source, startIndex);
            }

            if (StartsWith(source, startIndex, "$\"") && !StartsWith(source, startIndex, "$\"\"\""))
            {
                return MarkInterpolatedRegularStringAsIgnored(codeCharacters, source, startIndex);
            }

            int ignoredTextEndIndex = GetIgnoredTextEndIndex(source, startIndex);
            if (ignoredTextEndIndex == startIndex)
            {
                return startIndex;
            }

            MarkRangeAsIgnored(codeCharacters, startIndex, ignoredTextEndIndex);
            return ignoredTextEndIndex;
        }
    }
}
