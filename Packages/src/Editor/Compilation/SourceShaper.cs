using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Lightweight C# source scanner that determines top-level structure without Roslyn.
    /// Tracks string literals, comments, and brace depth to classify source into one of three modes.
    /// </summary>
    internal static class SourceShaper
    {
        public static SourceShapeResult Analyze(string source)
        {
            Debug.Assert(source != null, "source must not be null");

            SourceShapeResult result = new SourceShapeResult();
            int length = source.Length;
            int pos = 0;
            int braceDepth = 0;

            while (pos < length)
            {
                pos = SkipWhitespace(source, pos);
                if (pos >= length) break;

                if (braceDepth == 0)
                {
                    (bool Matched, int NextPosition) commentMatch = TryMatchComment(source, pos);
                    if (commentMatch.Matched)
                    {
                        pos = commentMatch.NextPosition;
                        continue;
                    }

                    if (StartsWithKeyword(source, pos, "using"))
                    {
                        int segmentStart = pos;
                        int afterUsing = pos + 5;
                        afterUsing = SkipWhitespaceAndComments(source, afterUsing);

                        if (StartsWithKeyword(source, afterUsing, "static"))
                        {
                            int end = FindSemicolon(source, segmentStart);
                            RegisterUsingDirective(result, source, segmentStart, end);
                            pos = end + 1;
                            continue;
                        }

                        // "using var" and "using (" are using-statements, not using-directives
                        if (afterUsing < length && (StartsWithKeyword(source, afterUsing, "var") || source[afterUsing] == '('))
                        {
                            result.HasTopLevelStatements = true;
                            int end = FindStatementEnd(source, segmentStart, ref braceDepth);
                            result.TopLevelBodyBuilder.AppendLine(source.Substring(segmentStart, end - segmentStart + 1).TrimEnd());
                            pos = end + 1;
                            continue;
                        }

                        int semiEnd = FindSemicolon(source, segmentStart);
                        RegisterUsingDirective(result, source, segmentStart, semiEnd);
                        pos = semiEnd + 1;
                        continue;
                    }

                    if (StartsWithKeyword(source, pos, "namespace"))
                    {
                        result.HasNamespaceDeclaration = true;
                        pos = SkipBlock(source, pos, ref braceDepth);
                        continue;
                    }

                    if (IsTypeDeclarationKeyword(source, pos))
                    {
                        result.HasTypeDeclaration = true;
                        pos = SkipBlock(source, pos, ref braceDepth);
                        continue;
                    }

                    if (StartsWithKeyword(source, pos, "global") &&
                        StartsWithKeyword(source, SkipWhitespaceAndComments(source, pos + "global".Length), "using"))
                    {
                        int segmentStart = pos;
                        int semiEnd = FindSemicolon(source, segmentStart);
                        RegisterUsingDirective(result, source, segmentStart, semiEnd);
                        pos = semiEnd + 1;
                        continue;
                    }

                    if (pos < length && source[pos] == '[')
                    {
                        int afterAttr = SkipAttributeBlock(source, pos);
                        int nextNonWs = SkipWhitespace(source, afterAttr);
                        if (nextNonWs < length && IsTypeDeclarationKeyword(source, nextNonWs))
                        {
                            result.HasTypeDeclaration = true;
                            pos = SkipBlock(source, nextNonWs, ref braceDepth);
                            continue;
                        }
                    }

                    if (IsAccessModifier(source, pos))
                    {
                        int afterMod = SkipAccessModifiers(source, pos);
                        if (IsTypeDeclarationKeyword(source, afterMod))
                        {
                            result.HasTypeDeclaration = true;
                            pos = SkipBlock(source, afterMod, ref braceDepth);
                            continue;
                        }
                    }

                    result.HasTopLevelStatements = true;
                    int stmtStart = pos;
                    int stmtEnd = FindStatementEnd(source, stmtStart, ref braceDepth);
                    result.TopLevelBodyBuilder.AppendLine(source.Substring(stmtStart, stmtEnd - stmtStart + 1).TrimEnd());
                    pos = stmtEnd + 1;
                }
                else
                {
                    pos = AdvanceInsideBlock(source, pos, ref braceDepth);
                }
            }

            return result;
        }

        public static string WrapIfNeeded(string source, string namespaceName, string className)
        {
            SourceShapeResult shape = Analyze(source);

            // Raw mode: namespace or type declaration without top-level statements → pass through
            if ((shape.HasNamespaceDeclaration || shape.HasTypeDeclaration) && !shape.HasTopLevelStatements)
            {
                return source;
            }

            // Mixed mode: both type declarations and top-level statements → error
            if ((shape.HasNamespaceDeclaration || shape.HasTypeDeclaration) && shape.HasTopLevelStatements)
            {
                return null;
            }

            // Script mode: wrap top-level statements
            string body = shape.TopLevelBodyBuilder.ToString().TrimEnd();

            bool hasReturn = TopLevelReturnDetector.HasTopLevelReturn(body);
            if (!hasReturn)
            {
                body = string.IsNullOrWhiteSpace(body)
                    ? "return null;"
                    : body + "\nreturn null;";
            }

            return WrapperTemplate.Build(shape.UsingDirectives, shape.AliasedNames, namespaceName, className, body);
        }

        internal static int SkipWhitespace(string s, int pos)
        {
            Debug.Assert(s != null, "s must not be null");
            Debug.Assert(pos >= 0, "pos must be non-negative");
            while (pos < s.Length && char.IsWhiteSpace(s[pos])) pos++;
            return pos;
        }

        internal static bool StartsWithKeyword(string s, int pos, string keyword)
        {
            Debug.Assert(s != null, "s must not be null");
            Debug.Assert(keyword != null, "keyword must not be null");
            Debug.Assert(pos >= 0, "pos must be non-negative");
            if (pos + keyword.Length > s.Length) return false;
            for (int i = 0; i < keyword.Length; i++)
            {
                if (s[pos + i] != keyword[i]) return false;
            }
            // Keyword must be followed by non-identifier char
            int afterPos = pos + keyword.Length;
            if (afterPos < s.Length && (char.IsLetterOrDigit(s[afterPos]) || s[afterPos] == '_'))
            {
                return false;
            }
            return true;
        }

        private static void RegisterUsingDirective(
            SourceShapeResult result,
            string source,
            int segmentStart,
            int semiEnd)
        {
            result.UsingDirectives.Add(source.Substring(segmentStart, semiEnd - segmentStart + 1).TrimEnd());

            string aliasName = ExtractUsingAliasName(source, segmentStart, semiEnd);
            if (!string.IsNullOrEmpty(aliasName))
            {
                result.AliasedNames.Add(aliasName);
            }
        }

        private static string ExtractUsingAliasName(string source, int segmentStart, int semiEnd)
        {
            int position = segmentStart;
            if (StartsWithKeyword(source, position, "global"))
            {
                position = SkipWhitespaceAndComments(source, position + "global".Length);
            }

            if (!StartsWithKeyword(source, position, "using"))
            {
                return null;
            }

            position = SkipWhitespaceAndComments(source, position + "using".Length);
            if (StartsWithKeyword(source, position, "static"))
            {
                return null;
            }

            AliasNameParseResult aliasName = ReadAliasName(source, position, semiEnd);
            if (aliasName.Name == null)
            {
                return null;
            }

            int equalsPosition = SkipWhitespaceAndComments(source, aliasName.EndPosition);
            if (equalsPosition > semiEnd || source[equalsPosition] != '=')
            {
                return null;
            }

            return aliasName.Name;
        }

        private static AliasNameParseResult ReadAliasName(string source, int position, int semiEnd)
        {
            int currentPosition = position;
            if (currentPosition <= semiEnd && source[currentPosition] == '@')
            {
                currentPosition++;
            }

            if (currentPosition > semiEnd || !IsIdentifierStart(source[currentPosition]))
            {
                return new AliasNameParseResult(null, position);
            }

            int nameStart = currentPosition;
            currentPosition++;
            while (currentPosition <= semiEnd && IsIdentifierPart(source[currentPosition]))
            {
                currentPosition++;
            }

            return new AliasNameParseResult(
                source.Substring(nameStart, currentPosition - nameStart),
                currentPosition);
        }

        private static bool IsIdentifierStart(char value)
        {
            return char.IsLetter(value) || value == '_';
        }

        private static bool IsIdentifierPart(char value)
        {
            return char.IsLetterOrDigit(value) || value == '_';
        }

        private static int SkipWhitespaceAndComments(string source, int position)
        {
            int currentPosition = SkipWhitespace(source, position);
            while (true)
            {
                (bool Matched, int NextPosition) commentMatch = TryMatchComment(source, currentPosition);
                if (!commentMatch.Matched)
                {
                    break;
                }

                currentPosition = SkipWhitespace(source, commentMatch.NextPosition);
            }

            return currentPosition;
        }

        private static bool IsTypeDeclarationKeyword(string s, int pos)
        {
            return StartsWithKeyword(s, pos, "class") ||
                   StartsWithKeyword(s, pos, "struct") ||
                   StartsWithKeyword(s, pos, "interface") ||
                   StartsWithKeyword(s, pos, "enum") ||
                   StartsWithKeyword(s, pos, "record");
        }

        private static bool IsAccessModifier(string s, int pos)
        {
            return StartsWithKeyword(s, pos, "public") ||
                   StartsWithKeyword(s, pos, "internal") ||
                   StartsWithKeyword(s, pos, "private") ||
                   StartsWithKeyword(s, pos, "protected") ||
                   StartsWithKeyword(s, pos, "static") ||
                   StartsWithKeyword(s, pos, "sealed") ||
                   StartsWithKeyword(s, pos, "abstract") ||
                   StartsWithKeyword(s, pos, "partial");
        }

        private static int SkipAccessModifiers(string s, int pos)
        {
            while (pos < s.Length && IsAccessModifier(s, pos))
            {
                int wordEnd = pos;
                while (wordEnd < s.Length && (char.IsLetterOrDigit(s[wordEnd]) || s[wordEnd] == '_')) wordEnd++;
                pos = SkipWhitespace(s, wordEnd);
            }
            return pos;
        }

        private static (bool Matched, int NextPosition) TryMatchComment(string s, int pos)
        {
            if (pos + 1 >= s.Length || s[pos] != '/')
            {
                return (false, pos);
            }

            if (s[pos + 1] == '/')
            {
                int end = pos + 2;
                while (end < s.Length && s[end] != '\n') end++;
                if (end < s.Length) end++; // skip \n
                return (true, end);
            }

            if (s[pos + 1] == '*')
            {
                int end = pos + 2;
                while (end + 1 < s.Length && !(s[end] == '*' && s[end + 1] == '/')) end++;
                return (true, end + 2 < s.Length ? end + 2 : s.Length);
            }

            return (false, pos);
        }

        private static int FindSemicolon(string s, int pos)
        {
            while (pos < s.Length)
            {
                if (s[pos] == ';') return pos;
                pos = AdvanceOneToken(s, pos);
            }
            return s.Length - 1;
        }

        private static int FindStatementEnd(string s, int pos, ref int braceDepth)
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

        private static int SkipBlock(string s, int pos, ref int braceDepth)
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

        private static int SkipAttributeBlock(string s, int pos)
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

        private static int AdvanceInsideBlock(string s, int pos, ref int braceDepth)
        {
            char c = s[pos];
            if (c == '{') { braceDepth++; return pos + 1; }
            if (c == '}') { braceDepth--; return pos + 1; }
            return AdvanceOneToken(s, pos);
        }

        internal static int AdvanceOneTokenPublic(string s, int pos)
        {
            return AdvanceOneToken(s, pos);
        }

        private static int AdvanceOneToken(string s, int pos)
        {
            if (pos >= s.Length) return s.Length;
            char c = s[pos];

            (bool Matched, int NextPosition) commentMatch = TryMatchComment(s, pos);
            if (commentMatch.Matched)
            {
                return commentMatch.NextPosition;
            }

            // Verbatim string (@"...")
            if (c == '@' && pos + 1 < s.Length && s[pos + 1] == '"')
            {
                int end = pos + 2;
                while (end < s.Length)
                {
                    if (s[end] == '"')
                    {
                        if (end + 1 < s.Length && s[end + 1] == '"') { end += 2; continue; }
                        return end + 1;
                    }
                    end++;
                }
                return s.Length;
            }

            // Raw string literal (""" ... """)
            if (c == '"' && pos + 2 < s.Length && s[pos + 1] == '"' && s[pos + 2] == '"')
            {
                int end = pos + 3;
                while (end + 2 < s.Length)
                {
                    if (s[end] == '"' && s[end + 1] == '"' && s[end + 2] == '"') return end + 3;
                    end++;
                }
                return s.Length;
            }

            // Regular string literal ("...")
            if (c == '"')
            {
                int end = pos + 1;
                while (end < s.Length && s[end] != '"')
                {
                    if (s[end] == '\\') end++; // skip escaped char
                    end++;
                }
                return end < s.Length ? end + 1 : s.Length;
            }

            // Char literal ('x')
            if (c == '\'')
            {
                int end = pos + 1;
                while (end < s.Length && s[end] != '\'')
                {
                    if (s[end] == '\\') end++;
                    end++;
                }
                return end < s.Length ? end + 1 : s.Length;
            }

            // Interpolated string ($"...")
            if (c == '$' && pos + 1 < s.Length && s[pos + 1] == '"')
            {
                return SkipInterpolatedString(s, pos + 2);
            }

            return pos + 1;
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
                if (s[end] == '\\')
                {
                    end += 2;
                    continue;
                }

                if (s[end] == '@' && end + 1 < s.Length && s[end + 1] == '"')
                {
                    end = SkipVerbatimString(s, end + 2);
                    continue;
                }

                if (s[end] == '$' && end + 1 < s.Length && s[end + 1] == '"')
                {
                    end = SkipInterpolatedString(s, end + 2);
                    continue;
                }

                if (s[end] == '"' && end + 2 < s.Length && s[end + 1] == '"' && s[end + 2] == '"')
                {
                    end = SkipRawString(s, end + 3);
                    continue;
                }

                if (s[end] == '"')
                {
                    end = SkipRegularString(s, end + 1);
                    continue;
                }

                if (s[end] == '\'')
                {
                    end = SkipCharLiteral(s, end + 1);
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

        private sealed class AliasNameParseResult
        {
            public string Name { get; }

            public int EndPosition { get; }

            public AliasNameParseResult(string name, int endPosition)
            {
                Name = name;
                EndPosition = endPosition;
            }
        }
    }

    internal sealed class SourceShapeResult
    {
        public List<string> UsingDirectives { get; } = new List<string>();
        public HashSet<string> AliasedNames { get; } = new HashSet<string>(System.StringComparer.Ordinal);
        public bool HasNamespaceDeclaration { get; set; }
        public bool HasTypeDeclaration { get; set; }
        public bool HasTopLevelStatements { get; set; }
        public StringBuilder TopLevelBodyBuilder { get; } = new StringBuilder();
    }
}
