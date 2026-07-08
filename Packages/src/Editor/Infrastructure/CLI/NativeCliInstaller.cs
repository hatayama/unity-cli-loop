using System;
using System.Diagnostics;
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
    /// <summary>
    /// Installs the package-owned global CLI through the same installer scripts used by CLI commands.
    /// </summary>
    public static class NativeCliInstaller
    {
        private const int INSTALL_PROCESS_TIMEOUT_MS = 300000;
        private const int INSTALL_PROCESS_WAIT_SLICE_MS = 250;

        public static NativeCliInstallCommand GetInstallCommand(
            RuntimePlatform platform,
            string cliReleaseTag,
            bool removeLegacyLaunchers)
        {
            return NativeCliCommandBuilder.BuildInstallCommandWithPackagePath(
                platform,
                cliReleaseTag,
                removeLegacyLaunchers,
                NodeEnvironmentResolver.GetUserShell(),
                UnityCliLoopConstants.PackageResolvedPath);
        }

        public static async Task<CliInstallResult> InstallAsync(
            RuntimePlatform platform,
            string cliReleaseTag,
            CancellationToken ct)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(cliReleaseTag), "cliReleaseTag must not be null or empty");
            ct.ThrowIfCancellationRequested();

            string installDirectory = NativeCliInstallPathResolver.GetInstallDirectoryForCurrentUser(platform);
            if (string.IsNullOrWhiteSpace(installDirectory))
            {
                return new CliInstallResult(
                    false,
                    $"Could not resolve the global CLI install directory. Set {CliConstants.INSTALL_DIR_ENVIRONMENT_VARIABLE} and try again.");
            }

            NativeCliInstallCommand command = GetInstallCommand(platform, cliReleaseTag, true);
            CliInstallResult result = await Task.Run(
                () => RunInstallCommand(command, ct, INSTALL_PROCESS_TIMEOUT_MS),
                ct);

            if (result.Success)
            {
                result = FinishSuccessfulInstall(
                    result,
                    installDirectory,
                    platform,
                    ApplyInstallDirectoryToCurrentProcessPath);
            }

            return result;
        }

        public static async Task<CliInstallResult> UninstallAsync(RuntimePlatform platform, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            string installDirectory = NativeCliInstallPathResolver.GetInstallDirectoryForCurrentUser(platform);
            if (string.IsNullOrWhiteSpace(installDirectory))
            {
                return new CliInstallResult(
                    false,
                    $"Could not resolve the global CLI install directory. Set {CliConstants.INSTALL_DIR_ENVIRONMENT_VARIABLE} and try again.");
            }

            NativeCliInstallCommand command = NativeCliCommandBuilder.BuildUninstallCommand(
                installDirectory,
                platform);
            CliInstallResult result = await Task.Run(
                () => RunUninstallCommand(command, installDirectory, ct, INSTALL_PROCESS_TIMEOUT_MS),
                ct);
            if (!result.Success)
            {
                return result;
            }

            string installPath = NativeCliInstallPathResolver.GetGlobalCliInstallPath(installDirectory, platform);
            CliInstallResult removalResult = await NativeCliUninstallCompletionWaiter.WaitForUninstallCompletionAsync(
                installPath,
                installDirectory,
                platform,
                ct,
                NativeCliUninstallCompletionWaiter.UNINSTALL_COMPLETION_TIMEOUT_MS,
                INSTALL_PROCESS_WAIT_SLICE_MS,
                File.Exists,
                false,
                Environment.GetEnvironmentVariable,
                Task.Delay);
            if (!removalResult.Success)
            {
                return removalResult;
            }

            RemoveInstallDirectoryFromCurrentProcessPath(platform);
            return result;
        }

        internal static CliInstallResult RunInstallCommand(
            NativeCliInstallCommand command,
            CancellationToken ct,
            int timeoutMs)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(command.FileName), "command.FileName must not be null or empty");
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(command.Arguments), "command.Arguments must not be null or empty");
            UnityEngine.Debug.Assert(timeoutMs > 0, "timeoutMs must be greater than zero");
            ct.ThrowIfCancellationRequested();

            return RunCliSetupCommand(
                command,
                ct,
                timeoutMs,
                "release CLI installer",
                startInfo => { });
        }

        internal static CliInstallResult RunUninstallCommand(
            NativeCliInstallCommand command,
            string installDirectory,
            CancellationToken ct,
            int timeoutMs)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(installDirectory), "installDirectory must not be null or empty");

            return RunCliSetupCommand(
                command,
                ct,
                timeoutMs,
                "global CLI uninstall command",
                startInfo =>
                {
                    startInfo.EnvironmentVariables[CliConstants.INSTALL_DIR_ENVIRONMENT_VARIABLE] = installDirectory;
                });
        }

        private static CliInstallResult RunCliSetupCommand(
            NativeCliInstallCommand command,
            CancellationToken ct,
            int timeoutMs,
            string commandDescription,
            Action<ProcessStartInfo> configureStartInfo)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(command.FileName), "command.FileName must not be null or empty");
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(command.Arguments), "command.Arguments must not be null or empty");
            UnityEngine.Debug.Assert(timeoutMs > 0, "timeoutMs must be greater than zero");
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(commandDescription), "commandDescription must not be null or empty");
            UnityEngine.Debug.Assert(configureStartInfo != null, "configureStartInfo must not be null");
            ct.ThrowIfCancellationRequested();

            ProcessStartInfo startInfo = new()
            {
                FileName = command.FileName,
                Arguments = command.Arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            configureStartInfo(startInfo);

            Process process = ProcessStartHelper.TryStart(startInfo);
            if (process == null)
            {
                return new CliInstallResult(
                    false,
                    $"Failed to start {commandDescription}: {command.FileName}");
            }

            StringBuilder standardOutputBuilder = new();
            StringBuilder errorOutputBuilder = new();
            process.OutputDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    standardOutputBuilder.AppendLine(e.Data);
                }
            };
            process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    errorOutputBuilder.AppendLine(e.Data);
                }
            };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            bool canceled;
            bool exited = WaitForInstallProcessExit(process, ct, timeoutMs, out canceled);
            if (!exited)
            {
                KillProcessIfRunning(process);
                process.WaitForExit(INSTALL_PROCESS_WAIT_SLICE_MS);
                string timedOutStandardOutput = standardOutputBuilder.ToString();
                string timedOutErrorOutput = errorOutputBuilder.ToString();
                process.Dispose();

                if (canceled)
                {
                    return new CliInstallResult(
                        false,
                        $"{BuildSentenceSubject(commandDescription)} was canceled.");
                }

                return new CliInstallResult(
                    false,
                    BuildCliSetupCommandTimeoutFailure(
                        commandDescription,
                        timeoutMs,
                        timedOutErrorOutput,
                        timedOutStandardOutput));
            }

            process.WaitForExit();
            string standardOutput = standardOutputBuilder.ToString();
            string errorOutput = errorOutputBuilder.ToString();
            bool success = process.ExitCode == 0;
            process.Dispose();

            return success
                ? new CliInstallResult(true, standardOutput)
                : new CliInstallResult(false, BuildCliSetupCommandFailure(commandDescription, errorOutput, standardOutput));
        }

        internal static CliInstallResult FinishSuccessfulInstall(
            CliInstallResult installResult,
            string installDirectory,
            RuntimePlatform platform,
            Action<RuntimePlatform> applyInstallDirectoryToCurrentProcessPath)
        {
            UnityEngine.Debug.Assert(installResult.Success, "installResult must be successful");
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(installDirectory), "installDirectory must not be null or empty");
            UnityEngine.Debug.Assert(applyInstallDirectoryToCurrentProcessPath != null, "applyInstallDirectoryToCurrentProcessPath must not be null");

            applyInstallDirectoryToCurrentProcessPath(platform);
            return installResult;
        }

        private static void ApplyInstallDirectoryToCurrentProcessPath(RuntimePlatform platform)
        {
            string installDirectory = NativeCliInstallPathResolver.GetInstallDirectoryForCurrentUser(platform);
            if (string.IsNullOrEmpty(installDirectory))
            {
                return;
            }

            string pathVariableName = NativeCliInstallPathResolver.GetPathEnvironmentVariableName(platform);
            string currentPath = Environment.GetEnvironmentVariable(pathVariableName);
            string updatedPath = NativeCliInstallPathResolver.BuildPathWithInstallDirectory(
                currentPath,
                installDirectory,
                platform);
            Environment.SetEnvironmentVariable(pathVariableName, updatedPath);
        }

        private static void RemoveInstallDirectoryFromCurrentProcessPath(RuntimePlatform platform)
        {
            string installDirectory = NativeCliInstallPathResolver.GetInstallDirectoryForCurrentUser(platform);
            if (string.IsNullOrEmpty(installDirectory))
            {
                return;
            }

            string pathVariableName = NativeCliInstallPathResolver.GetPathEnvironmentVariableName(platform);
            string currentPath = Environment.GetEnvironmentVariable(pathVariableName);
            string updatedPath = NativeCliInstallPathResolver.BuildPathWithoutInstallDirectory(
                currentPath,
                installDirectory,
                platform);
            Environment.SetEnvironmentVariable(pathVariableName, updatedPath);
        }

        private static string BuildCliSetupCommandFailure(
            string commandDescription,
            string errorOutput,
            string standardOutput)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(commandDescription), "commandDescription must not be null or empty");

            if (!string.IsNullOrWhiteSpace(errorOutput))
            {
                return errorOutput;
            }

            if (!string.IsNullOrWhiteSpace(standardOutput))
            {
                return standardOutput;
            }

            return $"{BuildSentenceSubject(commandDescription)} failed without output.";
        }

        private static string BuildCliSetupCommandTimeoutFailure(
            string commandDescription,
            int timeoutMs,
            string errorOutput,
            string standardOutput)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(commandDescription), "commandDescription must not be null or empty");

            string capturedOutput = BuildCliSetupCommandFailure(commandDescription, errorOutput, standardOutput);
            string noOutputMessage = $"{BuildSentenceSubject(commandDescription)} failed without output.";
            if (string.Equals(capturedOutput, noOutputMessage, StringComparison.Ordinal))
            {
                return $"{BuildSentenceSubject(commandDescription)} timed out after {timeoutMs} ms.";
            }

            return $"{BuildSentenceSubject(commandDescription)} timed out after {timeoutMs} ms.\n{capturedOutput}";
        }

        private static string BuildSentenceSubject(string value)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(value), "value must not be null or empty");

            return char.ToUpperInvariant(value[0]) + value.Substring(1);
        }

        private static bool WaitForInstallProcessExit(
            Process process,
            CancellationToken ct,
            int timeoutMs,
            out bool canceled)
        {
            UnityEngine.Debug.Assert(process != null, "process must not be null");
            UnityEngine.Debug.Assert(timeoutMs > 0, "timeoutMs must be greater than zero");

            canceled = false;
            int remainingMs = timeoutMs;
            while (remainingMs > 0)
            {
                if (ct.IsCancellationRequested)
                {
                    canceled = true;
                    return false;
                }

                int waitMs = Math.Min(INSTALL_PROCESS_WAIT_SLICE_MS, remainingMs);
                if (process.WaitForExit(waitMs))
                {
                    return true;
                }

                remainingMs -= waitMs;
            }

            return false;
        }

        private static void KillProcessIfRunning(Process process)
        {
            UnityEngine.Debug.Assert(process != null, "process must not be null");

            try
            {
                if (process.HasExited)
                {
                    return;
                }

                process.Kill();
            }
            catch (InvalidOperationException)
            {
                // Process exit can race with Kill, and timeout/cancel still needs to return a CliInstallResult.
            }
        }

    }

    /// <summary>
    /// Provides Native CLI Installer operations for its owning module.
    /// </summary>
    public sealed class NativeCliInstallerService : INativeCliInstaller
    {
        public bool IsPackageOwnedCurrentUserInstallPath(string cliExecutablePath, RuntimePlatform platform)
        {
            return NativeCliInstallPathResolver.IsPackageOwnedCurrentUserInstallPath(cliExecutablePath, platform);
        }

        public bool HasPackageOwnedCurrentUserInstall(RuntimePlatform platform)
        {
            return NativeCliInstallPathResolver.HasPackageOwnedCurrentUserInstall(platform);
        }

        public Task<CliInstallResult> InstallGlobalCliAsync(
            RuntimePlatform platform,
            string cliReleaseTag,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return NativeCliInstaller.InstallAsync(platform, cliReleaseTag, ct);
        }

        public Task<CliInstallResult> UninstallGlobalCliAsync(RuntimePlatform platform, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return NativeCliInstaller.UninstallAsync(platform, ct);
        }

        public Task<CliPathSetupPlan> GetGlobalCliPathSetupPlanAsync(RuntimePlatform platform, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(CliPathSetupProfileResolver.ResolveCurrentUserPlan(platform));
        }

        public CliPathSetupApplyResult ApplyGlobalCliPathSetup(CliPathSetupPlan plan)
        {
            return CliPathSetupWriter.ApplyToFileSystem(plan);
        }

        public NativeCliInstallCommand GetGlobalCliInstallCommand(
            RuntimePlatform platform,
            string cliReleaseTag,
            bool removeLegacyLaunchers)
        {
            return NativeCliInstaller.GetInstallCommand(platform, cliReleaseTag, removeLegacyLaunchers);
        }
    }
}
