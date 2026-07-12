using System;
using System.Diagnostics;
using System.Text;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds user-snippet diagnostic context from #line-mapped compiler locations.
    /// </summary>
    internal static class DynamicCodeDiagnosticContextBuilder
    {
        internal static string BuildContext(
            string[] userSnippetLines,
            int userLineNumber1Based,
            int column1Based)
        {
            if (userSnippetLines == null
                || userSnippetLines.Length == 0
                || userLineNumber1Based <= 0
                || userLineNumber1Based > userSnippetLines.Length)
            {
                return string.Empty;
            }

            int start = Math.Max(1, userLineNumber1Based - 3);
            int end = Math.Min(userSnippetLines.Length, userLineNumber1Based + 3);
            StringBuilder builder = new();
            for (int lineIndex = start; lineIndex <= end; lineIndex++)
            {
                string line = userSnippetLines[lineIndex - 1];
                string linePrefix = $"L{lineIndex}:";
                builder.AppendLine(linePrefix + line);
                if (lineIndex == userLineNumber1Based)
                {
                    int caretPos = Math.Max(1, column1Based);
                    builder.AppendLine(
                        new string(' ', linePrefix.Length)
                        + new string(' ', Math.Max(0, caretPos - 1))
                        + "^");
                }
            }

            return builder.ToString();
        }

        internal static bool IsUserSnippetLineInRange(string[] userSnippetLines, int userLineNumber1Based)
        {
            Debug.Assert(userSnippetLines != null, "userSnippetLines must not be null");
            return userLineNumber1Based > 0 && userLineNumber1Based <= userSnippetLines.Length;
        }
    }
}
