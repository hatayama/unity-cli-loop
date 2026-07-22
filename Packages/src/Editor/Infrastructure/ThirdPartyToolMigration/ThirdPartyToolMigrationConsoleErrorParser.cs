using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Parses raw Unity Console error text back into structured CompileErrorLogEntry values (file path
    /// + line number) so ThirdPartyToolMigrationCompileErrorLogMatcher can match them. Uses the same
    /// standard csc/Roslyn diagnostic format ("path(line,col): error CSxxxx: message") as
    /// ExternalCompilerMessageParser; deliberately not shared with that class, which is scoped to a
    /// different module (external compiler process output) and parsing the same universal diagnostic
    /// format twice is simpler than coupling two unrelated call sites.
    /// </summary>
    internal static class ThirdPartyToolMigrationConsoleErrorParser
    {
        private static readonly Regex DiagnosticRegex = new Regex(
            @"^(?<file>.+)\((?<line>\d+),(?<column>\d+)\): (?<severity>error|warning) (?<code>[A-Za-z]+\d+): (?<message>.+)$",
            RegexOptions.Compiled);

        internal static List<CompileErrorLogEntry> Parse(
            IReadOnlyList<string> rawMessages,
            string projectRoot)
        {
            Debug.Assert(rawMessages != null, "rawMessages must not be null");
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            List<CompileErrorLogEntry> entries = new List<CompileErrorLogEntry>();
            foreach (string rawMessage in rawMessages)
            {
                if (TryParseErrorLine(rawMessage, projectRoot, out CompileErrorLogEntry entry))
                {
                    entries.Add(entry);
                }
            }

            return entries;
        }

        private static bool TryParseErrorLine(string rawMessage, string projectRoot, out CompileErrorLogEntry entry)
        {
            entry = default;
            if (string.IsNullOrEmpty(rawMessage))
            {
                return false;
            }

            // A console entry's message can span multiple physical lines; only the first line carries
            // the diagnostic-format file/line prefix.
            string firstLine = SplitFirstLine(rawMessage).Trim();
            Match match = DiagnosticRegex.Match(firstLine);
            if (!match.Success || match.Groups["severity"].Value != "error")
            {
                return false;
            }

            string filePath = NormalizeFilePath(match.Groups["file"].Value.Trim(), projectRoot);
            int lineNumber = int.Parse(match.Groups["line"].Value);
            string message = $"{match.Groups["code"].Value}: {match.Groups["message"].Value}";

            entry = new CompileErrorLogEntry(message, filePath, lineNumber);
            return true;
        }

        private static string SplitFirstLine(string text)
        {
            int newlineIndex = text.IndexOfAny(new[] { '\r', '\n' });
            return newlineIndex < 0 ? text : text.Substring(0, newlineIndex);
        }

        /// <summary>
        /// Normalizes a compiler-reported path into an absolute, forward-slash path, without relying on
        /// Path.IsPathRooted/Path.Combine: those resolve rootedness and separators against the host OS
        /// running the test, not the OS that produced the raw message, so a Windows-style path (backslash
        /// separators, drive-letter root) must parse correctly even when this code runs on macOS/Linux.
        /// </summary>
        private static string NormalizeFilePath(string rawFilePath, string projectRoot)
        {
            string slashNormalized = rawFilePath.Replace('\\', '/');
            if (IsRootedPath(slashNormalized))
            {
                return slashNormalized;
            }

            string normalizedProjectRoot = projectRoot.Replace('\\', '/').TrimEnd('/');
            return $"{normalizedProjectRoot}/{slashNormalized}";
        }

        private static bool IsRootedPath(string path)
        {
            if (path.Length == 0)
            {
                return false;
            }

            if (path[0] == '/')
            {
                return true;
            }

            // Windows drive-letter absolute path, e.g. "C:/Users/...".
            return path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':';
        }
    }
}
