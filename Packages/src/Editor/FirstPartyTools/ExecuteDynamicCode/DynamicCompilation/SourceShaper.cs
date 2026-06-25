using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
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

            SourceShapeResult result = new();
            int length = source.Length;
            int pos = 0;
            int braceDepth = 0;

            while (pos < length)
            {
                pos = SkipWhitespace(source, pos);
                if (pos >= length) break;

                if (braceDepth == 0)
                {
                    SourceTopLevelStep topLevelStep = AnalyzeTopLevelSourceStep(
                        source,
                        pos,
                        braceDepth,
                        result);
                    pos = topLevelStep.Position;
                    braceDepth = topLevelStep.BraceDepth;
                }
                else
                {
                    pos = AdvanceInsideBlock(source, pos, ref braceDepth);
                }
            }

            return result;
        }

        private static SourceTopLevelStep AnalyzeTopLevelSourceStep(
            string source,
            int pos,
            int braceDepth,
            SourceShapeResult result)
        {
            SourceTopLevelStep? commentStep = TryAnalyzeTopLevelComment(source, pos, braceDepth);
            if (commentStep.HasValue)
            {
                return commentStep.Value;
            }

            SourceTopLevelStep? usingStep = TryAnalyzeTopLevelUsing(source, pos, braceDepth, result);
            if (usingStep.HasValue)
            {
                return usingStep.Value;
            }

            SourceTopLevelStep? declarationStep = TryAnalyzeTopLevelDeclaration(source, pos, braceDepth, result);
            return declarationStep ?? AnalyzeTopLevelStatement(source, pos, braceDepth, result);
        }

        private static SourceTopLevelStep? TryAnalyzeTopLevelComment(
            string source,
            int pos,
            int braceDepth)
        {
            if (TryMatchLineComment(source, pos, out int afterComment))
            {
                return new SourceTopLevelStep(afterComment, braceDepth);
            }

            if (TryMatchBlockComment(source, pos, out int afterBlock))
            {
                return new SourceTopLevelStep(afterBlock, braceDepth);
            }

            return null;
        }

        private static SourceTopLevelStep? TryAnalyzeTopLevelUsing(
            string source,
            int pos,
            int braceDepth,
            SourceShapeResult result)
        {
            if (StartsWithKeyword(source, pos, "global"))
            {
                return TryAnalyzeGlobalUsingDirective(source, pos, braceDepth, result);
            }

            if (!StartsWithKeyword(source, pos, "using"))
            {
                return null;
            }

            int segmentStart = pos;
            int afterUsing = SkipWhitespace(source, pos + 5);
            if (StartsWithKeyword(source, afterUsing, "static"))
            {
                return AddUsingDirectiveStep(source, segmentStart, braceDepth, result);
            }

            // "using var" and "using (" are using-statements, not using-directives
            if (afterUsing < source.Length && (StartsWithKeyword(source, afterUsing, "var") || source[afterUsing] == '('))
            {
                return AnalyzeTopLevelStatement(source, segmentStart, braceDepth, result);
            }

            return AddUsingDirectiveStep(source, segmentStart, braceDepth, result);
        }

        private static SourceTopLevelStep? TryAnalyzeGlobalUsingDirective(
            string source,
            int pos,
            int braceDepth,
            SourceShapeResult result)
        {
            int usingPos = SkipWhitespace(source, pos + 6);
            if (!StartsWithKeyword(source, usingPos, "using"))
            {
                return null;
            }

            return AddUsingDirectiveStep(source, pos, braceDepth, result);
        }

        private static SourceTopLevelStep AddUsingDirectiveStep(
            string source,
            int segmentStart,
            int braceDepth,
            SourceShapeResult result)
        {
            int semiEnd = FindSemicolon(source, segmentStart);
            result.UsingDirectives.Add(source.Substring(segmentStart, semiEnd - segmentStart + 1).TrimEnd());
            return new SourceTopLevelStep(semiEnd + 1, braceDepth);
        }

        private static SourceTopLevelStep? TryAnalyzeTopLevelDeclaration(
            string source,
            int pos,
            int braceDepth,
            SourceShapeResult result)
        {
            if (StartsWithKeyword(source, pos, "namespace"))
            {
                result.HasNamespaceDeclaration = true;
                return SkipTopLevelBlock(source, pos, braceDepth);
            }

            if (IsTypeDeclarationKeyword(source, pos))
            {
                result.HasTypeDeclaration = true;
                return SkipTopLevelBlock(source, pos, braceDepth);
            }

            SourceTopLevelStep? attributedStep = TryAnalyzeAttributedTypeDeclaration(source, pos, braceDepth, result);
            return attributedStep ?? TryAnalyzeModifiedTypeDeclaration(source, pos, braceDepth, result);
        }

        private static SourceTopLevelStep? TryAnalyzeAttributedTypeDeclaration(
            string source,
            int pos,
            int braceDepth,
            SourceShapeResult result)
        {
            if (pos >= source.Length || source[pos] != '[')
            {
                return null;
            }

            int afterAttr = SkipAttributeBlock(source, pos);
            int nextNonWs = SkipWhitespace(source, afterAttr);
            int declarationStart = SkipAccessModifiers(source, nextNonWs);
            if (declarationStart >= source.Length || !IsTypeDeclarationKeyword(source, declarationStart))
            {
                return null;
            }

            result.HasTypeDeclaration = true;
            return SkipTopLevelBlock(source, declarationStart, braceDepth);
        }

        private static SourceTopLevelStep? TryAnalyzeModifiedTypeDeclaration(
            string source,
            int pos,
            int braceDepth,
            SourceShapeResult result)
        {
            if (!IsAccessModifier(source, pos))
            {
                return null;
            }

            int afterMod = SkipAccessModifiers(source, pos);
            if (!IsTypeDeclarationKeyword(source, afterMod))
            {
                return null;
            }

            result.HasTypeDeclaration = true;
            return SkipTopLevelBlock(source, afterMod, braceDepth);
        }

        private static SourceTopLevelStep AnalyzeTopLevelStatement(
            string source,
            int pos,
            int braceDepth,
            SourceShapeResult result)
        {
            result.HasTopLevelStatements = true;
            int nextBraceDepth = braceDepth;
            int stmtEnd = FindStatementEnd(source, pos, ref nextBraceDepth);
            result.TopLevelBodyBuilder.AppendLine(source.Substring(pos, stmtEnd - pos + 1).TrimEnd());
            return new SourceTopLevelStep(stmtEnd + 1, nextBraceDepth);
        }

        private static SourceTopLevelStep SkipTopLevelBlock(string source, int pos, int braceDepth)
        {
            int nextBraceDepth = braceDepth;
            int nextPosition = SkipBlock(source, pos, ref nextBraceDepth);
            return new SourceTopLevelStep(nextPosition, nextBraceDepth);
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

            return WrapperTemplate.Build(shape.UsingDirectives, namespaceName, className, body);
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

        private static bool TryMatchLineComment(string s, int pos, out int afterComment)
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

        private static bool TryMatchBlockComment(string s, int pos, out int afterBlock)
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

        private readonly struct SourceTopLevelStep
        {
            public SourceTopLevelStep(int position, int braceDepth)
            {
                Position = position;
                BraceDepth = braceDepth;
            }

            public int Position { get; }
            public int BraceDepth { get; }
        }
    }

    /// <summary>
    /// Carries the result data produced by Source Shape behavior.
    /// </summary>
    internal sealed class SourceShapeResult
    {
        public List<string> UsingDirectives { get; } = new List<string>();
        public bool HasNamespaceDeclaration { get; set; }
        public bool HasTypeDeclaration { get; set; }
        public bool HasTopLevelStatements { get; set; }
        public StringBuilder TopLevelBodyBuilder { get; } = new StringBuilder();
    }
}
