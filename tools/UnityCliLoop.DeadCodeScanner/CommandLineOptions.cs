using System;
using System.IO;

namespace UnityCliLoop.DeadCodeScanner
{
    /// <summary>
    /// Parses the small scanner CLI without introducing a command framework dependency.
    /// </summary>
    public static class CommandLineOptions
    {
        public static ScanOptions Parse(string[] args)
        {
            ScanOptions defaults = ScanOptions.Default(Directory.GetCurrentDirectory());
            string rootPath = defaults.RootPath;
            ScanScope scope = defaults.Scope;
            bool includeTypes = defaults.IncludeTypes;
            bool includeMembers = defaults.IncludeMembers;
            bool includeLocals = defaults.IncludeLocals;
            bool includeTestOnly = defaults.IncludeTestOnly;
            bool includeKept = defaults.IncludeKept;
            ReportFormat format = defaults.Format;
            bool failOnHighConfidence = defaults.FailOnHighConfidence;

            for (int index = 0; index < args.Length; index++)
            {
                string argument = args[index];
                if (argument == "--root")
                {
                    rootPath = ReadValue(args, ref index, argument);
                    continue;
                }

                if (argument == "--scope")
                {
                    scope = ParseScope(ReadValue(args, ref index, argument));
                    continue;
                }

                if (argument == "--include-types")
                {
                    includeTypes = ParseBoolean(ReadValue(args, ref index, argument), argument);
                    continue;
                }

                if (argument == "--include-members")
                {
                    includeMembers = ParseBoolean(ReadValue(args, ref index, argument), argument);
                    continue;
                }

                if (argument == "--include-locals")
                {
                    includeLocals = ParseBoolean(ReadValue(args, ref index, argument), argument);
                    continue;
                }

                if (argument == "--include-test-only")
                {
                    includeTestOnly = ParseBoolean(ReadValue(args, ref index, argument), argument);
                    continue;
                }

                if (argument == "--include-kept")
                {
                    includeKept = ParseBoolean(ReadValue(args, ref index, argument), argument);
                    continue;
                }

                if (argument == "--format")
                {
                    format = ParseFormat(ReadValue(args, ref index, argument));
                    continue;
                }

                if (argument == "--fail-on")
                {
                    failOnHighConfidence = ParseFailOn(ReadValue(args, ref index, argument));
                    continue;
                }

                if (argument == "--help" || argument == "-h")
                {
                    throw new ArgumentException(CreateHelpText());
                }

                throw new ArgumentException($"Unknown argument '{argument}'.");
            }

            return new ScanOptions(
                Path.GetFullPath(rootPath),
                scope,
                includeTypes,
                includeMembers,
                includeLocals,
                includeTestOnly,
                includeKept,
                format,
                failOnHighConfidence);
        }

        public static string CreateHelpText()
        {
            return string.Join(
                Environment.NewLine,
                "Usage: dotnet run --project tools/UnityCliLoop.DeadCodeScanner -- [options]",
                "",
                "Options:",
                "  --root <path>                   Repository root. Defaults to current directory.",
                "  --scope private|internal|public  Detection scope. Defaults to private.",
                "  --include-types true|false       Include type symbols. Defaults to true.",
                "  --include-members true|false     Include member symbols. Defaults to true.",
                "  --include-locals true|false      Include local variables. Defaults to true.",
                "  --include-test-only true|false   Include test-only findings. Defaults to true.",
                "  --include-kept true|false        Include Unity/reflection-kept findings. Defaults to false.",
                "  --format table|json              Output format. Defaults to table.",
                "  --fail-on none|high-confidence   Exit 1 for high confidence findings.");
        }

        private static string ReadValue(string[] args, ref int index, string optionName)
        {
            int valueIndex = index + 1;
            if (valueIndex >= args.Length)
            {
                throw new ArgumentException($"{optionName} requires a value.");
            }

            index = valueIndex;
            return args[valueIndex];
        }

        private static ScanScope ParseScope(string value)
        {
            string normalized = value.Trim().ToLowerInvariant();
            if (normalized == "private")
            {
                return ScanScope.Private;
            }

            if (normalized == "internal")
            {
                return ScanScope.Internal;
            }

            if (normalized == "public")
            {
                return ScanScope.Public;
            }

            throw new ArgumentException($"Unsupported scope '{value}'.");
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

        private static bool ParseFailOn(string value)
        {
            string normalized = value.Trim().ToLowerInvariant();
            if (normalized == "none")
            {
                return false;
            }

            if (normalized == "high-confidence")
            {
                return true;
            }

            throw new ArgumentException($"Unsupported fail-on value '{value}'.");
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
