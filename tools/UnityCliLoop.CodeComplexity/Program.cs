using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace UnityCliLoop.CodeComplexity
{
    /// <summary>
    /// Console entry point that runs CA1502 complexity analysis and maps findings to an exit code.
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            return RunAsync(args, CancellationToken.None).GetAwaiter().GetResult();
        }

        private static async Task<int> RunAsync(string[] args, CancellationToken ct)
        {
            if (CommandLineOptions.HasHelpOption(args))
            {
                Console.WriteLine(CommandLineOptions.CreateHelpText());
                return 0;
            }

            CodeComplexityOptions options = CommandLineOptions.Parse(args);
            CodeComplexityAnalyzerRunner runner = new();
            IReadOnlyList<CodeComplexityIssue> issues = await runner.AnalyzeAsync(options, ct);
            CodeComplexityReporter.Write(issues, options);

            if (options.FailOnExceeded && issues.Any())
            {
                return 1;
            }

            return 0;
        }
    }
}
