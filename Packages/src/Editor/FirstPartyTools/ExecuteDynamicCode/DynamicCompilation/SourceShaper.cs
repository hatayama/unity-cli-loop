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
                    pos = SourceTokenScanner.AdvanceInsideBlock(source, pos, ref braceDepth);
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
            if (SourceTokenScanner.TryMatchLineComment(source, pos, out int afterComment))
            {
                return new SourceTopLevelStep(afterComment, braceDepth);
            }

            if (SourceTokenScanner.TryMatchBlockComment(source, pos, out int afterBlock))
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
            int afterUsing = SourceTokenScanner.SkipWhitespaceAndComments(source, pos + 5);
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
            int usingPos = SourceTokenScanner.SkipWhitespaceAndComments(source, pos + 6);
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
            int semiEnd = SourceTokenScanner.FindSemicolon(source, segmentStart);
            RegisterUsingDirective(result, source, segmentStart, semiEnd);
            return new SourceTopLevelStep(semiEnd + 1, braceDepth);
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

        // Recognizes "using Name = ...", "using @Name = ...", and their "global using" variants
        // so WrapperTemplate can skip injecting a default alias the user's code already defines.
        private static string ExtractUsingAliasName(string source, int segmentStart, int semiEnd)
        {
            int position = segmentStart;
            if (StartsWithKeyword(source, position, "global"))
            {
                position = SourceTokenScanner.SkipWhitespaceAndComments(source, position + "global".Length);
            }

            if (!StartsWithKeyword(source, position, "using"))
            {
                return null;
            }

            position = SourceTokenScanner.SkipWhitespaceAndComments(source, position + "using".Length);
            if (StartsWithKeyword(source, position, "static"))
            {
                return null;
            }

            (string Name, int EndPosition) aliasName = ReadAliasName(source, position, semiEnd);
            if (aliasName.Name == null)
            {
                return null;
            }

            int equalsPosition = SourceTokenScanner.SkipWhitespaceAndComments(source, aliasName.EndPosition);
            if (equalsPosition > semiEnd || source[equalsPosition] != '=')
            {
                return null;
            }

            return aliasName.Name;
        }

        private static (string Name, int EndPosition) ReadAliasName(string source, int position, int semiEnd)
        {
            int currentPosition = position;
            if (currentPosition <= semiEnd && source[currentPosition] == '@')
            {
                currentPosition++;
            }

            if (currentPosition > semiEnd || !IsIdentifierStart(source[currentPosition]))
            {
                return (null, position);
            }

            int nameStart = currentPosition;
            currentPosition++;
            while (currentPosition <= semiEnd && IsIdentifierPart(source[currentPosition]))
            {
                currentPosition++;
            }

            return (source.Substring(nameStart, currentPosition - nameStart), currentPosition);
        }

        private static bool IsIdentifierStart(char value)
        {
            return char.IsLetter(value) || value == '_';
        }

        private static bool IsIdentifierPart(char value)
        {
            return char.IsLetterOrDigit(value) || value == '_';
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

            int afterAttr = SourceTokenScanner.SkipAttributeBlock(source, pos);
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
            int stmtEnd = SourceTokenScanner.FindStatementEnd(source, pos, ref nextBraceDepth);
            int originalLineNumber1Based = GetLineNumber1Based(source, pos);
            PadTopLevelBodyBuilderToOriginalLine(result, originalLineNumber1Based);
            // Multi-line statements are copied verbatim, so CRLF input would leak
            // platform line endings into the emitted body unless normalized here.
            string statementText = source.Substring(pos, stmtEnd - pos + 1).TrimEnd()
                .Replace("\r\n", "\n")
                .Replace('\r', '\n');
            result.TopLevelBodyBuilder.Append(statementText);
            result.TopLevelBodyBuilder.Append('\n');
            result.NextBodyLineNumber1Based = originalLineNumber1Based + CountLinesInText(statementText);
            return new SourceTopLevelStep(stmtEnd + 1, nextBraceDepth);
        }

        private static void PadTopLevelBodyBuilderToOriginalLine(
            SourceShapeResult result,
            int originalLineNumber1Based)
        {
            while (result.NextBodyLineNumber1Based < originalLineNumber1Based)
            {
                result.TopLevelBodyBuilder.Append('\n');
                result.NextBodyLineNumber1Based++;
            }
        }

        private static int GetLineNumber1Based(string source, int index)
        {
            int lineNumber = 1;
            for (int position = 0; position < index && position < source.Length; position++)
            {
                // A standalone CR is a line break too; CRLF must count as one break,
                // matching the LF-normalized text the body builder emits.
                if (source[position] == '\r')
                {
                    lineNumber++;
                    if (position + 1 < index && source[position + 1] == '\n')
                    {
                        position++;
                    }
                }
                else if (source[position] == '\n')
                {
                    lineNumber++;
                }
            }

            return lineNumber;
        }

        private static int CountLinesInText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            int lineCount = 1;
            for (int index = 0; index < text.Length; index++)
            {
                if (text[index] == '\n')
                {
                    lineCount++;
                }
            }

            return lineCount;
        }

        private static SourceTopLevelStep SkipTopLevelBlock(string source, int pos, int braceDepth)
        {
            int nextBraceDepth = braceDepth;
            int nextPosition = SourceTokenScanner.SkipBlock(source, pos, ref nextBraceDepth);
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

            return WrapperTemplate.Build(shape.UsingDirectives, shape.AliasedNames, namespaceName, className, body);
        }

        internal static int SkipWhitespace(string s, int pos)
        {
            return SourceTokenScanner.SkipWhitespace(s, pos);
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

        internal static int AdvanceOneTokenPublic(string s, int pos)
        {
            return SourceTokenScanner.AdvanceOneToken(s, pos);
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
        public int NextBodyLineNumber1Based { get; set; } = 1;
        public HashSet<string> AliasedNames { get; } = new HashSet<string>(System.StringComparer.Ordinal);
        public bool HasNamespaceDeclaration { get; set; }
        public bool HasTypeDeclaration { get; set; }
        public bool HasTopLevelStatements { get; set; }
        public StringBuilder TopLevelBodyBuilder { get; } = new StringBuilder();
    }
}
