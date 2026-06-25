using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace UnityCliLoop.CodeComplexity
{
    /// <summary>
    /// Defines the report shape emitted by the complexity command.
    /// </summary>
    public enum ReportFormat
    {
        Table,
        Json
    }

    /// <summary>
    /// Carries command-line options for deterministic complexity analysis.
    /// </summary>
    public sealed class CodeComplexityOptions
    {
        public string RootPath { get; }
        public int MaxComplexity { get; }
        public bool IncludeNonProduction { get; }
        public ReportFormat Format { get; }
        public bool FailOnExceeded { get; }

        public CodeComplexityOptions(
            string rootPath,
            int maxComplexity,
            bool includeNonProduction,
            ReportFormat format,
            bool failOnExceeded)
        {
            Debug.Assert(!string.IsNullOrEmpty(rootPath), "The repository root path must be resolved before options are created.");
            Debug.Assert(maxComplexity > 0, "The complexity threshold must be positive after input validation.");

            RootPath = rootPath;
            MaxComplexity = maxComplexity;
            IncludeNonProduction = includeNonProduction;
            Format = format;
            FailOnExceeded = failOnExceeded;
        }

        public static CodeComplexityOptions Default(string rootPath)
        {
            return new CodeComplexityOptions(
                rootPath,
                maxComplexity: 25,
                includeNonProduction: false,
                ReportFormat.Table,
                failOnExceeded: false);
        }
    }

    /// <summary>
    /// Represents one CA1502 complexity diagnostic in a stable reporting shape.
    /// </summary>
    public sealed class CodeComplexityIssue
    {
        public string RuleId { get; }
        public string Severity { get; }
        public string FilePath { get; }
        public int Line { get; }
        public int Column { get; }
        public string Message { get; }

        public CodeComplexityIssue(
            string ruleId,
            string severity,
            string filePath,
            int line,
            int column,
            string message)
        {
            Debug.Assert(!string.IsNullOrEmpty(ruleId), "Analyzer diagnostics must have a rule id.");
            Debug.Assert(line >= 1, "Reported lines are one-based.");
            Debug.Assert(column >= 1, "Reported columns are one-based.");

            RuleId = ruleId;
            Severity = severity;
            FilePath = filePath;
            Line = line;
            Column = column;
            Message = message;
        }
    }

    /// <summary>
    /// Stores source paths grouped by their production role.
    /// </summary>
    public sealed class SourceFileSet
    {
        public IReadOnlyList<string> ProductionFiles { get; }
        public IReadOnlyList<string> NonProductionFiles { get; }

        public SourceFileSet(IReadOnlyList<string> productionFiles, IReadOnlyList<string> nonProductionFiles)
        {
            ProductionFiles = productionFiles;
            NonProductionFiles = nonProductionFiles;
        }
    }
}
