using System.Text;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Scans C# source and copies or advances over literal and comment syntax segments.
    /// </summary>
    internal static class DynamicCodeLiteralSyntaxScanner
    {
        internal static bool TryCopyInterpolatedStringLiteral(
            string source,
            StringBuilder rewrittenSource,
            ref int index)
        {
            int start = index;
            if (!TryAdvanceInterpolatedStringLiteral(source, ref index))
            {
                return false;
            }

            rewrittenSource.Append(source, start, index - start);
            return true;
        }

        internal static bool TryCopyLineComment(
            string source,
            StringBuilder rewrittenSource,
            ref int index)
        {
            int start = index;
            if (!TryAdvanceLineComment(source, ref index))
            {
                return false;
            }

            rewrittenSource.Append(source, start, index - start);
            return true;
        }

        internal static bool TryCopyBlockComment(
            string source,
            StringBuilder rewrittenSource,
            ref int index)
        {
            int start = index;
            if (!TryAdvanceBlockComment(source, ref index))
            {
                return false;
            }

            rewrittenSource.Append(source, start, index - start);
            return true;
        }

        internal static bool TryCopyVerbatimStringLiteral(
            string source,
            StringBuilder rewrittenSource,
            ref int index)
        {
            int start = index;
            if (!TryAdvanceVerbatimStringLiteral(source, ref index))
            {
                return false;
            }

            rewrittenSource.Append(source, start, index - start);
            return true;
        }

        private static bool TryAdvanceInterpolatedStringLiteral(string source, ref int index)
        {
            int start = index;
            if (!TryMatchInterpolatedStringStart(source, ref index, out bool isVerbatim))
            {
                return false;
            }

            int interpolationDepth = 0;
            while (index < source.Length)
            {
                if (interpolationDepth > 0)
                {
                    interpolationDepth = AdvanceInterpolatedExpressionSegment(
                        source,
                        ref index,
                        interpolationDepth);
                    continue;
                }

                InterpolatedStringAdvanceResult advanceResult =
                    AdvanceInterpolatedStringContentSegment(source, index, isVerbatim);
                index = advanceResult.Index;
                interpolationDepth = advanceResult.InterpolationDepth;
                if (advanceResult.Completed)
                {
                    return true;
                }
            }

            index = start;
            return false;
        }

        private static int AdvanceInterpolatedExpressionSegment(
            string source,
            ref int index,
            int interpolationDepth)
        {
            if (TryAdvanceInterpolatedExpressionToken(source, ref index))
            {
                return interpolationDepth;
            }

            char expressionCharacter = source[index];
            if (expressionCharacter == '{')
            {
                index++;
                return interpolationDepth + 1;
            }

            if (expressionCharacter == '}')
            {
                index++;
                return interpolationDepth - 1;
            }

            index++;
            return interpolationDepth;
        }

        private static InterpolatedStringAdvanceResult AdvanceInterpolatedStringContentSegment(
            string source,
            int index,
            bool isVerbatim)
        {
            char current = source[index];
            if (current == '{')
            {
                return index + 1 < source.Length && source[index + 1] == '{'
                    ? new InterpolatedStringAdvanceResult(index + 2, 0, false)
                    : new InterpolatedStringAdvanceResult(index + 1, 1, false);
            }

            if (current == '}')
            {
                return index + 1 < source.Length && source[index + 1] == '}'
                    ? new InterpolatedStringAdvanceResult(index + 2, 0, false)
                    : new InterpolatedStringAdvanceResult(index + 1, 0, false);
            }

            if (!isVerbatim && current == '\\')
            {
                int escapedIndex = index;
                DynamicCodeRegularStringLiteralUnescaper.AdvanceEscapedLiteralSequence(source, ref escapedIndex);
                return new InterpolatedStringAdvanceResult(escapedIndex, 0, false);
            }

            if (current != '"')
            {
                return new InterpolatedStringAdvanceResult(index + 1, 0, false);
            }

            if (isVerbatim && index + 1 < source.Length && source[index + 1] == '"')
            {
                return new InterpolatedStringAdvanceResult(index + 2, 0, false);
            }

            return new InterpolatedStringAdvanceResult(index + 1, 0, true);
        }

        private static bool TryMatchInterpolatedStringStart(
            string source,
            ref int index,
            out bool isVerbatim)
        {
            isVerbatim = false;

            if (source[index] == '$')
            {
                if (index + 1 < source.Length && source[index + 1] == '"')
                {
                    index += 2;
                    return true;
                }

                if (index + 2 < source.Length && source[index + 1] == '@' && source[index + 2] == '"')
                {
                    isVerbatim = true;
                    index += 3;
                    return true;
                }

                return false;
            }

            if (source[index] != '@')
            {
                return false;
            }

            if (index + 2 < source.Length && source[index + 1] == '$' && source[index + 2] == '"')
            {
                isVerbatim = true;
                index += 3;
                return true;
            }

            return false;
        }

        private static bool TryAdvanceInterpolatedExpressionToken(string source, ref int index)
        {
            if (TryAdvanceInterpolatedStringLiteral(source, ref index))
            {
                return true;
            }

            if (TryAdvanceVerbatimStringLiteral(source, ref index))
            {
                return true;
            }

            if (TryAdvanceRegularStringLiteral(source, ref index))
            {
                return true;
            }

            if (TryAdvanceCharLiteral(source, ref index))
            {
                return true;
            }

            if (TryAdvanceLineComment(source, ref index))
            {
                return true;
            }

            if (TryAdvanceBlockComment(source, ref index))
            {
                return true;
            }

            return false;
        }

        private static bool TryAdvanceLineComment(string source, ref int index)
        {
            if (index + 1 >= source.Length || source[index] != '/' || source[index + 1] != '/')
            {
                return false;
            }

            index += 2;
            while (index < source.Length && source[index] != '\n')
            {
                index++;
            }

            if (index < source.Length)
            {
                index++;
            }

            return true;
        }

        private static bool TryAdvanceBlockComment(string source, ref int index)
        {
            if (index + 1 >= source.Length || source[index] != '/' || source[index + 1] != '*')
            {
                return false;
            }

            index += 2;
            while (index < source.Length)
            {
                if (source[index] == '*' && index + 1 < source.Length && source[index + 1] == '/')
                {
                    index += 2;
                    return true;
                }

                index++;
            }

            return true;
        }

        private static bool TryAdvanceVerbatimStringLiteral(string source, ref int index)
        {
            if (source[index] != '@'
                || index + 1 >= source.Length
                || source[index + 1] != '"')
            {
                return false;
            }

            int start = index;
            index += 2;

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

                index++;
                return true;
            }

            index = start;
            return false;
        }

        private static bool TryAdvanceRegularStringLiteral(string source, ref int index)
        {
            if (source[index] != '"')
            {
                return false;
            }

            if (index > 0 && (source[index - 1] == '@' || source[index - 1] == '$'))
            {
                return false;
            }

            int start = index;
            index++;

            while (index < source.Length)
            {
                char current = source[index];
                if (current == '\\')
                {
                    DynamicCodeRegularStringLiteralUnescaper.AdvanceEscapedLiteralSequence(source, ref index);
                    continue;
                }

                if (current == '"')
                {
                    index++;
                    return true;
                }

                index++;
            }

            index = start;
            return false;
        }

        private static bool TryAdvanceCharLiteral(string source, ref int index)
        {
            if (source[index] != '\'')
            {
                return false;
            }

            int start = index;
            index++;

            while (index < source.Length)
            {
                char current = source[index];
                if (current == '\\')
                {
                    DynamicCodeRegularStringLiteralUnescaper.AdvanceEscapedLiteralSequence(source, ref index);
                    continue;
                }

                if (current == '\'')
                {
                    index++;
                    return true;
                }

                index++;
            }

            index = start;
            return false;
        }

        internal static bool TryCopyCharLiteral(
            string source,
            StringBuilder rewrittenSource,
            ref int index)
        {
            int start = index;
            if (!TryAdvanceCharLiteral(source, ref index))
            {
                return false;
            }

            rewrittenSource.Append(source, start, index - start);
            return true;
        }

        internal static bool TryCopyRegularStringLiteral(
            string source,
            StringBuilder rewrittenSource,
            ref int index)
        {
            int start = index;
            if (!TryAdvanceRegularStringLiteral(source, ref index))
            {
                return false;
            }

            rewrittenSource.Append(source, start, index - start);
            return true;
        }

        private readonly struct InterpolatedStringAdvanceResult
        {
            public InterpolatedStringAdvanceResult(int index, int interpolationDepth, bool completed)
            {
                Index = index;
                InterpolationDepth = interpolationDepth;
                Completed = completed;
            }

            public int Index { get; }
            public int InterpolationDepth { get; }
            public bool Completed { get; }
        }
    }
}
