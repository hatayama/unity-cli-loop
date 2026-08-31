using System.Diagnostics;
using System;
using System.IO;
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
                    return CliShellInstallationProbe.IsShellDetectionUsableForPathSetup(
                        detection,
                        platform,
                        NativeCliInstallPathResolver.IsPackageOwnedCurrentUserInstallPath,
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
            string executablePath = NativeCliInstallPathResolver.GetCurrentUserGlobalCliInstallPath(platform);
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
            ProcessStartInfo startInfo = CliShellInstallationProbe.BuildShellCliDetectionStartInfo(
                shell,
                platform,
                pathSetupPlan,
                Environment.GetEnvironmentVariable(CliConstants.POSIX_PATH_ENVIRONMENT_VARIABLE));

            CliDetectionCommandResult commandResult = CliDetectionCommandRunner.Execute(startInfo, ct);
            string output = commandResult == null
                ? null
                : string.Join(Environment.NewLine, commandResult.StandardOutputLines).Trim();
            return CliShellInstallationProbe.ParseShellCliInstallationOutput(output);
        }

        private static CliInstallationDetection DetectCliInstallationAtExecutablePath(
            string executablePath,
            CancellationToken ct)
        {
            string fileName = executablePath ?? CliConstants.EXECUTABLE_NAME;
            string contractOutput = ExecuteCliVersionCommand(fileName, CliConstants.VERSION_FLAG + " " + CliConstants.JSON_FLAG, ct);
            CliInstallationDetection contractDetection = CliShellInstallationProbe.ParseCliContractOutput(contractOutput, executablePath);
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

            try
            {
                CliDetectionCommandResult commandResult = CliDetectionCommandRunner.Execute(startInfo, ct);
                if (commandResult == null)
                {
                    return null;
                }

                string output = string.Concat(commandResult.StandardOutputLines).Trim();
                bool failed = commandResult.ExitCode != 0 || string.IsNullOrEmpty(output);

                return failed ? null : output;
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                {
                    UnityEngine.Debug.LogWarning($"[UnityCliLoop] Failed to detect CLI version: {ex.Message}");
                }
                return null;
            }
        }
    }
}
