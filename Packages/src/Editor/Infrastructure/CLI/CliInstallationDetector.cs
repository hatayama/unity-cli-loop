using System.Diagnostics;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    internal readonly struct CliInstallationDetection
    {
        public CliInstallationDetection(string version, string executablePath)
        {
            Version = version;
            ExecutablePath = executablePath;
        }

        public string Version { get; }
        public string ExecutablePath { get; }
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
        private const string SHELL_SUCCESS_EXIT_CODE = "0";

        private string _cachedCliVersion;
        private string _cachedCliExecutablePath;
        private bool _cacheInitialized;
        private bool _isRefreshing;

        public bool IsCliInstalled()
        {
            return GetCachedCliVersion() != null;
        }

        public string GetCachedCliVersion()
        {
            return _cacheInitialized ? _cachedCliVersion : null;
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
            _cachedCliExecutablePath = detection.ExecutablePath;
            _cacheInitialized = true;
        }

        public void InvalidateCache()
        {
            _cachedCliVersion = null;
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
                return DetectShellCliInstallationFromLoginShell(ct);
            }

            string executablePath = NodeEnvironmentResolver.FindExecutablePathAtPlatform(
                CliConstants.EXECUTABLE_NAME,
                platform);
            return DetectCliInstallationAtExecutablePath(executablePath, ct);
        }

        private static CliInstallationDetection DetectShellCliInstallationFromLoginShell(CancellationToken ct)
        {
            string shell = NodeEnvironmentResolver.GetUserShell();
            ProcessStartInfo startInfo = new()
            {
                FileName = shell,
                Arguments = "-l -i -c " + QuoteProcessArgument(BuildShellCliDetectionCommand(CliConstants.EXECUTABLE_NAME)),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            string output = ExecuteAndGetOutput(startInfo, ct);
            return ParseShellCliInstallationOutput(output);
        }

        internal static string BuildShellCliDetectionCommand(string executableName)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(executableName), "executableName must not be null or empty");

            return "echo " + SHELL_PATH_START_MARKER + "\n"
                + "command -v " + executableName + "\n"
                + "echo " + SHELL_PATH_END_MARKER + "\n"
                + "echo " + SHELL_VERSION_START_MARKER + "\n"
                + executableName + " " + CliConstants.SHORT_VERSION_FLAG + "\n"
                + "uloop_version_status=$?\n"
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
            string executablePath = NodeEnvironmentResolver.ExtractAbsolutePathLine(pathBlock);
            string version = IsSuccessfulShellStatus(versionStatusBlock)
                ? ExtractFirstNonEmptyLine(versionBlock)
                : null;
            return new CliInstallationDetection(version, executablePath);
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

            ProcessStartInfo startInfo = new()            {
                FileName = fileName,
                Arguments = CliConstants.VERSION_FLAG,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            Process process = ProcessStartHelper.TryStart(startInfo);
            if (process == null)
            {
                return new CliInstallationDetection(null, executablePath);
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
                    return new CliInstallationDetection(null, executablePath);
                }

                // Parameterless WaitForExit flushes async output buffers
                process.WaitForExit();

                string output = outputBuilder.ToString().Trim();
                bool failed = process.ExitCode != 0 || string.IsNullOrEmpty(output);
                process.Dispose();

                string version = failed ? null : output;
                return new CliInstallationDetection(version, executablePath);
            }
            catch (Exception ex)
            {
                process.Dispose();
                if (!ct.IsCancellationRequested)
                {
                    UnityEngine.Debug.LogWarning($"[UnityCliLoop] Failed to detect CLI version: {ex.Message}");
                }
                return new CliInstallationDetection(null, executablePath);
            }
        }
    }
}
