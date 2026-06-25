using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace UnityCliLoop.CodeComplexity
{
    /// <summary>
    /// Writes complexity diagnostics in stable human-readable or machine-readable form.
    /// </summary>
    public static class CodeComplexityReporter
    {
        public static void Write(IReadOnlyList<CodeComplexityIssue> issues, CodeComplexityOptions options)
        {
            if (options.Format == ReportFormat.Json)
            {
                WriteJson(issues);
                return;
            }

            WriteTable(issues, options.MaxComplexity);
        }

        private static void WriteJson(IReadOnlyList<CodeComplexityIssue> issues)
        {
            string json = JsonSerializer.Serialize(issues, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            Console.WriteLine(json);
        }

        private static void WriteTable(IReadOnlyList<CodeComplexityIssue> issues, int maxComplexity)
        {
            if (issues.Count == 0)
            {
                Console.WriteLine($"No CA1502 complexity issues found above threshold {maxComplexity}.");
                return;
            }

            Console.WriteLine($"CA1502 complexity issues above threshold {maxComplexity}:");
            Console.WriteLine("Rule\tSeverity\tLocation\tMessage");
            foreach (CodeComplexityIssue issue in issues)
            {
                string location = $"{issue.FilePath}:{issue.Line}:{issue.Column}";
                Console.WriteLine(string.Join(
                    "\t",
                    issue.RuleId,
                    issue.Severity,
                    location,
                    issue.Message));
            }

            Console.WriteLine();
            Console.WriteLine($"Total: {issues.Count}");
            foreach (IGrouping<string, CodeComplexityIssue> group in issues.GroupBy(issue => issue.RuleId))
            {
                Console.WriteLine($"{group.Key}: {group.Count()}");
            }
        }
    }
}
