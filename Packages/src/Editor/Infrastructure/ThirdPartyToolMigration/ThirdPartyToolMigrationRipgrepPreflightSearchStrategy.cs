using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Uses ripgrep as an optional fixed-string preflight search strategy.
    /// </summary>
    internal sealed class ThirdPartyToolMigrationRipgrepPreflightSearchStrategy :
        IThirdPartyToolMigrationPreflightSearchStrategy
    {
        private const string RipgrepExecutableName = "rg";
        private const int ProcessTimeoutMs = 5000;
        private const int MatchExitCode = 0;
        private const int NoMatchExitCode = 1;

        public async Task<MigrationTargetPreflightResult> FindMigrationTargetAsync(
            string projectRoot,
            CancellationToken ct)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            string assetsDirectory = Path.Combine(projectRoot, "Assets");
            if (!Directory.Exists(assetsDirectory))
            {
                return MigrationTargetPreflightResult.NoTargets;
            }

            if (ct.IsCancellationRequested)
            {
                return MigrationTargetPreflightResult.NoTargets;
            }

            ProcessStartInfo startInfo = BuildStartInfo(assetsDirectory);
            Process process = ProcessStartHelper.TryStart(startInfo);
            if (process == null)
            {
                return MigrationTargetPreflightResult.NeedsFullScan;
            }

            using (process)
            {
                process.OutputDataReceived += (sender, e) => { };
                process.ErrorDataReceived += (sender, e) => { };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                using CancellationTokenRegistration registration =
                    ct.Register(() => CliInstallationDetector.KillProcessIfRunning(process));
                bool exited = await Task.Run(() => process.WaitForExit(ProcessTimeoutMs));
                if (!exited)
                {
                    CliInstallationDetector.KillProcessIfRunning(process);
                    return MigrationTargetPreflightResult.NeedsFullScan;
                }

                process.WaitForExit();
                if (ct.IsCancellationRequested)
                {
                    return MigrationTargetPreflightResult.NoTargets;
                }

                return MapExitCodeToResult(process.ExitCode);
            }
        }

        internal static MigrationTargetPreflightResult MapExitCodeToResult(int exitCode)
        {
            if (exitCode == NoMatchExitCode)
            {
                return MigrationTargetPreflightResult.NoTargets;
            }

            if (exitCode == MatchExitCode)
            {
                return MigrationTargetPreflightResult.NeedsFullScan;
            }

            return MigrationTargetPreflightResult.NeedsFullScan;
        }

        internal static ProcessStartInfo BuildStartInfo(string assetsDirectory)
        {
            Debug.Assert(!string.IsNullOrEmpty(assetsDirectory), "assetsDirectory must not be null or empty");

            return new ProcessStartInfo
            {
                FileName = RipgrepExecutableName,
                Arguments = BuildArguments(assetsDirectory),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
        }

        internal static string BuildArguments(string assetsDirectory)
        {
            Debug.Assert(!string.IsNullOrEmpty(assetsDirectory), "assetsDirectory must not be null or empty");

            StringBuilder builder = new StringBuilder();
            AppendRawArgument(builder, "--fixed-strings");
            AppendRawArgument(builder, "--quiet");
            AppendRawArgument(builder, "--no-messages");
            AppendRawArgument(builder, "--no-ignore");
            AppendRawArgument(builder, "--hidden");
            AppendRawArgument(builder, "--color");
            AppendQuotedArgument(builder, "never");
            AppendGlobArguments(builder);
            AppendPatternArguments(builder);
            AppendQuotedArgument(builder, assetsDirectory);
            return builder.ToString();
        }

        private static void AppendGlobArguments(StringBuilder builder)
        {
            Debug.Assert(builder != null, "builder must not be null");

            AppendRawArgument(builder, "--glob");
            AppendQuotedArgument(builder, "*.cs");
            AppendRawArgument(builder, "--glob");
            AppendQuotedArgument(builder, "*.asmdef");

            foreach (string directoryName in ThirdPartyToolMigrationRules.GetExcludedDirectoryNames())
            {
                AppendRawArgument(builder, "--glob");
                AppendQuotedArgument(builder, "!**/" + directoryName + "/**");
            }
        }

        private static void AppendPatternArguments(StringBuilder builder)
        {
            Debug.Assert(builder != null, "builder must not be null");

            foreach (string marker in ThirdPartyToolMigrationPreflightMarkerSet.CreateAllCandidateMarkers())
            {
                AppendRawArgument(builder, "--regexp");
                AppendQuotedArgument(builder, marker);
            }
        }

        private static void AppendRawArgument(StringBuilder builder, string argument)
        {
            Debug.Assert(builder != null, "builder must not be null");
            Debug.Assert(!string.IsNullOrEmpty(argument), "argument must not be null or empty");

            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(argument);
        }

        private static void AppendQuotedArgument(StringBuilder builder, string argument)
        {
            Debug.Assert(builder != null, "builder must not be null");
            Debug.Assert(argument != null, "argument must not be null");

            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(QuoteProcessArgument(argument));
        }

        private static string QuoteProcessArgument(string value)
        {
            Debug.Assert(value != null, "value must not be null");

            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
