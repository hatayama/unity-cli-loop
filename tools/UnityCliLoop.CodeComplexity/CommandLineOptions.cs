using System;
using System.IO;
using System.Linq;

namespace UnityCliLoop.CodeComplexity
{
    /// <summary>
    /// Parses the small complexity CLI without introducing a command framework dependency.
    /// </summary>
    public static class CommandLineOptions
    {
        public static CodeComplexityOptions Parse(string[] args)
        {
            CodeComplexityOptions defaults = CodeComplexityOptions.Default(Directory.GetCurrentDirectory());
            string rootPath = defaults.RootPath;
            int maxComplexity = defaults.MaxComplexity;
            bool includeNonProduction = defaults.IncludeNonProduction;
            ReportFormat format = defaults.Format;
            bool failOnExceeded = defaults.FailOnExceeded;

            for (int index = 0; index < args.Length; index++)
            {
                string argument = args[index];
                if (argument == "--root")
                {
                    (string value, int nextIndex) = ReadValue(args, index, argument);
                    rootPath = value;
                    index = nextIndex;
                    continue;
                }

                if (argument == "--max-complexity")
                {
                    (string value, int nextIndex) = ReadValue(args, index, argument);
                    maxComplexity = ParsePositiveInteger(value, argument);
                    index = nextIndex;
                    continue;
                }

                if (argument == "--include-non-production")
                {
                    (string value, int nextIndex) = ReadValue(args, index, argument);
                    includeNonProduction = ParseBoolean(value, argument);
                    index = nextIndex;
                    continue;
                }

                if (argument == "--format")
                {
                    (string value, int nextIndex) = ReadValue(args, index, argument);
                    format = ParseFormat(value);
                    index = nextIndex;
                    continue;
                }

                if (argument == "--fail-on-exceeded")
                {
                    (string value, int nextIndex) = ReadValue(args, index, argument);
                    failOnExceeded = ParseBoolean(value, argument);
                    index = nextIndex;
                    continue;
                }

                if (argument == "--help" || argument == "-h")
                {
                    throw new ArgumentException(CreateHelpText());
                }

                throw new ArgumentException($"Unknown argument '{argument}'.");
            }

            return new CodeComplexityOptions(
                Path.GetFullPath(rootPath),
                maxComplexity,
                includeNonProduction,
                format,
                failOnExceeded);
        }

        public static bool HasHelpOption(string[] args)
        {
            return args.Any(argument => argument == "--help" || argument == "-h");
        }

        public static string CreateHelpText()
        {
            return string.Join(
                Environment.NewLine,
                "Usage: dotnet run --project tools/UnityCliLoop.CodeComplexity -- [options]",
                "",
                "Options:",
                "  --root <path>                         Repository root. Defaults to current directory.",
                "  --max-complexity <number>             CA1502 threshold. Defaults to 25.",
                "  --include-non-production true|false   Include Assets and tests sources. Defaults to false.",
                "  --format table|json                   Output format. Defaults to table.",
                "  --fail-on-exceeded true|false         Exit 1 when CA1502 diagnostics exist. Defaults to false.");
        }

        private static (string Value, int NextIndex) ReadValue(string[] args, int index, string optionName)
        {
            int valueIndex = index + 1;
            if (valueIndex >= args.Length)
            {
                throw new ArgumentException($"{optionName} requires a value.");
            }

            return (args[valueIndex], valueIndex);
        }

        private static int ParsePositiveInteger(string value, string optionName)
        {
            if (!string.IsNullOrEmpty(value) && value.All(char.IsDigit))
            {
                int parsed = int.Parse(value);
                if (parsed > 0)
                {
                    return parsed;
                }
            }

            throw new ArgumentException($"{optionName} expects a positive integer.");
        }

        private static ReportFormat ParseFormat(string value)
        {
            string normalized = value.Trim().ToLowerInvariant();
            if (normalized == "table")
            {
                return ReportFormat.Table;
            }

            if (normalized == "json")
            {
                return ReportFormat.Json;
            }

            throw new ArgumentException($"Unsupported format '{value}'.");
        }

        private static bool ParseBoolean(string value, string optionName)
        {
            string normalized = value.Trim().ToLowerInvariant();
            if (normalized == "true")
            {
                return true;
            }

            if (normalized == "false")
            {
                return false;
            }

            throw new ArgumentException($"{optionName} expects true or false.");
        }
    }
}
