using System;
using System.Diagnostics;
using System.IO;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Builds login-shell CLI probe commands and parses their marker-delimited output.
    /// </summary>
    internal static class CliShellInstallationProbe
    {
        private const string SHELL_PATH_START_MARKER = "__ULOOP_PATH_START__";
        private const string SHELL_PATH_END_MARKER = "__ULOOP_PATH_END__";
        private const string SHELL_VERSION_START_MARKER = "__ULOOP_VERSION_START__";
        private const string SHELL_VERSION_END_MARKER = "__ULOOP_VERSION_END__";
        private const string SHELL_VERSION_STATUS_START_MARKER = "__ULOOP_VERSION_STATUS_START__";
        private const string SHELL_VERSION_STATUS_END_MARKER = "__ULOOP_VERSION_STATUS_END__";
        private const string SHELL_CONTRACT_START_MARKER = "__ULOOP_CONTRACT_START__";
        private const string SHELL_CONTRACT_END_MARKER = "__ULOOP_CONTRACT_END__";
        private const string SHELL_CONTRACT_STATUS_START_MARKER = "__ULOOP_CONTRACT_STATUS_START__";
        private const string SHELL_CONTRACT_STATUS_END_MARKER = "__ULOOP_CONTRACT_STATUS_END__";
        private const string SHELL_SUCCESS_EXIT_CODE = "0";
        private const string VERSION_JSON_PROJECT_RUNNER_VERSION_PROPERTY = "ProjectRunnerVersion";
        private const string VERSION_JSON_LEGACY_CLI_VERSION_PROPERTY = "CliVersion";
        private const string VERSION_JSON_DISPATCHER_VERSION_PROPERTY = "DispatcherVersion";

        public static ProcessStartInfo BuildShellCliDetectionStartInfo(
            string shell,
            RuntimePlatform platform,
            CliPathSetupPlan pathSetupPlan,
            string currentPath)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(shell), "shell must not be null or empty");

            ProcessStartInfo startInfo = new()
            {
                FileName = shell,
                Arguments = "-l -i -c " + QuoteProcessArgument(BuildShellCliDetectionCommandForShell(
                    CliConstants.EXECUTABLE_NAME,
                    shell)),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            if (platform != RuntimePlatform.WindowsEditor)
            {
                startInfo.EnvironmentVariables[CliConstants.POSIX_PATH_ENVIRONMENT_VARIABLE] =
                    NativeCliInstallPathResolver.BuildPathWithoutInstallDirectory(
                        currentPath,
                        pathSetupPlan.InstallDirectory,
                        platform);
            }

            return startInfo;
        }

        public static bool IsShellDetectionUsableForPathSetup(
            CliInstallationDetection detection,
            RuntimePlatform platform,
            Func<string, RuntimePlatform, bool> isPackageOwnedCurrentUserInstallPath,
            string minimumDispatcherVersion)
        {
            UnityEngine.Debug.Assert(isPackageOwnedCurrentUserInstallPath != null, "isPackageOwnedCurrentUserInstallPath must not be null");
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(minimumDispatcherVersion), "minimumDispatcherVersion must not be null or empty");

            if (isPackageOwnedCurrentUserInstallPath(detection.ExecutablePath, platform))
            {
                return true;
            }

            if (!detection.IsDispatcher)
            {
                return false;
            }

            return CliVersionComparer.IsVersionGreaterThanOrEqual(
                detection.Version,
                minimumDispatcherVersion);
        }

        public static string BuildShellCliDetectionCommandForShell(
            string executableName,
            string shellPath)
        {
            string shellName = string.IsNullOrWhiteSpace(shellPath)
                ? string.Empty
                : Path.GetFileName(shellPath.Trim()).ToLowerInvariant();
            if (string.Equals(shellName, "fish", StringComparison.Ordinal))
            {
                return BuildFishShellCliDetectionCommand(executableName);
            }

            return BuildShellCliDetectionCommand(executableName);
        }

        public static string BuildShellCliDetectionCommand(string executableName)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(executableName), "executableName must not be null or empty");

            return "echo " + SHELL_PATH_START_MARKER + "\n"
                + "command -v " + executableName + "\n"
                + "echo " + SHELL_PATH_END_MARKER + "\n"
                + "echo " + SHELL_CONTRACT_START_MARKER + "\n"
                + executableName + " " + CliConstants.VERSION_FLAG + " " + CliConstants.JSON_FLAG + "\n"
                + "uloop_contract_status=$?\n"
                + "echo " + SHELL_CONTRACT_END_MARKER + "\n"
                + "echo " + SHELL_CONTRACT_STATUS_START_MARKER + "\n"
                + "echo $uloop_contract_status\n"
                + "echo " + SHELL_CONTRACT_STATUS_END_MARKER + "\n"
                + "echo " + SHELL_VERSION_START_MARKER + "\n"
                + executableName + " " + CliConstants.SHORT_VERSION_FLAG + "\n"
                + "uloop_version_status=$?\n"
                + "echo " + SHELL_VERSION_END_MARKER + "\n"
                + "echo " + SHELL_VERSION_STATUS_START_MARKER + "\n"
                + "echo $uloop_version_status\n"
                + "echo " + SHELL_VERSION_STATUS_END_MARKER;
        }

        private static string BuildFishShellCliDetectionCommand(string executableName)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(executableName), "executableName must not be null or empty");

            return "echo " + SHELL_PATH_START_MARKER + "\n"
                + "command -v " + executableName + "\n"
                + "echo " + SHELL_PATH_END_MARKER + "\n"
                + "echo " + SHELL_CONTRACT_START_MARKER + "\n"
                + executableName + " " + CliConstants.VERSION_FLAG + " " + CliConstants.JSON_FLAG + "\n"
                + "set uloop_contract_status $status\n"
                + "echo " + SHELL_CONTRACT_END_MARKER + "\n"
                + "echo " + SHELL_CONTRACT_STATUS_START_MARKER + "\n"
                + "echo $uloop_contract_status\n"
                + "echo " + SHELL_CONTRACT_STATUS_END_MARKER + "\n"
                + "echo " + SHELL_VERSION_START_MARKER + "\n"
                + executableName + " " + CliConstants.SHORT_VERSION_FLAG + "\n"
                + "set uloop_version_status $status\n"
                + "echo " + SHELL_VERSION_END_MARKER + "\n"
                + "echo " + SHELL_VERSION_STATUS_START_MARKER + "\n"
                + "echo $uloop_version_status\n"
                + "echo " + SHELL_VERSION_STATUS_END_MARKER;
        }

        public static CliInstallationDetection ParseShellCliInstallationOutput(string output)
        {
            string pathBlock = NodeEnvironmentResolver.ExtractBetweenMarkers(
                output,
                SHELL_PATH_START_MARKER,
                SHELL_PATH_END_MARKER);
            string versionBlock = NodeEnvironmentResolver.ExtractBetweenMarkers(
                output,
                SHELL_VERSION_START_MARKER,
                SHELL_VERSION_END_MARKER);
            string versionStatusBlock = NodeEnvironmentResolver.ExtractBetweenMarkers(
                output,
                SHELL_VERSION_STATUS_START_MARKER,
                SHELL_VERSION_STATUS_END_MARKER);
            string contractBlock = NodeEnvironmentResolver.ExtractBetweenMarkers(
                output,
                SHELL_CONTRACT_START_MARKER,
                SHELL_CONTRACT_END_MARKER);
            string contractStatusBlock = NodeEnvironmentResolver.ExtractBetweenMarkers(
                output,
                SHELL_CONTRACT_STATUS_START_MARKER,
                SHELL_CONTRACT_STATUS_END_MARKER);
            string executablePath = NodeEnvironmentResolver.ExtractAbsolutePathLine(pathBlock);
            if (IsSuccessfulShellStatus(contractStatusBlock))
            {
                CliInstallationDetection contractDetection = ParseCliContractOutput(contractBlock, executablePath);
                if (!string.IsNullOrEmpty(contractDetection.Version))
                {
                    return contractDetection;
                }
            }

            string version = IsSuccessfulShellStatus(versionStatusBlock)
                ? ExtractFirstNonEmptyLine(versionBlock)
                : null;
            return new CliInstallationDetection(version, executablePath);
        }

        public static CliInstallationDetection ParseCliContractOutput(string output, string executablePath)
        {
            string jsonLine = ExtractFirstNonEmptyLine(output);
            if (string.IsNullOrEmpty(jsonLine))
            {
                return new CliInstallationDetection(null, executablePath);
            }

            try
            {
                JObject parsed = JObject.Parse(jsonLine);
                CliInstallationDetection dispatcherDetection = ParseDispatcherContract(parsed, executablePath);
                if (!string.IsNullOrEmpty(dispatcherDetection.Version))
                {
                    return dispatcherDetection;
                }

                string version = parsed[VERSION_JSON_PROJECT_RUNNER_VERSION_PROPERTY]?.ToString();
                if (string.IsNullOrEmpty(version))
                {
                    version = parsed[VERSION_JSON_LEGACY_CLI_VERSION_PROPERTY]?.ToString();
                }
                return new CliInstallationDetection(version, executablePath);
            }
            catch (JsonException)
            {
                return new CliInstallationDetection(null, executablePath);
            }
        }

        private static CliInstallationDetection ParseDispatcherContract(JObject parsed, string executablePath)
        {
            // Why: dispatcher compatibility is enforced by the pin's minimumDispatcherVersion (semver floor),
            // so identifying the dispatcher only needs its release version.
            string dispatcherVersion = parsed[VERSION_JSON_DISPATCHER_VERSION_PROPERTY]?.ToString();
            if (string.IsNullOrEmpty(dispatcherVersion))
            {
                return new CliInstallationDetection(null, executablePath);
            }

            return new CliInstallationDetection(
                dispatcherVersion,
                executablePath,
                true);
        }

        private static bool IsSuccessfulShellStatus(string statusBlock)
        {
            string status = ExtractFirstNonEmptyLine(statusBlock);
            return string.Equals(status, SHELL_SUCCESS_EXIT_CODE, StringComparison.Ordinal);
        }

        private static string ExtractFirstNonEmptyLine(string block)
        {
            if (string.IsNullOrEmpty(block))
            {
                return null;
            }

            string[] lines = block.Split('\n');
            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (!string.IsNullOrEmpty(line))
                {
                    return line;
                }
            }

            return null;
        }

        private static string QuoteProcessArgument(string value)
        {
            UnityEngine.Debug.Assert(value != null, "value must not be null");

            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
    }
}
