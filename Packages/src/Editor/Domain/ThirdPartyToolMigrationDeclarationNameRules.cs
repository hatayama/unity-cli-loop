using System;
using System.Collections.Generic;
using System.Diagnostics;

using CodeTextMask = io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationParsingRules.CodeTextMask;
using static io.github.hatayama.UnityCliLoop.Domain.ThirdPartyToolMigrationParsingRules;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Decides whether an identifier in a class body declares a name there or uses one, for the type hierarchy index.
    /// </summary>
    public static class ThirdPartyToolMigrationDeclarationNameRules
    {
        // Words after which an identifier is in an expression, so it is a use of a name rather than a declaration.
        // Type keywords and modifiers (var, void, int, static, override, ...) are left out on purpose: a name after
        // them is declared, and an unknown word must count as a declaration so the walk stays on the unchanged side.
        private static readonly HashSet<string> ExpressionKeywords = new(StringComparer.Ordinal)
        {
            "return", "await", "new", "else", "throw", "yield", "in", "is", "as", "case", "when",
            "out", "ref", "typeof", "sizeof", "nameof", "default", "not", "and", "or", "goto", "using", "lock", "fixed",
            "checked", "unchecked", "this", "base", "select", "where", "orderby", "by", "on", "equals", "ascending",
            "descending",
        };

        /// <summary>
        /// Decides whether the identifier at the index is declared there. Only positions that are certainly
        /// expressions count as uses; anything unclear counts as a declaration so an inherited call is left unchanged.
        /// </summary>
        public static bool IsDeclarationName(
            string source,
            CodeTextMask codeTextMask,
            int identifierStartIndex,
            int parenDepth)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(identifierStartIndex >= 0, "identifierStartIndex must not be negative");

            if (IsLambdaParameterName(source, codeTextMask, identifierStartIndex))
            {
                return true;
            }

            int previousIndex = ReadPreviousCodeIndex(source, codeTextMask, identifierStartIndex - 1);
            if (previousIndex < 0)
            {
                return true;
            }

            char previous = source[previousIndex];
            if (previous == '.')
            {
                // x.Run or an explicit interface implementation IFoo.Run: not a name this type can call unqualified.
                return false;
            }

            if (IsIdentifierCharacter(previous))
            {
                return !ExpressionKeywords.Contains(ReadIdentifierEndingAt(source, codeTextMask, previousIndex));
            }

            return IsDeclarationAfterPunctuation(source, codeTextMask, previousIndex, parenDepth);
        }

        private static bool IsDeclarationAfterPunctuation(
            string source,
            CodeTextMask codeTextMask,
            int previousIndex,
            int parenDepth)
        {
            char previous = source[previousIndex];
            switch (previous)
            {
                case '>':
                    // "=> Run" is an expression; "Task<int> Run" is a type. A comparison cannot be told apart.
                    return !(previousIndex > 0 && source[previousIndex - 1] == '=');
                case ']':
                    return true;
                case '?':
                    // "int? Run" and "(int, int)? Run" are types; the conditional "x ? Run" has a space before '?'.
                    return previousIndex > 0 && IsNullableTypeSuffixOwner(source[previousIndex - 1]);
                case ')':
                    return IsTupleTypeClose(source, codeTextMask, previousIndex);
                case ',':
                    // Outside brackets a comma separates declarators; inside them it separates arguments.
                    return parenDepth == 0;
                default:
                    return !IsExpressionPunctuation(previous);
            }
        }

        // "Run => ..", or "(Run, x) => .." where the name opens or continues a parenthesized parameter list.
        // Foo(Run, x); is still a use: its ')' is not followed by "=>".
        private static bool IsLambdaParameterName(string source, CodeTextMask codeTextMask, int identifierStartIndex)
        {
            int identifierEndIndex = ReadIdentifierEndIndex(source, identifierStartIndex);
            if (IsArrowAt(source, codeTextMask, ReadNextCodeIndex(source, codeTextMask, identifierEndIndex)))
            {
                return true;
            }

            int previousIndex = ReadPreviousCodeIndex(source, codeTextMask, identifierStartIndex - 1);
            if (previousIndex < 0 || (source[previousIndex] != '(' && source[previousIndex] != ','))
            {
                return false;
            }

            int closeIndex = FindListCloseParenthesisIndex(source, codeTextMask, identifierEndIndex);
            return closeIndex >= 0 &&
                   IsArrowAt(source, codeTextMask, ReadNextCodeIndex(source, codeTextMask, closeIndex + 1));
        }

        // Finds the ')' that closes the list the index is in, or -1 when the statement or block ends first.
        private static int FindListCloseParenthesisIndex(string source, CodeTextMask codeTextMask, int startIndex)
        {
            int depth = 0;
            for (int index = startIndex; index < source.Length; index++)
            {
                if (!codeTextMask.IsCodeAt(index))
                {
                    continue;
                }

                char character = source[index];
                if (character == '(' || character == '[')
                {
                    depth++;
                }
                else if (character == ')' || character == ']')
                {
                    if (depth == 0)
                    {
                        return character == ')' ? index : -1;
                    }

                    depth--;
                }
                else if (character == ';' || character == '{' || character == '}')
                {
                    return -1;
                }
            }

            return -1;
        }

        private static bool IsArrowAt(string source, CodeTextMask codeTextMask, int index)
        {
            return index >= 0 &&
                   index + 1 < source.Length &&
                   source[index] == '=' &&
                   source[index + 1] == '>' &&
                   codeTextMask.IsCodeAt(index + 1);
        }

        internal static bool IsNullableTypeSuffixOwner(char character)
        {
            return IsIdentifierCharacter(character) || character == '>' || character == ']' || character == ')';
        }

        private static bool IsExpressionPunctuation(char character)
        {
            return "([=;{}!&|^+-/%~:<".IndexOf(character) >= 0;
        }

        // A ')' closes a tuple type when its parentheses hold a top-level comma and no top-level ';' or '='.
        // "if (x) Run(..)" and "(T)Run(..)" have no comma, so they stay expressions.
        private static bool IsTupleTypeClose(string source, CodeTextMask codeTextMask, int closeIndex)
        {
            int depth = 0;
            bool hasTopLevelComma = false;
            for (int index = closeIndex - 1; index >= 0; index--)
            {
                if (!codeTextMask.IsCodeAt(index))
                {
                    continue;
                }

                char character = source[index];
                if (character == ')' || character == ']')
                {
                    depth++;
                    continue;
                }

                if (character == '(' || character == '[')
                {
                    if (depth == 0)
                    {
                        return hasTopLevelComma;
                    }

                    depth--;
                    continue;
                }

                if (depth > 0)
                {
                    continue;
                }

                if (character == ';' || character == '=')
                {
                    return false;
                }

                if (character == '{' || character == '}')
                {
                    // The open parenthesis cannot be found within the statement, so the shape is unknown.
                    return true;
                }

                hasTopLevelComma |= character == ',';
            }

            return true;
        }

        internal static int ReadPreviousCodeIndex(string source, CodeTextMask codeTextMask, int startIndex)
        {
            int index = startIndex;
            while (index >= 0 && (!codeTextMask.IsCodeAt(index) || char.IsWhiteSpace(source[index])))
            {
                index--;
            }

            return index;
        }

        private static int ReadNextCodeIndex(string source, CodeTextMask codeTextMask, int startIndex)
        {
            int index = startIndex;
            while (index < source.Length && (!codeTextMask.IsCodeAt(index) || char.IsWhiteSpace(source[index])))
            {
                index++;
            }

            return index < source.Length ? index : -1;
        }

        private static string ReadIdentifierEndingAt(string source, CodeTextMask codeTextMask, int endIndex)
        {
            int startIndex = endIndex;
            while (startIndex > 0 &&
                   codeTextMask.IsCodeAt(startIndex - 1) &&
                   IsIdentifierCharacter(source[startIndex - 1]))
            {
                startIndex--;
            }

            return source.Substring(startIndex, endIndex - startIndex + 1);
        }

        internal static int ReadIdentifierEndIndex(string source, int identifierStartIndex)
        {
            int index = identifierStartIndex;
            while (index < source.Length && IsIdentifierCharacter(source[index]))
            {
                index++;
            }

            return index;
        }
    }
}
