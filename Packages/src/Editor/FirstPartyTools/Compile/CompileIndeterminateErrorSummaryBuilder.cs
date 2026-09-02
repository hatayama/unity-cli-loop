using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Summarizes the most recent Unity Console errors for indeterminate compile results so the
    /// likely cause is visible in the compile response itself.
    /// </summary>
    internal static class CompileIndeterminateErrorSummaryBuilder
    {
        internal const string Header = "Recent Console errors:";
        internal const int MaxEntries = 5;
        internal const int MaxLineLength = 300;
        private const string LinePrefix = "- ";
        private const string Ellipsis = "...";

        /// <summary>
        /// Returns the summary block, or null when the Console holds no usable error entries.
        /// Why only the most recent entries: an aborted compile leaves its cause at the tail of the
        /// Console, and older unrelated errors would bury it.
        /// </summary>
        internal static string Build(UnityCliLoopConsoleLogEntry[] consoleEntries)
        {
            Debug.Assert(consoleEntries != null, "consoleEntries must not be null");

            List<string> lines = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int index = consoleEntries.Length - 1; index >= 0; index--)
            {
                if (lines.Count >= MaxEntries)
                {
                    break;
                }

                string line = ToSummaryLine(consoleEntries[index]);
                if (line == null || !seen.Add(line))
                {
                    continue;
                }

                lines.Add(line);
            }

            if (lines.Count == 0)
            {
                return null;
            }

            // Why reverse: entries were collected newest-first, but Console order reads naturally.
            lines.Reverse();
            return Header + "\n" + string.Join("\n", lines);
        }

        private static string ToSummaryLine(UnityCliLoopConsoleLogEntry entry)
        {
            if (entry == null ||
                !string.Equals(entry.Type, UnityCliLoopLogType.Error, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string firstLine = ExtractFirstLine(entry.Message);
            if (firstLine.Length == 0)
            {
                return null;
            }

            if (firstLine.Length > MaxLineLength)
            {
                firstLine = firstLine.Substring(0, MaxLineLength) + Ellipsis;
            }

            return LinePrefix + firstLine;
        }

        private static string ExtractFirstLine(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return string.Empty;
            }

            int newlineIndex = message.IndexOfAny(new[] { '\r', '\n' });
            string firstLine = newlineIndex < 0 ? message : message.Substring(0, newlineIndex);
            return firstLine.Trim();
        }
    }
}
