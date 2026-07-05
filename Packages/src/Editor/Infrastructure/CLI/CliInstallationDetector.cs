using System.Diagnostics;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    internal readonly struct CliInstallationDetection
    {
        public CliInstallationDetection(string version, string executablePath, bool isDispatcher = false)
        {
            Version = version;
            ExecutablePath = executablePath;
            IsDispatcher = isDispatcher;
        }

        public string Version { get; }
        public string ExecutablePath { get; }
        public bool IsDispatcher { get; }
    }

    /// <summary>
    /// Detects the installed CLI and keeps the result in an instance-scoped editor cache.
    /// </summary>
    public sealed class CliInstallationDetector : ICliInstallationDetector
    {
        private const int PROCESS_TIMEOUT_MS = 5000;
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

        private readonly ICliPinReader _cliPinReader;

        private string _cachedCliVersion;
        private bool _cachedCliIsDispatcher;
        private string _cachedCliExecutablePath;
        private bool _cacheInitialized;
        private bool _isRefreshing;

        public CliInstallationDetector(ICliPinReader cliPinReader)
        {
            UnityEngine.Debug.Assert(cliPinReader != null, "cliPinReader must not be null");

            _cliPinReader = cliPinReader ?? throw new ArgumentNullException(nameof(cliPinReader));
        }

        public bool IsCliInstalled()
        {
            return GetCachedCliVersion() != null;
        }

        public string GetCachedCliVersion()
        {
            return _cacheInitialized ? _cachedCliVersion : null;
        }

        public bool GetCachedCliIsDispatcher()
        {
            return _cacheInitialized && _cachedCliIsDispatcher;
        }

        public string GetCachedCliExecutablePath()
        {
            return _cacheInitialized ? _cachedCliExecutablePath : null;
        }

        public bool IsCheckCompleted()
        {
            return _cacheInitialized;
        }

        public async Task RefreshCliVersionAsync(CancellationToken ct)
        {
            if (_cacheInitialized || _isRefreshing)
            {
                return;
            }

            _isRefreshing = true;
            try
            {
                CliInstallationDetection detection = await DetectCliInstallationAsync(ct);
                _cachedCliVersion = detection.Version;
                _cachedCliIsDispatcher = detection.IsDispatcher;
                _cachedCliExecutablePath = detection.ExecutablePath;
                _cacheInitialized = true;
            }
            finally
            {
                _isRefreshing = false;
            }
        }

        public async Task ForceRefreshCliVersionAsync(CancellationToken ct)
        {
            CliInstallationDetection detection = await DetectCliInstallationAsync(ct);
            _cachedCliVersion = detection.Version;
            _cachedCliIsDispatcher = detection.IsDispatcher;
            _cachedCliExecutablePath = detection.ExecutablePath;
            _cacheInitialized = true;
        }

        public Task<bool> IsCliVisibleFromShellAsync(RuntimePlatform platform, CancellationToken ct)
        {
            if (platform == RuntimePlatform.WindowsEditor)
            {
                return Task.FromResult(true);
            }

            // Why: resolve the minimum dispatcher version once on the caller thread so the background
            // detection task does not perform Unity package IO from a worker thread.
            string minimumDispatcherVersion = _cliPinReader.LoadMinimumDispatcherVersionOrThrow();

            return Task.Run(
                () =>
                {
                    CliInstallationDetection detection = DetectShellCliInstallationBlocking(platform, ct);
                    return IsShellDetectionUsableForPathSetup(
                        detection,
                        platform,
                        NativeCliInstaller.IsPackageOwnedCurrentUserInstallPath,
                        minimumDispatcherVersion);
                },
                ct);
        }

        public void InvalidateCache()
        {
            _cachedCliVersion = null;
            _cachedCliIsDispatcher = false;
            _cachedCliExecutablePath = null;
            _cacheInitialized = false;
            _isRefreshing = false;
        }

        private static Task<CliInstallationDetection> DetectCliInstallationAsync(CancellationToken ct)
        {
            RuntimePlatform platform = UnityEngine.Application.platform;
            return Task.Run(() => DetectCliInstallationBlocking(platform, ct), ct);
        }

        internal static string DetectCliVersionBlocking(RuntimePlatform platform, CancellationToken ct)
        {
            return DetectCliInstallationBlocking(platform, ct).Version;
        }

        internal static CliInstallationDetection DetectCliInstallationBlocking(RuntimePlatform platform, CancellationToken ct)
        {
            CliInstallationDetection packageOwnedDetection = DetectPackageOwnedCliInstallationBlocking(platform, ct);
            CliInstallationDetection shellDetection = DetectShellCliInstallationBlocking(platform, ct);
            return SelectPreferredDetection(packageOwnedDetection, shellDetection);
        }

        internal static CliInstallationDetection SelectPreferredDetection(
            CliInstallationDetection packageOwnedDetection,
            CliInstallationDetection shellDetection)
        {
            return !string.IsNullOrEmpty(shellDetection.Version)
                || !string.IsNullOrEmpty(shellDetection.ExecutablePath)
                ? shellDetection
                : packageOwnedDetection;
        }

        private static CliInstallationDetection DetectPackageOwnedCliInstallationBlocking(
            RuntimePlatform platform,
            CancellationToken ct)
        {
            string executablePath = NativeCliInstaller.GetCurrentUserGlobalCliInstallPath(platform);
            if (string.IsNullOrEmpty(executablePath) || !File.Exists(executablePath))
            {
                return new CliInstallationDetection(null, executablePath);
            }

            return DetectCliInstallationAtExecutablePath(executablePath, ct);
        }

        private static CliInstallationDetection DetectShellCliInstallationBlocking(RuntimePlatform platform, CancellationToken ct)
        {
            if (platform != RuntimePlatform.WindowsEditor)
            {
                return DetectShellCliInstallationFromLoginShell(platform, ct);
            }

            string executablePath = NodeEnvironmentResolver.FindExecutablePathAtPlatform(
                CliConstants.EXECUTABLE_NAME,
                platform);
            return DetectCliInstallationAtExecutablePath(executablePath, ct);
        }

        private static CliInstallationDetection DetectShellCliInstallationFromLoginShell(RuntimePlatform platform, CancellationToken ct)
        {
            string shell = NodeEnvironmentResolver.GetUserShell();
            CliPathSetupPlan pathSetupPlan = CliPathSetupProfileResolver.ResolveCurrentUserPlan(platform);
            ProcessStartInfo startInfo = BuildShellCliDetectionStartInfo(
                shell,
                platform,
                pathSetupPlan,
                Environment.GetEnvironmentVariable(CliConstants.POSIX_PATH_ENVIRONMENT_VARIABLE));

            string output = ExecuteAndGetOutput(startInfo, ct);
            return ParseShellCliInstallationOutput(output);
        }

        internal static ProcessStartInfo BuildShellCliDetectionStartInfo(
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
                    NativeCliInstaller.BuildPathWithoutInstallDirectory(
                        currentPath,
                        pathSetupPlan.InstallDirectory,
                        platform);
            }

            return startInfo;
        }

        internal static bool IsShellDetectionUsableForPathSetup(
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

        internal static string BuildShellCliDetectionCommandForShell(
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

        internal static string BuildShellCliDetectionCommand(string executableName)
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

        internal static CliInstallationDetection ParseShellCliInstallationOutput(string output)
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

        private static CliInstallationDetection ParseCliContractOutput(string output, string executablePath)
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

        private static string ExecuteAndGetOutput(ProcessStartInfo startInfo, CancellationToken ct)
        {
            UnityEngine.Debug.Assert(startInfo != null, "startInfo must not be null");
            UnityEngine.Debug.Assert(startInfo.RedirectStandardOutput, "RedirectStandardOutput must be true");
            UnityEngine.Debug.Assert(startInfo.RedirectStandardError, "RedirectStandardError must be true");

            Process process = ProcessStartHelper.TryStart(startInfo);
            if (process == null)
            {
                return null;
            }

            using (process)
            {
                StringBuilder outputBuilder = new();

                process.OutputDataReceived += (sender, e) =>
                {
                    if (e.Data != null)
                    {
                        outputBuilder.AppendLine(e.Data);
                    }
                };
                process.ErrorDataReceived += (sender, e) => { };

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                using CancellationTokenRegistration registration = ct.Register(() => KillProcessIfRunning(process));
                bool exited = process.WaitForExit(PROCESS_TIMEOUT_MS);
                if (!exited)
                {
                    KillProcessIfRunning(process);
                    return null;
                }

                process.WaitForExit();
                return outputBuilder.ToString().Trim();
            }
        }

        internal static void KillProcessIfRunning(Process process)
        {
            UnityEngine.Debug.Assert(process != null, "process must not be null");

            try
            {
                process.Kill();
            }
            catch (System.InvalidOperationException)
            {
            }
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

        private static CliInstallationDetection DetectCliInstallationAtExecutablePath(
            string executablePath,
            CancellationToken ct)
        {
            string fileName = executablePath ?? CliConstants.EXECUTABLE_NAME;
            string contractOutput = ExecuteCliVersionCommand(fileName, CliConstants.VERSION_FLAG + " " + CliConstants.JSON_FLAG, ct);
            CliInstallationDetection contractDetection = ParseCliContractOutput(contractOutput, executablePath);
            if (!string.IsNullOrEmpty(contractDetection.Version))
            {
                return contractDetection;
            }

            string versionOutput = ExecuteCliVersionCommand(fileName, CliConstants.VERSION_FLAG, ct);
            string version = string.IsNullOrEmpty(versionOutput) ? null : versionOutput;
            return new CliInstallationDetection(version, executablePath);
        }

        private static string ExecuteCliVersionCommand(
            string fileName,
            string arguments,
            CancellationToken ct)
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            Process process = ProcessStartHelper.TryStart(startInfo);
            if (process == null)
            {
                return null;
            }

            StringBuilder outputBuilder = new();

            process.OutputDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    outputBuilder.Append(e.Data);
                }
            };
            process.ErrorDataReceived += (sender, e) => { };

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using CancellationTokenRegistration registration = ct.Register(() =>
            {
                KillProcessIfRunning(process);
            });

            try
            {
                bool exited = process.WaitForExit(PROCESS_TIMEOUT_MS);

                if (!exited)
                {
                    KillProcessIfRunning(process);
                    process.Dispose();
                    return null;
                }

                // Parameterless WaitForExit flushes async output buffers
                process.WaitForExit();

                string output = outputBuilder.ToString().Trim();
                bool failed = process.ExitCode != 0 || string.IsNullOrEmpty(output);
                process.Dispose();

                return failed ? null : output;
            }
            catch (Exception ex)
            {
                process.Dispose();
                if (!ct.IsCancellationRequested)
                {
                    UnityEngine.Debug.LogWarning($"[UnityCliLoop] Failed to detect CLI version: {ex.Message}");
                }
                return null;
            }
        }
    }
}
