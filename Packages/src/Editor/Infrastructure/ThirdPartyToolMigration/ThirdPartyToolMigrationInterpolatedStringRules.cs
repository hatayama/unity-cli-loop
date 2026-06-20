using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Finds literal boundaries inside regular interpolated strings used by migration argument parsing.
    /// </summary>
    internal static class ThirdPartyToolMigrationInterpolatedStringRules
    {
        internal static int FindRegularInterpolatedStringEndIndex(string source, int startIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(startIndex >= 0, "startIndex must not be negative");
            Debug.Assert(
                ThirdPartyToolMigrationParsingRules.StartsWith(source, startIndex, "$\""),
                "startIndex must point to an interpolated string");

            int index = startIndex + 2;
            int interpolationBraceDepth = 0;
            while (index < source.Length)
            {
                if (interpolationBraceDepth == 0)
                {
                    if (source[index] == '\\')
                    {
                        index += 2;
                        continue;
                    }

                    if (source[index] == '"')
                    {
                        return index;
                    }

                    if (source[index] == '{')
                    {
                        if (index + 1 < source.Length && source[index + 1] == '{')
                        {
                            index += 2;
                            continue;
                        }

                        interpolationBraceDepth = 1;
                    }

                    index++;
                    continue;
                }

                int skippedLiteralEndIndex = FindSkippedInterpolationLiteralEndIndex(source, index);
                if (skippedLiteralEndIndex >= 0)
                {
                    index = skippedLiteralEndIndex + 1;
                    continue;
                }

                if (source[index] == '{')
                {
                    interpolationBraceDepth++;
                    index++;
                    continue;
                }

                if (source[index] == '}')
                {
                    interpolationBraceDepth--;
                    index++;
                    continue;
                }

                index++;
            }

            return -1;
        }

        private static int FindSkippedInterpolationLiteralEndIndex(string source, int startIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(startIndex >= 0, "startIndex must not be negative");

            if (ThirdPartyToolMigrationParsingRules.IsRawStringStart(source, startIndex))
            {
                return FindRawStringEndIndex(source, startIndex);
            }

            if (ThirdPartyToolMigrationParsingRules.StartsWith(source, startIndex, "$\""))
            {
                return FindRegularInterpolatedStringEndIndex(source, startIndex);
            }

            if (ThirdPartyToolMigrationParsingRules.StartsWith(source, startIndex, "@\"") ||
                ThirdPartyToolMigrationParsingRules.StartsWith(source, startIndex, "$@\"") ||
                ThirdPartyToolMigrationParsingRules.StartsWith(source, startIndex, "@$\""))
            {
                int quoteIndex = startIndex + ThirdPartyToolMigrationParsingRules.GetStringPrefixLength(
                    source,
                    startIndex);
                return FindVerbatimStringEndIndex(source, quoteIndex);
            }

            if (source[startIndex] == '"')
            {
                return FindRegularStringEndIndex(source, startIndex);
            }

            if (source[startIndex] == '\'')
            {
                return FindCharLiteralEndIndex(source, startIndex);
            }

            return -1;
        }

        private static int FindRawStringEndIndex(string source, int startIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(startIndex >= 0, "startIndex must not be negative");

            int dollarCount = ThirdPartyToolMigrationParsingRules.CountRepeatedCharacter(source, startIndex, '$');
            int quoteIndex = startIndex + dollarCount;
            int quoteCount = ThirdPartyToolMigrationParsingRules.CountRepeatedCharacter(source, quoteIndex, '"');
            int index = quoteIndex + quoteCount;
            while (index < source.Length)
            {
                if (ThirdPartyToolMigrationParsingRules.HasRepeatedCharacterAt(
                        source,
                        index,
                        '"',
                        quoteCount))
                {
                    return index + quoteCount - 1;
                }

                index++;
            }

            return -1;
        }

        private static int FindVerbatimStringEndIndex(string source, int quoteIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(quoteIndex >= 0, "quoteIndex must not be negative");

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

                return index;
            }

            return -1;
        }

        private static int FindRegularStringEndIndex(string source, int quoteIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(quoteIndex >= 0, "quoteIndex must not be negative");

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
                    return index;
                }

                index++;
            }

            return -1;
        }

        private static int FindCharLiteralEndIndex(string source, int quoteIndex)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(quoteIndex >= 0, "quoteIndex must not be negative");

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
                    return index;
                }

                index++;
            }

            return -1;
        }
    }
}
