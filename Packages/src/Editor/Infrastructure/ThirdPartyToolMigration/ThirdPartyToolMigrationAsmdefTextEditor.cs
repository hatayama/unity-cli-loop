using System;
using System.Diagnostics;
using System.Text;

using Newtonsoft.Json;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Replaces only the top-level "references" array inside asmdef JSON text so every byte
    /// outside that array (indentation, line endings, trailing newline, other properties)
    /// is preserved exactly.
    /// </summary>
    internal static class ThirdPartyToolMigrationAsmdefTextEditor
    {
        internal static AsmdefReferencesEditResult ReplaceReferencesArray(
            string source,
            string[] references)
        {
            Debug.Assert(source != null, "source must not be null");
            Debug.Assert(references != null, "references must not be null");
            Debug.Assert(references.Length > 0, "references must not be empty");

            ReferencesArraySpan span = FindTopLevelReferencesArray(source);
            if (!span.Found)
            {
                return AsmdefReferencesEditResult.NotReplaced;
            }

            string renderedArray = RenderReferencesArray(source, span, references);
            string content = source.Substring(0, span.ArrayStartIndex) +
                renderedArray +
                source.Substring(span.ArrayEndIndexExclusive);
            return AsmdefReferencesEditResult.ReplacedWith(content);
        }

        private static ReferencesArraySpan FindTopLevelReferencesArray(string source)
        {
            int depth = 0;
            int index = 0;
            while (index < source.Length)
            {
                char current = source[index];
                if (current == '"')
                {
                    int stringStartIndex = index;
                    index = SkipJsonString(source, index);
                    if (depth != 1)
                    {
                        continue;
                    }

                    // A depth-1 string is a property name only when the next
                    // non-whitespace character is a colon.
                    int colonIndex = SkipWhitespace(source, index);
                    if (colonIndex >= source.Length || source[colonIndex] != ':')
                    {
                        continue;
                    }

                    string propertyName = source.Substring(
                        stringStartIndex + 1,
                        index - stringStartIndex - 2);
                    int valueStartIndex = SkipWhitespace(source, colonIndex + 1);
                    if (!string.Equals(propertyName, "references", StringComparison.Ordinal) ||
                        valueStartIndex >= source.Length ||
                        source[valueStartIndex] != '[')
                    {
                        continue;
                    }

                    int arrayEndIndexExclusive = SkipJsonArray(source, valueStartIndex);
                    return ReferencesArraySpan.FoundAt(
                        stringStartIndex,
                        valueStartIndex,
                        arrayEndIndexExclusive);
                }

                if (current == '{' || current == '[')
                {
                    depth++;
                }
                else if (current == '}' || current == ']')
                {
                    depth--;
                }

                index++;
            }

            return ReferencesArraySpan.NotFound;
        }

        // Returns the index just after the closing quote.
        private static int SkipJsonString(string source, int openQuoteIndex)
        {
            int index = openQuoteIndex + 1;
            while (index < source.Length)
            {
                char current = source[index];
                if (current == '\\')
                {
                    index += 2;
                    continue;
                }

                if (current == '"')
                {
                    return index + 1;
                }

                index++;
            }

            return source.Length;
        }

        // Returns the index just after the matching closing bracket.
        private static int SkipJsonArray(string source, int openBracketIndex)
        {
            int bracketDepth = 0;
            int index = openBracketIndex;
            while (index < source.Length)
            {
                char current = source[index];
                if (current == '"')
                {
                    index = SkipJsonString(source, index);
                    continue;
                }

                if (current == '[')
                {
                    bracketDepth++;
                }
                else if (current == ']')
                {
                    bracketDepth--;
                    if (bracketDepth == 0)
                    {
                        return index + 1;
                    }
                }

                index++;
            }

            return source.Length;
        }

        private static int SkipWhitespace(string source, int startIndex)
        {
            int index = startIndex;
            while (index < source.Length && char.IsWhiteSpace(source[index]))
            {
                index++;
            }

            return index;
        }

        private static string RenderReferencesArray(
            string source,
            ReferencesArraySpan span,
            string[] references)
        {
            string originalArrayText = source.Substring(
                span.ArrayStartIndex,
                span.ArrayEndIndexExclusive - span.ArrayStartIndex);
            if (originalArrayText.IndexOf('\n') < 0)
            {
                return RenderSingleLineArray(references);
            }

            // The file's own newline flavor and the property line's indent decide the layout;
            // Environment.NewLine would silently rewrite CRLF files on macOS and vice versa.
            string newline = source.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";
            string propertyIndent = GetLineIndent(source, span.PropertyNameIndex);
            string indentUnit = propertyIndent.Length > 0 ? propertyIndent : "    ";
            string elementIndent = propertyIndent + indentUnit;
            StringBuilder builder = new();
            builder.Append('[');
            for (int index = 0; index < references.Length; index++)
            {
                builder.Append(newline);
                builder.Append(elementIndent);
                builder.Append(JsonConvert.ToString(references[index]));
                if (index < references.Length - 1)
                {
                    builder.Append(',');
                }
            }

            builder.Append(newline);
            builder.Append(propertyIndent);
            builder.Append(']');
            return builder.ToString();
        }

        private static string RenderSingleLineArray(string[] references)
        {
            StringBuilder builder = new();
            builder.Append('[');
            for (int index = 0; index < references.Length; index++)
            {
                builder.Append(JsonConvert.ToString(references[index]));
                if (index < references.Length - 1)
                {
                    builder.Append(", ");
                }
            }

            builder.Append(']');
            return builder.ToString();
        }

        // Returns the leading whitespace of the property's line, or empty when the
        // property does not start its own line.
        private static string GetLineIndent(string source, int propertyNameIndex)
        {
            Debug.Assert(propertyNameIndex > 0, "propertyNameIndex must be inside the object");

            int lineStartIndex = source.LastIndexOf('\n', propertyNameIndex - 1) + 1;
            int index = lineStartIndex;
            while (index < propertyNameIndex && (source[index] == ' ' || source[index] == '\t'))
            {
                index++;
            }

            if (index != propertyNameIndex)
            {
                return string.Empty;
            }

            return source.Substring(lineStartIndex, index - lineStartIndex);
        }

        internal readonly struct AsmdefReferencesEditResult
        {
            public static AsmdefReferencesEditResult NotReplaced => new(false, string.Empty);

            public static AsmdefReferencesEditResult ReplacedWith(string content)
            {
                return new AsmdefReferencesEditResult(true, content);
            }

            private AsmdefReferencesEditResult(bool replaced, string content)
            {
                Replaced = replaced;
                Content = content;
            }

            public bool Replaced { get; }
            public string Content { get; }
        }

        private readonly struct ReferencesArraySpan
        {
            public static ReferencesArraySpan NotFound => new(false, 0, 0, 0);

            public static ReferencesArraySpan FoundAt(
                int propertyNameIndex,
                int arrayStartIndex,
                int arrayEndIndexExclusive)
            {
                return new ReferencesArraySpan(
                    true,
                    propertyNameIndex,
                    arrayStartIndex,
                    arrayEndIndexExclusive);
            }

            private ReferencesArraySpan(
                bool found,
                int propertyNameIndex,
                int arrayStartIndex,
                int arrayEndIndexExclusive)
            {
                Found = found;
                PropertyNameIndex = propertyNameIndex;
                ArrayStartIndex = arrayStartIndex;
                ArrayEndIndexExclusive = arrayEndIndexExclusive;
            }

            public bool Found { get; }
            public int PropertyNameIndex { get; }
            public int ArrayStartIndex { get; }
            public int ArrayEndIndexExclusive { get; }
        }
    }
}
