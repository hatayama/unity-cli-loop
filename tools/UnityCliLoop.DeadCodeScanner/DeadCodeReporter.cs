using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace UnityCliLoop.DeadCodeScanner
{
    /// <summary>
    /// Writes scanner findings in stable human-readable or machine-readable form.
    /// </summary>
    public static class DeadCodeReporter
    {
        public static void Write(IReadOnlyList<DeadCodeIssue> issues, ScanOptions options)
        {
            if (options.Format == ReportFormat.Json)
            {
                WriteJson(issues);
                return;
            }

            WriteTable(issues);
        }

        private static void WriteJson(IReadOnlyList<DeadCodeIssue> issues)
        {
            string json = JsonSerializer.Serialize(issues, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            Console.WriteLine(json);
        }

        private static void WriteTable(IReadOnlyList<DeadCodeIssue> issues)
        {
            if (issues.Count == 0)
            {
                Console.WriteLine("No dead code candidates found.");
                return;
            }

            Console.WriteLine("Category\tKind\tAccessibility\tProductionRefs\tNonProductionRefs\tLocation\tSymbol\tReason");
            foreach (DeadCodeIssue issue in issues)
            {
                string location = $"{issue.FilePath}:{issue.Line}";
                Console.WriteLine(string.Join(
                    "\t",
                    issue.Category,
                    issue.SymbolKind,
                    issue.Accessibility,
                    issue.ProductionReferenceCount,
                    issue.NonProductionReferenceCount,
                    location,
                    issue.FullName,
                    issue.Reason));
            }

            Console.WriteLine();
            Console.WriteLine($"Total: {issues.Count}");
            foreach (IGrouping<DeadCodeCategory, DeadCodeIssue> group in issues.GroupBy(issue => issue.Category))
            {
                Console.WriteLine($"{group.Key}: {group.Count()}");
            }
        }
    }
}
