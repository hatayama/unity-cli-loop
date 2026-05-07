using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace UnityCliLoop.DeadCodeScanner
{
    /// <summary>
    /// Console entry point that runs the scanner and converts findings into a process exit code.
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            return RunAsync(args, CancellationToken.None).GetAwaiter().GetResult();
        }

        private static async Task<int> RunAsync(string[] args, CancellationToken ct)
        {
            if (args.Any(argument => argument == "--help" || argument == "-h"))
            {
                Console.WriteLine(CommandLineOptions.CreateHelpText());
                return 0;
            }

            ScanOptions options = CommandLineOptions.Parse(args);
            DeadCodeScanner scanner = new();
            IReadOnlyList<DeadCodeIssue> issues = await scanner.ScanAsync(options, ct);
            DeadCodeReporter.Write(issues, options);

            if (options.FailOnHighConfidence && issues.Any(issue => issue.IsHighConfidenceDeletionCandidate()))
            {
                return 1;
            }

            return 0;
        }
    }
}
