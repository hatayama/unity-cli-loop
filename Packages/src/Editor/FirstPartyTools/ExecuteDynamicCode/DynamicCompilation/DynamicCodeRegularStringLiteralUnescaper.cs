using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Unescapes regular C# string literal tokens into their decoded text values.
    /// </summary>
    internal static class DynamicCodeRegularStringLiteralUnescaper
    {
        private static readonly Dictionary<char, char> RegularStringEscapes = new()
        {
            { '\'', '\'' },
            { '"', '"' },
            { '\\', '\\' },
            { '0', '\0' },
            { 'a', '\a' },
            { 'b', '\b' },
            { 'f', '\f' },
            { 'n', '\n' },
            { 'r', '\r' },
            { 't', '\t' },
            { 'v', '\v' }
        };

        internal static void AdvanceEscapedLiteralSequence(string source, ref int index)
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

        internal static bool TryUnescapeRegularStringLiteral(string token, out string value)
        {
            string inner = token.Substring(1, token.Length - 2);
            StringBuilder unescaped = new(inner.Length);

            for (int index = 0; index < inner.Length; index++)
            {
                char current = inner[index];
                if (current != '\\')
                {
                    unescaped.Append(current);
                    continue;
                }

                if (!TryAppendRegularStringEscape(inner, ref index, unescaped))
                {
                    value = null;
                    return false;
                }
            }

            value = unescaped.ToString();
            return true;
        }

        private static bool TryAppendRegularStringEscape(
            string inner,
            ref int index,
            StringBuilder unescaped)
        {
            if (index + 1 >= inner.Length)
            {
                return false;
            }

            index++;
            char escape = inner[index];
            if (RegularStringEscapes.ContainsKey(escape))
            {
                unescaped.Append(RegularStringEscapes[escape]);
                return true;
            }

            return TryAppendComplexRegularStringEscape(inner, ref index, unescaped, escape);
        }

        private static bool TryAppendComplexRegularStringEscape(
            string inner,
            ref int index,
            StringBuilder unescaped,
            char escape)
        {
            switch (escape)
            {
                case 'u':
                    return TryAppendUnicodeEscape(inner, ref index, unescaped);
                case 'U':
                    return TryAppendUtf32Escape(inner, ref index, unescaped);
                case 'x':
                    return TryAppendVariableLengthHexEscape(inner, ref index, unescaped);
                default:
                    return false;
            }
        }

        private static bool TryAppendUnicodeEscape(
            string inner,
            ref int index,
            StringBuilder unescaped)
        {
            if (!TryParseHexDigits(inner, ref index, 4, out int unicodeValue))
            {
                return false;
            }

            unescaped.Append((char)unicodeValue);
            return true;
        }

        private static bool TryAppendUtf32Escape(
            string inner,
            ref int index,
            StringBuilder unescaped)
        {
            if (!TryParseHexDigits(inner, ref index, 8, out int codePoint) ||
                !IsValidUtf32CodePoint(codePoint))
            {
                return false;
            }

            unescaped.Append(char.ConvertFromUtf32(codePoint));
            return true;
        }

        private static bool TryAppendVariableLengthHexEscape(
            string inner,
            ref int index,
            StringBuilder unescaped)
        {
            if (!TryParseVariableLengthHexDigits(inner, ref index, out int variableLengthValue))
            {
                return false;
            }

            unescaped.Append((char)variableLengthValue);
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
}
