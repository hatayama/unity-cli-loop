using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Reads source text from disk for a pause-point response, so the AI agent can see exactly
    /// what code the resolved (possibly rounded-forward) line number maps to.
    /// </summary>
    internal static class SourcePausePointSourceLineReader
    {
        public static string ReadLineText(string absoluteFilePath, int startLine, int endLine)
        {
            if (string.IsNullOrEmpty(absoluteFilePath) || startLine <= 0 || !File.Exists(absoluteFilePath))
            {
                return string.Empty;
            }

            // Hidden or malformed sequence points can report EndLine <= 0 or before StartLine.
            // A backwards or empty range must not wipe the StartLine text the caller already resolved.
            int inclusiveEndLine = endLine;
            if (endLine < startLine || endLine <= 0)
            {
                inclusiveEndLine = startLine;
            }

            IEnumerable<string> spannedLines = File.ReadLines(absoluteFilePath)
                .Skip(startLine - 1)
                .Take(inclusiveEndLine - startLine + 1);
            IEnumerable<string> trimmedNonEmptyLines = spannedLines
                .Select(line => line.Trim())
                .Where(line => line.Length > 0);
            return string.Join(" ", trimmedNonEmptyLines);
        }

        public static string[] SplitSourceLines(string sourceText)
        {
            if (string.IsNullOrEmpty(sourceText))
            {
                return Array.Empty<string>();
            }

            return sourceText.Replace("\r\n", "\n").Split('\n');
        }

        public static string ReadLineTextFromSource(string sourceText, int lineNumber)
        {
            if (string.IsNullOrEmpty(sourceText) || lineNumber <= 0)
            {
                return string.Empty;
            }

            string[] lines = SplitSourceLines(sourceText);
            if (lineNumber > lines.Length)
            {
                return string.Empty;
            }

            return lines[lineNumber - 1].Trim();
        }
    }
}
