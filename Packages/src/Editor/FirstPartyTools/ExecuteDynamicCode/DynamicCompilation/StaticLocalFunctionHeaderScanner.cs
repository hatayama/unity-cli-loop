using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Detects static local function headers so literal hoisting can skip their bodies.
    /// </summary>
    internal static class StaticLocalFunctionHeaderScanner
    {
        internal static bool TrySkipHeader(
            string source,
            int startIndex,
            out bool isExpressionBody,
            out int headerEndIndex)
        {
            Debug.Assert(source != null, "source must not be null");

            isExpressionBody = false;
            headerEndIndex = startIndex;

            int index = SkipWhitespace(source, startIndex);
            if (!TrySkipReturnTypeAndName(source, ref index))
            {
                return false;
            }

            index = SkipWhitespace(source, index);
            if (index >= source.Length || source[index] != '(')
            {
                return false;
            }

            if (!TrySkipParameterList(source, ref index))
            {
                return false;
            }

            index = SkipWhitespace(source, index);
            if (index + 1 < source.Length && source[index] == '=' && source[index + 1] == '>')
            {
                isExpressionBody = true;
                headerEndIndex = index + 2;
                return true;
            }

            if (index < source.Length && source[index] == '{')
            {
                isExpressionBody = false;
                headerEndIndex = index;
                return true;
            }

            return false;
        }

        private static bool TrySkipReturnTypeAndName(string source, ref int index)
        {
            bool sawName = false;

            while (index < source.Length)
            {
                index = SkipWhitespace(source, index);
                if (index >= source.Length)
                {
                    return false;
                }

                if (source[index] == '(')
                {
                    return sawName;
                }

                if (!TrySkipIdentifier(source, ref index))
                {
                    return false;
                }

                sawName = true;
                index = SkipWhitespace(source, index);
                if (index < source.Length && source[index] == '<')
                {
                    if (!TrySkipGenericArguments(source, ref index))
                    {
                        return false;
                    }
                }

                if (index < source.Length && source[index] == '.')
                {
                    index++;
                }
            }

            return false;
        }

        private static bool TrySkipParameterList(string source, ref int index)
        {
            if (index >= source.Length || source[index] != '(')
            {
                return false;
            }

            int depth = 0;
            while (index < source.Length)
            {
                char current = source[index];
                if (current == '(')
                {
                    depth++;
                }
                else if (current == ')')
                {
                    depth--;
                    if (depth == 0)
                    {
                        index++;
                        return true;
                    }
                }

                index++;
            }

            return false;
        }

        private static bool TrySkipGenericArguments(string source, ref int index)
        {
            if (index >= source.Length || source[index] != '<')
            {
                return false;
            }

            int depth = 0;
            while (index < source.Length)
            {
                char current = source[index];
                if (current == '<')
                {
                    depth++;
                }
                else if (current == '>')
                {
                    depth--;
                    if (depth == 0)
                    {
                        index++;
                        return true;
                    }
                }

                index++;
            }

            return false;
        }

        private static bool TrySkipIdentifier(string source, ref int index)
        {
            if (index >= source.Length)
            {
                return false;
            }

            char first = source[index];
            if (!char.IsLetter(first) && first != '_')
            {
                return false;
            }

            index++;
            while (index < source.Length)
            {
                char current = source[index];
                if (!char.IsLetterOrDigit(current) && current != '_')
                {
                    return true;
                }

                index++;
            }

            return true;
        }

        private static int SkipWhitespace(string source, int index)
        {
            while (index < source.Length && char.IsWhiteSpace(source[index]))
            {
                index++;
            }

            return index;
        }
    }
}
