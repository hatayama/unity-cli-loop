using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace io.github.hatayama.UnityCliLoop
{
    internal static class DynamicCodeLiteralHoister
    {
        private const string LiteralParameterPrefix = "__uloop_literal_";

        public static HoistedLiteralRewriteResult Rewrite(string source)
        {
            StringBuilder rewrittenSource = new StringBuilder(source.Length);
            List<HoistedLiteralBinding> bindings = new List<HoistedLiteralBinding>();
            int index = 0;

            while (index < source.Length)
            {
                if (TryCopyInterpolatedStringLiteral(source, rewrittenSource, ref index))
                {
                    continue;
                }

                if (TryCopyVerbatimStringLiteral(source, rewrittenSource, ref index))
                {
                    continue;
                }

                if (TryCopyCharLiteral(source, rewrittenSource, ref index))
                {
                    continue;
                }

                if (TryCopyLineComment(source, rewrittenSource, ref index))
                {
                    continue;
                }

                if (TryCopyBlockComment(source, rewrittenSource, ref index))
                {
                    continue;
                }

                if (TryHoistRegularStringLiteral(source, rewrittenSource, bindings, ref index))
                {
                    continue;
                }

                if (TryHoistIntegerLiteral(source, rewrittenSource, bindings, ref index))
                {
                    continue;
                }

                rewrittenSource.Append(source[index]);
                index++;
            }

            List<string> declarationLines = new List<string>();
            foreach (HoistedLiteralBinding binding in bindings)
            {
                declarationLines.Add(
                    $"{binding.TypeName} {binding.ParameterName} = ({binding.TypeName})parameters[\"{binding.ParameterName}\"];");
            }

            return new HoistedLiteralRewriteResult(
                rewrittenSource.ToString(),
                bindings,
                declarationLines);
        }

        private static bool TryCopyInterpolatedStringLiteral(
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

        private static bool TryCopyLineComment(
            string source,
            StringBuilder rewrittenSource,
            ref int index)
        {
            if (index + 1 >= source.Length || source[index] != '/' || source[index + 1] != '/')
            {
                return false;
            }

            while (index < source.Length)
            {
                char current = source[index];
                rewrittenSource.Append(current);
                index++;
                if (current == '\n')
                {
                    break;
                }
            }

            return true;
        }

        private static bool TryCopyBlockComment(
            string source,
            StringBuilder rewrittenSource,
            ref int index)
        {
            if (index + 1 >= source.Length || source[index] != '/' || source[index + 1] != '*')
            {
                return false;
            }

            rewrittenSource.Append(source[index]);
            rewrittenSource.Append(source[index + 1]);
            index += 2;

            while (index < source.Length)
            {
                char current = source[index];
                rewrittenSource.Append(current);
                index++;
                if (current == '*' && index < source.Length && source[index] == '/')
                {
                    rewrittenSource.Append('/');
                    index++;
                    break;
                }
            }

            return true;
        }

        private static bool TryCopyVerbatimStringLiteral(
            string source,
            StringBuilder rewrittenSource,
            ref int index)
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
                rewrittenSource.Append(source, start, index - start);
                return true;
            }

            index = start;
            return false;
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
                    if (TryAdvanceInterpolatedExpressionToken(source, ref index))
                    {
                        continue;
                    }

                    char expressionCharacter = source[index];
                    if (expressionCharacter == '{')
                    {
                        interpolationDepth++;
                        index++;
                        continue;
                    }

                    if (expressionCharacter == '}')
                    {
                        interpolationDepth--;
                        index++;
                        continue;
                    }

                    index++;
                    continue;
                }

                char current = source[index];
                if (current == '{')
                {
                    if (index + 1 < source.Length && source[index + 1] == '{')
                    {
                        index += 2;
                        continue;
                    }

                    interpolationDepth = 1;
                    index++;
                    continue;
                }

                if (current == '}')
                {
                    if (index + 1 < source.Length && source[index + 1] == '}')
                    {
                        index += 2;
                        continue;
                    }

                    index++;
                    continue;
                }

                if (!isVerbatim && current == '\\')
                {
                    AdvanceEscapedLiteralSequence(source, ref index);
                    continue;
                }

                if (current == '"')
                {
                    if (isVerbatim && index + 1 < source.Length && source[index + 1] == '"')
                    {
                        index += 2;
                        continue;
                    }

                    index++;
                    return true;
                }

                index++;
            }

            index = start;
            return false;
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
                    AdvanceEscapedLiteralSequence(source, ref index);
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
                    AdvanceEscapedLiteralSequence(source, ref index);
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

        private static bool TryCopyCharLiteral(
            string source,
            StringBuilder rewrittenSource,
            ref int index)
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
                    AdvanceEscapedLiteralSequence(source, ref index);
                    continue;
                }

                if (current == '\'')
                {
                    index++;
                    rewrittenSource.Append(source, start, index - start);
                    return true;
                }

                index++;
            }

            index = start;
            return false;
        }

        private static bool TryHoistRegularStringLiteral(
            string source,
            StringBuilder rewrittenSource,
            List<HoistedLiteralBinding> bindings,
            ref int index)
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
                    AdvanceEscapedLiteralSequence(source, ref index);
                    continue;
                }

                if (current == '"')
                {
                    index++;
                    string literalToken = source.Substring(start, index - start);
                    if (!TryUnescapeRegularStringLiteral(literalToken, out string value))
                    {
                        rewrittenSource.Append(literalToken);
                        return true;
                    }

                    string parameterName = CreateParameterName(bindings.Count);
                    bindings.Add(new HoistedLiteralBinding(parameterName, "string", value));
                    rewrittenSource.Append(parameterName);
                    return true;
                }

                index++;
            }

            index = start;
            return false;
        }

        private static bool TryHoistIntegerLiteral(
            string source,
            StringBuilder rewrittenSource,
            List<HoistedLiteralBinding> bindings,
            ref int index)
        {
            char current = source[index];
            if (!char.IsDigit(current))
            {
                return false;
            }

            if (index > 0)
            {
                char previous = source[index - 1];
                if (char.IsLetterOrDigit(previous) || previous == '_' || previous == '.')
                {
                    return false;
                }
            }

            int start = index;
            index++;
            while (index < source.Length && char.IsDigit(source[index]))
            {
                index++;
            }

            if (index < source.Length && (source[index] == 'L' || source[index] == 'l'))
            {
                int suffixIndex = index;
                index++;
                if (!HasIntegerLiteralBoundary(source, index))
                {
                    index = start;
                    return false;
                }

                string longToken = source.Substring(start, suffixIndex - start);
                if (!long.TryParse(longToken, NumberStyles.None, CultureInfo.InvariantCulture, out long longValue))
                {
                    index = start;
                    return false;
                }

                string longParameterName = CreateParameterName(bindings.Count);
                bindings.Add(new HoistedLiteralBinding(longParameterName, "long", longValue));
                rewrittenSource.Append(longParameterName);
                return true;
            }

            if (!HasIntegerLiteralBoundary(source, index))
            {
                index = start;
                return false;
            }

            string token = source.Substring(start, index - start);
            if (!int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out int intValue))
            {
                index = start;
                return false;
            }

            string parameterName = CreateParameterName(bindings.Count);
            bindings.Add(new HoistedLiteralBinding(parameterName, "int", intValue));
            rewrittenSource.Append(parameterName);
            return true;
        }

        private static bool HasIntegerLiteralBoundary(string source, int index)
        {
            if (index >= source.Length)
            {
                return true;
            }

            char next = source[index];
            return !char.IsLetter(next) && !char.IsDigit(next) && next != '_' && next != '.';
        }

        private static string CreateParameterName(int index)
        {
            return $"{LiteralParameterPrefix}{index}";
        }

        private static void AdvanceEscapedLiteralSequence(string source, ref int index)
        {
            index++;
            if (index >= source.Length)
            {
                return;
            }

            char escape = source[index];
            index++;

            switch (escape)
            {
                case 'u':
                    index = Math.Min(source.Length, index + 4);
                    break;
                case 'U':
                    index = Math.Min(source.Length, index + 8);
                    break;
                case 'x':
                    index = AdvanceVariableLengthHexDigits(source, index, 4);
                    break;
            }
        }

        private static int AdvanceVariableLengthHexDigits(string value, int index, int maxDigits)
        {
            int digitsConsumed = 0;
            while (index < value.Length && digitsConsumed < maxDigits && IsHexDigit(value[index]))
            {
                index++;
                digitsConsumed++;
            }

            return index;
        }

        private static bool TryUnescapeRegularStringLiteral(string token, out string value)
        {
            string inner = token.Substring(1, token.Length - 2);
            StringBuilder unescaped = new StringBuilder(inner.Length);

            for (int index = 0; index < inner.Length; index++)
            {
                char current = inner[index];
                if (current != '\\')
                {
                    unescaped.Append(current);
                    continue;
                }

                if (index + 1 >= inner.Length)
                {
                    value = null;
                    return false;
                }

                index++;
                char escape = inner[index];
                switch (escape)
                {
                    case '\'':
                        unescaped.Append('\'');
                        break;
                    case '"':
                        unescaped.Append('"');
                        break;
                    case '\\':
                        unescaped.Append('\\');
                        break;
                    case '0':
                        unescaped.Append('\0');
                        break;
                    case 'a':
                        unescaped.Append('\a');
                        break;
                    case 'b':
                        unescaped.Append('\b');
                        break;
                    case 'f':
                        unescaped.Append('\f');
                        break;
                    case 'n':
                        unescaped.Append('\n');
                        break;
                    case 'r':
                        unescaped.Append('\r');
                        break;
                    case 't':
                        unescaped.Append('\t');
                        break;
                    case 'v':
                        unescaped.Append('\v');
                        break;
                    case 'u':
                        if (!TryParseHexDigits(inner, ref index, 4, out int unicodeValue))
                        {
                            value = null;
                            return false;
                        }

                        unescaped.Append((char)unicodeValue);
                        break;
                    case 'U':
                        if (!TryParseHexDigits(inner, ref index, 8, out int codePoint) ||
                            !IsValidUtf32CodePoint(codePoint))
                        {
                            value = null;
                            return false;
                        }

                        unescaped.Append(char.ConvertFromUtf32(codePoint));
                        break;
                    case 'x':
                        if (!TryParseVariableLengthHexDigits(inner, ref index, out int variableLengthValue))
                        {
                            value = null;
                            return false;
                        }

                        unescaped.Append((char)variableLengthValue);
                        break;
                    default:
                        value = null;
                        return false;
                }
            }

            value = unescaped.ToString();
            return true;
        }

        private static bool TryParseHexDigits(string value, ref int index, int digitCount, out int parsedValue)
        {
            int start = index + 1;
            if (start + digitCount > value.Length)
            {
                parsedValue = 0;
                return false;
            }

            string hex = value.Substring(start, digitCount);
            if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsedValue))
            {
                return false;
            }

            index += digitCount;
            return true;
        }

        private static bool TryParseVariableLengthHexDigits(string value, ref int index, out int parsedValue)
        {
            int start = index + 1;
            int end = AdvanceVariableLengthHexDigits(value, start, 4);
            if (end == start)
            {
                parsedValue = 0;
                return false;
            }

            string hex = value.Substring(start, end - start);
            if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsedValue))
            {
                return false;
            }

            index = end - 1;
            return true;
        }

        private static bool IsValidUtf32CodePoint(int value)
        {
            return value >= 0
                && value <= 0x10FFFF
                && (value < 0xD800 || value > 0xDFFF);
        }

        private static bool IsHexDigit(char value)
        {
            return (value >= '0' && value <= '9')
                || (value >= 'a' && value <= 'f')
                || (value >= 'A' && value <= 'F');
        }
    }

    internal sealed class HoistedLiteralRewriteResult
    {
        public string RewrittenSource { get; }
        public List<HoistedLiteralBinding> Bindings { get; }
        public List<string> DeclarationLines { get; }

        public HoistedLiteralRewriteResult(
            string rewrittenSource,
            List<HoistedLiteralBinding> bindings,
            List<string> declarationLines)
        {
            RewrittenSource = rewrittenSource;
            Bindings = bindings;
            DeclarationLines = declarationLines;
        }
    }
}
