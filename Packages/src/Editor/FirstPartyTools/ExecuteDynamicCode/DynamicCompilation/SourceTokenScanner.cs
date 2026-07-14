using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Low-level C# source token scanner for skipping strings, comments, and brace-delimited regions.
    /// </summary>
    internal static class SourceTokenScanner
    {
        internal static int SkipWhitespaceAndComments(string source, int pos)
        {
            int current = SourceShaper.SkipWhitespace(source, pos);
            while (true)
            {
                if (TryMatchLineComment(source, current, out int afterLine))
                {
                    current = SourceShaper.SkipWhitespace(source, afterLine);
                    continue;
                }

                if (TryMatchBlockComment(source, current, out int afterBlock))
                {
                    current = SourceShaper.SkipWhitespace(source, afterBlock);
                    continue;
                }

                break;
            }

            return current;
        }

        internal static bool TryMatchLineComment(string s, int pos, out int afterComment)
        {
            afterComment = pos;
            if (pos + 1 < s.Length && s[pos] == '/' && s[pos + 1] == '/')
            {
                int end = pos + 2;
                while (end < s.Length && s[end] != '\n') end++;
                if (end < s.Length) end++; // skip \n
                afterComment = end;
                return true;
            }
            return false;
        }

        internal static bool TryMatchBlockComment(string s, int pos, out int afterBlock)
        {
            afterBlock = pos;
            if (pos + 1 < s.Length && s[pos] == '/' && s[pos + 1] == '*')
            {
                int end = pos + 2;
                while (end + 1 < s.Length && !(s[end] == '*' && s[end + 1] == '/')) end++;
                afterBlock = end + 2 < s.Length ? end + 2 : s.Length;
                return true;
            }
            return false;
        }

        internal static int FindSemicolon(string s, int pos)
        {
            while (pos < s.Length)
            {
                if (s[pos] == ';') return pos;
                pos = AdvanceOneToken(s, pos);
            }
            return s.Length - 1;
        }

        internal static int FindStatementEnd(string s, int pos, ref int braceDepth)
        {
            while (pos < s.Length)
            {
                char c = s[pos];
                if (c == '{')
                {
                    braceDepth++;
                    pos++;
                    while (pos < s.Length && braceDepth > 0)
                    {
                        pos = AdvanceInsideBlock(s, pos, ref braceDepth);
                    }
                    return pos - 1;
                }
                if (c == ';') return pos;
                pos = AdvanceOneToken(s, pos);
            }
            return s.Length - 1;
        }

        internal static int SkipBlock(string s, int pos, ref int braceDepth)
        {
            // Advance past keywords until we hit the opening brace
            while (pos < s.Length && s[pos] != '{')
            {
                if (s[pos] == ';') return pos + 1; // forward declaration
                pos = AdvanceOneToken(s, pos);
            }
            if (pos < s.Length && s[pos] == '{')
            {
                braceDepth++;
                pos++;
                while (pos < s.Length && braceDepth > 0)
                {
                    pos = AdvanceInsideBlock(s, pos, ref braceDepth);
                }
            }
            return pos;
        }

        internal static int SkipAttributeBlock(string s, int pos)
        {
            Debug.Assert(s[pos] == '[', "SkipAttributeBlock must start at '['");
            int depth = 1;
            pos++;
            while (pos < s.Length && depth > 0)
            {
                if (s[pos] == '[') depth++;
                else if (s[pos] == ']') depth--;
                else pos = AdvanceOneToken(s, pos) - 1; // -1 because loop will pos++ via fallthrough
                pos++;
            }
            return pos;
        }

        internal static int AdvanceInsideBlock(string s, int pos, ref int braceDepth)
        {
            char c = s[pos];
            if (c == '{') { braceDepth++; return pos + 1; }
            if (c == '}') { braceDepth--; return pos + 1; }
            return AdvanceOneToken(s, pos);
        }

        internal static int AdvanceOneToken(string s, int pos)
        {
            if (pos >= s.Length) return s.Length;

            (bool matched, int nextPosition) = TryAdvanceLineComment(s, pos);
            if (matched) return nextPosition;
            (matched, nextPosition) = TryAdvanceBlockComment(s, pos);
            if (matched) return nextPosition;
            (matched, nextPosition) = TryAdvanceVerbatimString(s, pos);
            if (matched) return nextPosition;
            (matched, nextPosition) = TryAdvanceRawString(s, pos);
            if (matched) return nextPosition;
            (matched, nextPosition) = TryAdvanceRegularString(s, pos);
            if (matched) return nextPosition;
            (matched, nextPosition) = TryAdvanceCharLiteral(s, pos);
            if (matched) return nextPosition;
            (matched, nextPosition) = TryAdvanceInterpolatedString(s, pos);
            if (matched) return nextPosition;

            return pos + 1;
        }

        private static (bool Matched, int NextPosition) TryAdvanceLineComment(string s, int pos)
        {
            if (pos + 1 >= s.Length || s[pos] != '/' || s[pos + 1] != '/')
            {
                return (false, pos);
            }

            int end = pos + 2;
            while (end < s.Length && s[end] != '\n') end++;
            return (true, end < s.Length ? end + 1 : s.Length);
        }

        private static (bool Matched, int NextPosition) TryAdvanceBlockComment(string s, int pos)
        {
            if (pos + 1 >= s.Length || s[pos] != '/' || s[pos + 1] != '*')
            {
                return (false, pos);
            }

            int end = pos + 2;
            while (end + 1 < s.Length && !(s[end] == '*' && s[end + 1] == '/')) end++;
            return (true, end + 2 < s.Length ? end + 2 : s.Length);
        }

        private static (bool Matched, int NextPosition) TryAdvanceVerbatimString(string s, int pos)
        {
            if (pos + 1 >= s.Length || s[pos] != '@' || s[pos + 1] != '"')
            {
                return (false, pos);
            }

            return (true, SkipVerbatimString(s, pos + 2));
        }

        private static (bool Matched, int NextPosition) TryAdvanceRawString(string s, int pos)
        {
            if (pos + 2 >= s.Length || s[pos] != '"' || s[pos + 1] != '"' || s[pos + 2] != '"')
            {
                return (false, pos);
            }

            return (true, SkipRawString(s, pos + 3));
        }

        private static (bool Matched, int NextPosition) TryAdvanceRegularString(string s, int pos)
        {
            if (s[pos] != '"')
            {
                return (false, pos);
            }

            return (true, SkipRegularString(s, pos + 1));
        }

        private static (bool Matched, int NextPosition) TryAdvanceCharLiteral(string s, int pos)
        {
            if (s[pos] != '\'')
            {
                return (false, pos);
            }

            return (true, SkipCharLiteral(s, pos + 1));
        }

        private static (bool Matched, int NextPosition) TryAdvanceInterpolatedString(string s, int pos)
        {
            if (pos + 1 >= s.Length || s[pos] != '$' || s[pos + 1] != '"')
            {
                return (false, pos);
            }

            return (true, SkipInterpolatedString(s, pos + 2));
        }

        private static int SkipInterpolatedString(string s, int pos)
        {
            int end = pos;
            while (end < s.Length)
            {
                if (s[end] == '\\')
                {
                    end += 2;
                    continue;
                }

                if (s[end] == '{')
                {
                    if (end + 1 < s.Length && s[end + 1] == '{')
                    {
                        end += 2;
                        continue;
                    }

                    end = SkipInterpolationHole(s, end + 1);
                    continue;
                }

                if (s[end] == '"' && end + 1 < s.Length && s[end + 1] == '"')
                {
                    end += 2;
                    continue;
                }

                if (s[end] == '"')
                {
                    return end + 1;
                }

                end++;
            }

            return s.Length;
        }

        private static int SkipInterpolationHole(string s, int pos)
        {
            int depth = 1;
            int end = pos;

            while (end < s.Length && depth > 0)
            {
                int afterLiteral = SkipInterpolationHoleLiteral(s, end);
                if (afterLiteral != end)
                {
                    end = afterLiteral;
                    continue;
                }

                if (s[end] == '{')
                {
                    depth++;
                    end++;
                    continue;
                }

                if (s[end] == '}')
                {
                    depth--;
                    end++;
                    continue;
                }

                end++;
            }

            return end;
        }

        private static int SkipInterpolationHoleLiteral(string s, int end)
        {
            if (s[end] == '\\')
            {
                return end + 2;
            }

            if (s[end] == '@' && end + 1 < s.Length && s[end + 1] == '"')
            {
                return SkipVerbatimString(s, end + 2);
            }

            if (s[end] == '$' && end + 1 < s.Length && s[end + 1] == '"')
            {
                return SkipInterpolatedString(s, end + 2);
            }

            if (s[end] == '"' && end + 2 < s.Length && s[end + 1] == '"' && s[end + 2] == '"')
            {
                return SkipRawString(s, end + 3);
            }

            if (s[end] == '"')
            {
                return SkipRegularString(s, end + 1);
            }

            return s[end] == '\'' ? SkipCharLiteral(s, end + 1) : end;
        }

        private static int SkipRegularString(string s, int pos)
        {
            int end = pos;
            while (end < s.Length && s[end] != '"')
            {
                if (s[end] == '\\')
                {
                    end++;
                }

                end++;
            }

            return end < s.Length ? end + 1 : s.Length;
        }

        private static int SkipVerbatimString(string s, int pos)
        {
            int end = pos;
            while (end < s.Length)
            {
                if (s[end] == '"')
                {
                    if (end + 1 < s.Length && s[end + 1] == '"')
                    {
                        end += 2;
                        continue;
                    }

                    return end + 1;
                }

                end++;
            }

            return s.Length;
        }

        private static int SkipRawString(string s, int pos)
        {
            int end = pos;
            while (end + 2 < s.Length)
            {
                if (s[end] == '"' && s[end + 1] == '"' && s[end + 2] == '"')
                {
                    return end + 3;
                }

                end++;
            }

            return s.Length;
        }

        private static int SkipCharLiteral(string s, int pos)
        {
            int end = pos;
            while (end < s.Length && s[end] != '\'')
            {
                if (s[end] == '\\')
                {
                    end++;
                }

                end++;
            }

            return end < s.Length ? end + 1 : s.Length;
        }
    }
}
