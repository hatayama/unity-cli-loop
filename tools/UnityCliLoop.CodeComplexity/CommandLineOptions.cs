using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace UnityCliLoop.CodeComplexity
{
    /// <summary>
    /// Parses the small complexity CLI without introducing a command framework dependency.
    /// </summary>
    public static class CommandLineOptions
    {
        private const string MaximumInt32Value = "2147483647";

        public static CodeComplexityOptions Parse(string[] args)
        {
            CommandLineParseResult result = TryParse(args);
            if (!result.Success)
            {
                throw new ArgumentException(result.ErrorMessage);
            }

            if (result.Options == null)
            {
                throw new InvalidOperationException("Successful command-line parsing must produce options.");
            }

            return result.Options;
        }

        public static CommandLineParseResult TryParse(string[] args)
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
                    (bool success, string value, int nextIndex, string errorMessage) = ReadValue(args, index, argument);
                    if (!success)
                    {
                        return CommandLineParseResult.Failed(errorMessage);
                    }

                    rootPath = value;
                    index = nextIndex;
                    continue;
                }

                if (argument == "--max-complexity")
                {
                    (bool success, string value, int nextIndex, string errorMessage) = ReadValue(args, index, argument);
                    if (!success)
                    {
                        return CommandLineParseResult.Failed(errorMessage);
                    }

                    (bool integerSuccess, int parsedValue, string integerErrorMessage) = ParsePositiveInteger(value, argument);
                    if (!integerSuccess)
                    {
                        return CommandLineParseResult.Failed(integerErrorMessage);
                    }

                    maxComplexity = parsedValue;
                    index = nextIndex;
                    continue;
                }

                if (argument == "--include-non-production")
                {
                    (bool success, string value, int nextIndex, string errorMessage) = ReadValue(args, index, argument);
                    if (!success)
                    {
                        return CommandLineParseResult.Failed(errorMessage);
                    }

                    (bool booleanSuccess, bool parsedValue, string booleanErrorMessage) = ParseBoolean(value, argument);
                    if (!booleanSuccess)
                    {
                        return CommandLineParseResult.Failed(booleanErrorMessage);
                    }

                    includeNonProduction = parsedValue;
                    index = nextIndex;
                    continue;
                }

                if (argument == "--format")
                {
                    (bool success, string value, int nextIndex, string errorMessage) = ReadValue(args, index, argument);
                    if (!success)
                    {
                        return CommandLineParseResult.Failed(errorMessage);
                    }

                    (bool formatSuccess, ReportFormat parsedValue, string formatErrorMessage) = ParseFormat(value);
                    if (!formatSuccess)
                    {
                        return CommandLineParseResult.Failed(formatErrorMessage);
                    }

                    format = parsedValue;
                    index = nextIndex;
                    continue;
                }

                if (argument == "--fail-on-exceeded")
                {
                    (bool success, string value, int nextIndex, string errorMessage) = ReadValue(args, index, argument);
                    if (!success)
                    {
                        return CommandLineParseResult.Failed(errorMessage);
                    }

                    (bool booleanSuccess, bool parsedValue, string booleanErrorMessage) = ParseBoolean(value, argument);
                    if (!booleanSuccess)
                    {
                        return CommandLineParseResult.Failed(booleanErrorMessage);
                    }

                    failOnExceeded = parsedValue;
                    index = nextIndex;
                    continue;
                }

                if (argument == "--help" || argument == "-h")
                {
                    return CommandLineParseResult.Failed(CreateHelpText());
                }

                return CommandLineParseResult.Failed($"Unknown argument '{argument}'.");
            }

            (bool rootSuccess, string resolvedRootPath, string rootErrorMessage) = ResolveRootPath(rootPath);
            if (!rootSuccess)
            {
                return CommandLineParseResult.Failed(rootErrorMessage);
            }

            return CommandLineParseResult.Succeeded(
                new CodeComplexityOptions(
                    resolvedRootPath,
                    maxComplexity,
                    includeNonProduction,
                    format,
                    failOnExceeded));
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

        private static (bool Success, string Value, string ErrorMessage) ResolveRootPath(string rootPath)
        {
            if (string.IsNullOrEmpty(rootPath))
            {
                return (false, string.Empty, "--root expects a non-empty path.");
            }

            if (rootPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                return (false, string.Empty, "--root expects a path without invalid characters.");
            }

            return (true, Path.GetFullPath(rootPath), string.Empty);
        }

        private static (bool Success, string Value, int NextIndex, string ErrorMessage) ReadValue(string[] args, int index, string optionName)
        {
            int valueIndex = index + 1;
            if (valueIndex >= args.Length)
            {
                return (false, string.Empty, index, $"{optionName} requires a value.");
            }

            return (true, args[valueIndex], valueIndex, string.Empty);
        }

        private static (bool Success, int Value, string ErrorMessage) ParsePositiveInteger(string value, string optionName)
        {
            if (string.IsNullOrEmpty(value) || !value.All(char.IsDigit))
            {
                return (false, 0, $"{optionName} expects a positive integer.");
            }

            string normalized = value.TrimStart('0');
            if (normalized.Length == 0)
            {
                return (false, 0, $"{optionName} expects a positive integer.");
            }

            if (!IsWithinInt32Range(normalized))
            {
                return (false, 0, $"{optionName} expects a positive integer.");
            }

            return (true, int.Parse(normalized, CultureInfo.InvariantCulture), string.Empty);
        }

        private static bool IsWithinInt32Range(string normalizedValue)
        {
            if (normalizedValue.Length < MaximumInt32Value.Length)
            {
                return true;
            }

            return normalizedValue.Length == MaximumInt32Value.Length
                && string.CompareOrdinal(normalizedValue, MaximumInt32Value) <= 0;
        }

        private static (bool Success, ReportFormat Value, string ErrorMessage) ParseFormat(string value)
        {
            string normalized = value.Trim().ToLowerInvariant();
            if (normalized == "table")
            {
                return (true, ReportFormat.Table, string.Empty);
            }

            if (normalized == "json")
            {
                return (true, ReportFormat.Json, string.Empty);
            }

            return (false, ReportFormat.Table, $"Unsupported format '{value}'.");
        }

        private static (bool Success, bool Value, string ErrorMessage) ParseBoolean(string value, string optionName)
        {
            string normalized = value.Trim().ToLowerInvariant();
            if (normalized == "true")
            {
                return (true, true, string.Empty);
            }

            if (normalized == "false")
            {
                return (true, false, string.Empty);
            }

            return (false, false, $"{optionName} expects true or false.");
        }
    }
}
