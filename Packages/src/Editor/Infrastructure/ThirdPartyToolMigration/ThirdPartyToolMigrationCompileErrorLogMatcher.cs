using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// A single compile-error console log entry, already carrying the file path and line number the
    /// caller resolved from the raw log (see ThirdPartyToolMigrationCompileErrorLogMatcher for why the
    /// matcher itself does not parse these out of the message text).
    /// </summary>
    internal readonly struct CompileErrorLogEntry
    {
        public CompileErrorLogEntry(string message, string filePath, int lineNumber)
        {
            Debug.Assert(!string.IsNullOrEmpty(message), "message must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(filePath), "filePath must not be null or empty");
            Debug.Assert(lineNumber >= 0, "lineNumber must not be negative");

            Message = message ?? throw new ArgumentNullException(nameof(message));
            FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            LineNumber = lineNumber;
        }

        public string Message { get; }
        public string FilePath { get; }
        public int LineNumber { get; }
    }

    /// <summary>
    /// The result of matching compile-error log entries against V2 legacy API tokens: the matched
    /// entries themselves, plus the deduplicated set of file paths they point to.
    /// </summary>
    internal readonly struct ThirdPartyToolMigrationCompileErrorLogMatchResult
    {
        public ThirdPartyToolMigrationCompileErrorLogMatchResult(
            List<CompileErrorLogEntry> matchedEntries,
            List<string> targetFilePaths)
        {
            Debug.Assert(matchedEntries != null, "matchedEntries must not be null");
            Debug.Assert(targetFilePaths != null, "targetFilePaths must not be null");

            MatchedEntries = matchedEntries ?? throw new ArgumentNullException(nameof(matchedEntries));
            TargetFilePaths = targetFilePaths ?? throw new ArgumentNullException(nameof(targetFilePaths));
        }

        public List<CompileErrorLogEntry> MatchedEntries { get; }
        public List<string> TargetFilePaths { get; }
    }

    /// <summary>
    /// Matches compile-error log entries against V2 legacy API tokens (see
    /// ThirdPartyToolMigrationDetectionRules) to drive the compile-error-driven auto-scan trigger.
    /// This is a pure filter/dedup step: parsing file path and line number out of raw console log text
    /// is a separate concern owned by the caller wiring this to the real log source.
    /// </summary>
    internal static class ThirdPartyToolMigrationCompileErrorLogMatcher
    {
        internal static ThirdPartyToolMigrationCompileErrorLogMatchResult Match(
            IReadOnlyList<CompileErrorLogEntry> entries)
        {
            Debug.Assert(entries != null, "entries must not be null");
            if (entries == null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            List<CompileErrorLogEntry> matchedEntries = new List<CompileErrorLogEntry>();
            foreach (CompileErrorLogEntry entry in entries)
            {
                if (ThirdPartyToolMigrationDetectionRules.ContainsLegacyApiToken(entry.Message))
                {
                    matchedEntries.Add(entry);
                }
            }

            List<string> targetFilePaths = matchedEntries
                .Select(entry => entry.FilePath)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            return new ThirdPartyToolMigrationCompileErrorLogMatchResult(matchedEntries, targetFilePaths);
        }
    }
}
