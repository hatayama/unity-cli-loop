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
        internal const int UNINSTALL_COMPLETION_TIMEOUT_MS = 30000;
        private const string WINDOWS_FILE_PATH_SEPARATOR = "\\";
        private const string POSIX_FILE_PATH_SEPARATOR = "/";

        public static NativeCliInstallCommand GetInstallCommand(
            RuntimePlatform platform,
            string cliReleaseTag,
            bool removeLegacyLaunchers)
        {
            return BuildInstallCommand(
                platform,
                cliReleaseTag,
                removeLegacyLaunchers,
                NodeEnvironmentResolver.GetUserShell(),
                UnityCliLoopConstants.PackageResolvedPath);
        }

        internal static NativeCliInstallCommand BuildInstallCommand(
            RuntimePlatform platform,
            string cliReleaseTag,
            bool removeLegacyLaunchers,
            string posixShellPath)
        {
            return BuildInstallCommand(
                platform,
                cliReleaseTag,
                removeLegacyLaunchers,
                posixShellPath,
                null);
        }

        internal static NativeCliInstallCommand BuildInstallCommand(
            RuntimePlatform platform,
            string cliReleaseTag,
            bool removeLegacyLaunchers,
            string posixShellPath,
            string packageResolvedPath)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(cliReleaseTag), "cliReleaseTag must not be null or empty");
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(posixShellPath), "posixShellPath must not be null or empty");
            _ = removeLegacyLaunchers;

            string releaseTag = BuildReleaseTag(cliReleaseTag);
            if (platform == RuntimePlatform.WindowsEditor)
            {
                string localScriptPath = ResolvePackageLocalInstallerScriptPath(
                    packageResolvedPath,
                    CliConstants.WINDOWS_INSTALL_SCRIPT_NAME);
                string command = string.IsNullOrEmpty(localScriptPath)
                    ? BuildWindowsRemoteInstallScriptCommand(
                        BuildInstallerScriptUrl(releaseTag, CliConstants.WINDOWS_INSTALL_SCRIPT_NAME),
                        releaseTag)
                    : BuildWindowsLocalInstallScriptCommand(localScriptPath, releaseTag);
                return new NativeCliInstallCommand(
                    "powershell",
                    $"-NoProfile -ExecutionPolicy Bypass -Command \"{command}\"",
                    command);
            }

            string posixLocalScriptPath = ResolvePackageLocalInstallerScriptPath(
                packageResolvedPath,
                CliConstants.POSIX_INSTALL_SCRIPT_NAME);
            string posixCommand = string.IsNullOrEmpty(posixLocalScriptPath)
                ? BuildPosixRemoteInstallScriptCommand(
                    BuildInstallerScriptUrl(releaseTag, CliConstants.POSIX_INSTALL_SCRIPT_NAME),
                    releaseTag)
                : BuildPosixLocalInstallScriptCommand(posixLocalScriptPath, releaseTag);
            string loginShellCommand = BuildLoginShellPosixInstallScriptCommand(posixCommand);
            return new NativeCliInstallCommand(
                posixShellPath,
                $"-l -i -c {QuoteProcessArgument(loginShellCommand)}",
                loginShellCommand);
        }

        public static async Task<CliInstallResult> InstallAsync(
            RuntimePlatform platform,
            string cliReleaseTag,
            CancellationToken ct)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(cliReleaseTag), "cliReleaseTag must not be null or empty");
            ct.ThrowIfCancellationRequested();

            string installDirectory = GetInstallDirectoryForCurrentUser(platform);
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

            string installDirectory = GetInstallDirectoryForCurrentUser(platform);
            if (string.IsNullOrWhiteSpace(installDirectory))
            {
                return new CliInstallResult(
                    false,
                    $"Could not resolve the global CLI install directory. Set {CliConstants.INSTALL_DIR_ENVIRONMENT_VARIABLE} and try again.");
            }

            NativeCliInstallCommand command = BuildUninstallCommand(installDirectory, platform);
            CliInstallResult result = await Task.Run(
                () => RunUninstallCommand(command, installDirectory, ct, INSTALL_PROCESS_TIMEOUT_MS),
                ct);
            if (!result.Success)
            {
                return result;
            }

            string installPath = GetGlobalCliInstallPath(installDirectory, platform);
            CliInstallResult removalResult = await WaitForUninstallCompletionAsync(
                installPath,
                installDirectory,
                platform,
                ct,
                UNINSTALL_COMPLETION_TIMEOUT_MS,
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

        internal static NativeCliInstallCommand BuildUninstallCommand(
            string installDirectory,
            RuntimePlatform platform)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(installDirectory), "installDirectory must not be null or empty");

            string installPath = GetGlobalCliInstallPath(installDirectory, platform);
            return new NativeCliInstallCommand(
                installPath,
                "uninstall",
                $"{QuoteProcessArgument(installPath)} uninstall");
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

        internal static async Task<CliInstallResult> WaitForUninstallTargetRemovalAsync(
            string targetPath,
            CancellationToken ct,
            int timeoutMs,
            int pollMs,
            Func<string, bool> fileExists,
            Func<int, CancellationToken, Task> delayAsync)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(targetPath), "targetPath must not be null or empty");
            UnityEngine.Debug.Assert(timeoutMs > 0, "timeoutMs must be greater than zero");
            UnityEngine.Debug.Assert(pollMs > 0, "pollMs must be greater than zero");
            UnityEngine.Debug.Assert(fileExists != null, "fileExists must not be null");
            UnityEngine.Debug.Assert(delayAsync != null, "delayAsync must not be null");

            int elapsedMs = 0;
            while (fileExists(targetPath))
            {
                ct.ThrowIfCancellationRequested();
                if (elapsedMs >= timeoutMs)
                {
                    return new CliInstallResult(
                        false,
                        $"Timed out waiting for uLoop CLI uninstall to remove {targetPath}.");
                }

                int delayMs = Math.Min(pollMs, timeoutMs - elapsedMs);
                await delayAsync(delayMs, ct);
                elapsedMs += delayMs;
            }

            return new CliInstallResult(true, "");
        }

        internal static async Task<CliInstallResult> WaitForUninstallCompletionAsync(
            string targetPath,
            string installDirectory,
            RuntimePlatform platform,
            CancellationToken ct,
            int timeoutMs,
            int pollMs,
            Func<string, bool> fileExists,
            bool requireUserPathRemoval,
            Func<string, EnvironmentVariableTarget, string> getEnvironmentVariable,
            Func<int, CancellationToken, Task> delayAsync)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(targetPath), "targetPath must not be null or empty");
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(installDirectory), "installDirectory must not be null or empty");
            UnityEngine.Debug.Assert(timeoutMs > 0, "timeoutMs must be greater than zero");
            UnityEngine.Debug.Assert(pollMs > 0, "pollMs must be greater than zero");
            UnityEngine.Debug.Assert(fileExists != null, "fileExists must not be null");
            UnityEngine.Debug.Assert(getEnvironmentVariable != null, "getEnvironmentVariable must not be null");
            UnityEngine.Debug.Assert(delayAsync != null, "delayAsync must not be null");

            int elapsedMs = 0;
            while (true)
            {
                bool targetStillExists = fileExists(targetPath);
                bool userPathStillContainsInstallDirectory = ShouldWaitForUserPathRemoval(
                    requireUserPathRemoval,
                    installDirectory,
                    platform,
                    getEnvironmentVariable);
                if (!targetStillExists && !userPathStillContainsInstallDirectory)
                {
                    return new CliInstallResult(true, "");
                }

                ct.ThrowIfCancellationRequested();
                if (elapsedMs >= timeoutMs)
                {
                    return new CliInstallResult(
                        false,
                        BuildUninstallCompletionTimeoutFailure(
                            targetPath,
                            installDirectory,
                            platform,
                            targetStillExists,
                            userPathStillContainsInstallDirectory));
                }

                int delayMs = Math.Min(pollMs, timeoutMs - elapsedMs);
                await delayAsync(delayMs, ct);
                elapsedMs += delayMs;
            }
        }

        internal static string GetGlobalCliInstallPath(string installDirectory, RuntimePlatform platform)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(installDirectory), "installDirectory must not be null or empty");

            string separator = GetFilePathSeparator(platform);
            string normalizedInstallDirectory = installDirectory.TrimEnd('\\', '/');
            return normalizedInstallDirectory + separator + GetGlobalCliInstallFileName(platform);
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

        internal static string BuildPathWithInstallDirectory(
            string currentPath,
            string installDirectory,
            RuntimePlatform platform)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(installDirectory), "installDirectory must not be null or empty");

            string normalizedPath = currentPath ?? "";
            if (string.IsNullOrEmpty(normalizedPath))
            {
                return installDirectory;
            }

            string separator = GetPathSeparator(platform);
            string[] entries = normalizedPath.Split(
                new[] { separator },
                StringSplitOptions.RemoveEmptyEntries);
            StringComparison comparison = GetPathComparison(platform);
            StringBuilder builder = new(installDirectory);
            foreach (string entry in entries)
            {
                if (string.Equals(entry, installDirectory, comparison))
                {
                    continue;
                }

                builder.Append(separator);
                builder.Append(entry);
            }

            return builder.ToString();
        }

        internal static string BuildPathWithoutInstallDirectory(
            string currentPath,
            string installDirectory,
            RuntimePlatform platform)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(installDirectory), "installDirectory must not be null or empty");

            string normalizedPath = currentPath ?? "";
            if (string.IsNullOrEmpty(normalizedPath))
            {
                return "";
            }

            string separator = GetPathSeparator(platform);
            string[] entries = normalizedPath.Split(
                new[] { separator },
                StringSplitOptions.RemoveEmptyEntries);
            string normalizedInstallDirectory = NormalizePathForComparison(installDirectory, platform);
            StringComparison comparison = GetPathComparison(platform);
            StringBuilder builder = new StringBuilder();
            foreach (string entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry))
                {
                    continue;
                }

                string normalizedEntry = NormalizePathForComparison(entry, platform);
                if (string.Equals(normalizedEntry, normalizedInstallDirectory, comparison))
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append(separator);
                }

                builder.Append(entry);
            }

            return builder.ToString();
        }

        internal static string GetDefaultInstallDirectoryFromRoots(
            RuntimePlatform platform,
            string homeDirectory,
            string localAppData)
        {
            if (platform == RuntimePlatform.WindowsEditor)
            {
                if (string.IsNullOrWhiteSpace(localAppData))
                {
                    return null;
                }

                return Path.Combine(
                    localAppData,
                    CliConstants.WINDOWS_PROGRAMS_DIR_NAME,
                    CliConstants.NATIVE_INSTALL_DIR_NAME,
                    CliConstants.NATIVE_INSTALL_BIN_DIR_NAME);
            }

            if (string.IsNullOrWhiteSpace(homeDirectory))
            {
                return null;
            }

            return Path.Combine(
                homeDirectory,
                CliConstants.POSIX_LOCAL_DIR_NAME,
                CliConstants.NATIVE_INSTALL_BIN_DIR_NAME);
        }

        private static void ApplyInstallDirectoryToCurrentProcessPath(RuntimePlatform platform)
        {
            string installDirectory = GetInstallDirectoryForCurrentUser(platform);
            if (string.IsNullOrEmpty(installDirectory))
            {
                return;
            }

            string pathVariableName = GetPathEnvironmentVariableName(platform);
            string currentPath = Environment.GetEnvironmentVariable(pathVariableName);
            string updatedPath = BuildPathWithInstallDirectory(currentPath, installDirectory, platform);
            Environment.SetEnvironmentVariable(pathVariableName, updatedPath);
        }

        private static void RemoveInstallDirectoryFromCurrentProcessPath(RuntimePlatform platform)
        {
            string installDirectory = GetInstallDirectoryForCurrentUser(platform);
            if (string.IsNullOrEmpty(installDirectory))
            {
                return;
            }

            string pathVariableName = GetPathEnvironmentVariableName(platform);
            string currentPath = Environment.GetEnvironmentVariable(pathVariableName);
            string updatedPath = BuildPathWithoutInstallDirectory(currentPath, installDirectory, platform);
            Environment.SetEnvironmentVariable(pathVariableName, updatedPath);
        }

        internal static bool IsPackageOwnedCurrentUserInstallPath(
            string executablePath,
            RuntimePlatform platform)
        {
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return false;
            }

            string installDirectory = GetInstallDirectoryForCurrentUser(platform);
            if (string.IsNullOrWhiteSpace(installDirectory))
            {
                return false;
            }

            return IsPackageOwnedInstallPath(executablePath, installDirectory, platform);
        }

        internal static bool IsPackageOwnedInstallPath(
            string executablePath,
            string installDirectory,
            RuntimePlatform platform)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(installDirectory), "installDirectory must not be null or empty");

            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return false;
            }

            string expectedPath = GetGlobalCliInstallPath(installDirectory, platform);
            string normalizedExecutablePath = NormalizePathForComparison(executablePath, platform);
            string normalizedExpectedPath = NormalizePathForComparison(expectedPath, platform);
            return string.Equals(
                normalizedExecutablePath,
                normalizedExpectedPath,
                GetPathComparison(platform));
        }

        internal static string GetCurrentUserGlobalCliInstallPath(RuntimePlatform platform)
        {
            string installDirectory = GetInstallDirectoryForCurrentUser(platform);
            return string.IsNullOrWhiteSpace(installDirectory)
                ? null
                : GetGlobalCliInstallPath(installDirectory, platform);
        }

        internal static string GetCurrentUserGlobalCliInstallDirectory(RuntimePlatform platform)
        {
            return GetInstallDirectoryForCurrentUser(platform);
        }

        internal static bool HasPackageOwnedCurrentUserInstall(RuntimePlatform platform)
        {
            string executablePath = GetCurrentUserGlobalCliInstallPath(platform);
            return !string.IsNullOrWhiteSpace(executablePath) && File.Exists(executablePath);
        }

        private static string GetInstallDirectoryForCurrentUser(RuntimePlatform platform)
        {
            string configuredInstallDirectory = Environment.GetEnvironmentVariable(CliConstants.INSTALL_DIR_ENVIRONMENT_VARIABLE);
            if (!string.IsNullOrWhiteSpace(configuredInstallDirectory))
            {
                return configuredInstallDirectory;
            }

            string homeDirectory = Environment.GetEnvironmentVariable(CliConstants.POSIX_HOME_ENVIRONMENT_VARIABLE);
            if (string.IsNullOrWhiteSpace(homeDirectory))
            {
                homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }

            string localAppData = Environment.GetEnvironmentVariable(CliConstants.WINDOWS_LOCAL_APPDATA_ENVIRONMENT_VARIABLE);
            return GetDefaultInstallDirectoryFromRoots(platform, homeDirectory, localAppData);
        }

        private static string GetPathEnvironmentVariableName(RuntimePlatform platform)
        {
            return platform == RuntimePlatform.WindowsEditor
                ? CliConstants.WINDOWS_PATH_ENVIRONMENT_VARIABLE
                : CliConstants.POSIX_PATH_ENVIRONMENT_VARIABLE;
        }

        private static string GetPathSeparator(RuntimePlatform platform)
        {
            return platform == RuntimePlatform.WindowsEditor
                ? CliConstants.WINDOWS_PATH_SEPARATOR
                : CliConstants.POSIX_PATH_SEPARATOR;
        }

        private static string GetFilePathSeparator(RuntimePlatform platform)
        {
            return platform == RuntimePlatform.WindowsEditor
                ? WINDOWS_FILE_PATH_SEPARATOR
                : POSIX_FILE_PATH_SEPARATOR;
        }

        private static StringComparison GetPathComparison(RuntimePlatform platform)
        {
            return platform == RuntimePlatform.WindowsEditor
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
        }

        private static string NormalizePathForComparison(string path, RuntimePlatform platform)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(path), "path must not be null or empty");

            string normalizedPath = path.Trim().Trim('"');
            if (platform != RuntimePlatform.WindowsEditor)
            {
                return normalizedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }

            return normalizedPath.TrimEnd('\\', '/').Replace('/', '\\');
        }

        private static bool DoesUserPathContainInstallDirectory(
            string installDirectory,
            RuntimePlatform platform,
            Func<string, EnvironmentVariableTarget, string> getEnvironmentVariable)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(installDirectory), "installDirectory must not be null or empty");
            UnityEngine.Debug.Assert(getEnvironmentVariable != null, "getEnvironmentVariable must not be null");

            if (platform != RuntimePlatform.WindowsEditor)
            {
                return false;
            }

            string pathVariableName = GetPathEnvironmentVariableName(platform);
            string currentUserPath = getEnvironmentVariable(pathVariableName, EnvironmentVariableTarget.User);
            return DoesPathContainInstallDirectory(currentUserPath, installDirectory, platform);
        }

        private static bool ShouldWaitForUserPathRemoval(
            bool requireUserPathRemoval,
            string installDirectory,
            RuntimePlatform platform,
            Func<string, EnvironmentVariableTarget, string> getEnvironmentVariable)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(installDirectory), "installDirectory must not be null or empty");
            UnityEngine.Debug.Assert(getEnvironmentVariable != null, "getEnvironmentVariable must not be null");

            return requireUserPathRemoval
                && DoesUserPathContainInstallDirectory(installDirectory, platform, getEnvironmentVariable);
        }

        private static bool DoesPathContainInstallDirectory(
            string currentPath,
            string installDirectory,
            RuntimePlatform platform)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(installDirectory), "installDirectory must not be null or empty");

            string normalizedPath = currentPath ?? "";
            if (string.IsNullOrEmpty(normalizedPath))
            {
                return false;
            }

            string separator = GetPathSeparator(platform);
            string[] entries = normalizedPath.Split(
                new[] { separator },
                StringSplitOptions.RemoveEmptyEntries);
            string normalizedInstallDirectory = NormalizePathForComparison(installDirectory, platform);
            StringComparison comparison = GetPathComparison(platform);
            foreach (string entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry))
                {
                    continue;
                }

                string normalizedEntry = NormalizePathForComparison(entry, platform);
                if (string.Equals(normalizedEntry, normalizedInstallDirectory, comparison))
                {
                    return true;
                }
            }

            return false;
        }

        private static string BuildUninstallCompletionTimeoutFailure(
            string targetPath,
            string installDirectory,
            RuntimePlatform platform,
            bool targetStillExists,
            bool userPathStillContainsInstallDirectory)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(targetPath), "targetPath must not be null or empty");
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(installDirectory), "installDirectory must not be null or empty");
            UnityEngine.Debug.Assert(targetStillExists || userPathStillContainsInstallDirectory, "at least one uninstall cleanup condition must still be pending");

            if (platform != RuntimePlatform.WindowsEditor || !userPathStillContainsInstallDirectory)
            {
                return $"Timed out waiting for uLoop CLI uninstall to remove {targetPath}.";
            }

            if (!targetStillExists)
            {
                return $"Timed out waiting for uLoop CLI uninstall to remove {installDirectory} from Windows User PATH.";
            }

            return $"Timed out waiting for uLoop CLI uninstall to remove {targetPath} and remove {installDirectory} from Windows User PATH.";
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

        private static string GetGlobalCliInstallFileName(RuntimePlatform platform)
        {
            return platform == RuntimePlatform.WindowsEditor
                ? CliConstants.GLOBAL_WINDOWS_COMMAND_NAME
                : CliConstants.GLOBAL_UNIX_COMMAND_NAME;
        }

        private static string BuildPosixRemoteInstallScriptCommand(string scriptUrl, string releaseTag)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(scriptUrl), "scriptUrl must not be null or empty");
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(releaseTag), "releaseTag must not be null or empty");

            return "tmp_script=$(mktemp) && "
                + "trap 'rm -f \"$tmp_script\"' EXIT && "
                + $"curl -fsSL {QuotePosixShellValue(scriptUrl)} -o \"$tmp_script\" && "
                + $"{CliConstants.INSTALL_VERSION_ENVIRONMENT_VARIABLE}={QuotePosixShellValue(releaseTag)} sh \"$tmp_script\"";
        }

        private static string BuildPosixLocalInstallScriptCommand(string scriptPath, string releaseTag)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(scriptPath), "scriptPath must not be null or empty");
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(releaseTag), "releaseTag must not be null or empty");

            return $"{CliConstants.INSTALL_VERSION_ENVIRONMENT_VARIABLE}={QuotePosixShellValue(releaseTag)} sh {QuotePosixShellValue(scriptPath)}";
        }

        internal static string BuildLoginShellPosixInstallScriptCommand(string posixCommand)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(posixCommand), "posixCommand must not be null or empty");
            return $"{CliConstants.POSIX_SHELL_EXECUTABLE_PATH} -c {QuotePosixShellValue(posixCommand)}";
        }

        private static string BuildWindowsRemoteInstallScriptCommand(string scriptUrl, string releaseTag)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(scriptUrl), "scriptUrl must not be null or empty");
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(releaseTag), "releaseTag must not be null or empty");

            return $"$env:{CliConstants.INSTALL_VERSION_ENVIRONMENT_VARIABLE}={QuotePowerShellSingleQuotedValue(releaseTag)}; "
                + $"irm {QuotePowerShellSingleQuotedValue(scriptUrl)} | iex";
        }

        private static string BuildWindowsLocalInstallScriptCommand(string scriptPath, string releaseTag)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(scriptPath), "scriptPath must not be null or empty");
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(releaseTag), "releaseTag must not be null or empty");

            return $"$env:{CliConstants.INSTALL_VERSION_ENVIRONMENT_VARIABLE}={QuotePowerShellSingleQuotedValue(releaseTag)}; "
                + $"& {QuotePowerShellSingleQuotedValue(scriptPath)}";
        }

        internal static string ResolvePackageLocalInstallerScriptPath(string packageResolvedPath, string assetName)
        {
            if (string.IsNullOrWhiteSpace(packageResolvedPath) || string.IsNullOrWhiteSpace(assetName))
            {
                return null;
            }

            DirectoryInfo packageDirectory = new(packageResolvedPath);
            DirectoryInfo packagesDirectory = packageDirectory.Parent;
            DirectoryInfo repositoryDirectory = packagesDirectory?.Parent;
            if (repositoryDirectory == null)
            {
                return null;
            }

            if (!string.Equals(packageDirectory.Name, CliConstants.PACKAGE_SOURCE_DIR_NAME, StringComparison.Ordinal)
                || !string.Equals(packagesDirectory.Name, CliConstants.UNITY_PACKAGES_DIR_NAME, StringComparison.Ordinal))
            {
                return null;
            }

            string scriptPath = Path.Combine(repositoryDirectory.FullName, CliConstants.SCRIPTS_DIR_NAME, assetName);
            if (!File.Exists(scriptPath))
            {
                return null;
            }

            return scriptPath;
        }

        private static string QuotePosixShellValue(string value)
        {
            UnityEngine.Debug.Assert(value != null, "value must not be null");
            return $"'{value.Replace("'", "'\"'\"'")}'";
        }

        private static string QuotePowerShellSingleQuotedValue(string value)
        {
            UnityEngine.Debug.Assert(value != null, "value must not be null");
            return $"'{value.Replace("'", "''")}'";
        }

        private static string QuoteProcessArgument(string value)
        {
            UnityEngine.Debug.Assert(value != null, "value must not be null");
            return $"\"{value.Replace("\"", "\\\"")}\"";
        }

        private static string BuildReleaseTag(string cliReleaseTag)
        {
            if (cliReleaseTag.StartsWith(CliConstants.DISPATCHER_RELEASE_TAG_PREFIX, StringComparison.Ordinal))
            {
                return cliReleaseTag;
            }
            if (cliReleaseTag.StartsWith(CliConstants.RELEASE_TAG_PREFIX, StringComparison.Ordinal))
            {
                return $"{CliConstants.DISPATCHER_RELEASE_TAG_PREFIX}{cliReleaseTag.Substring(CliConstants.RELEASE_TAG_PREFIX.Length)}";
            }
            return $"{CliConstants.DISPATCHER_RELEASE_TAG_PREFIX}{cliReleaseTag}";
        }

        internal static string BuildInstallerScriptUrl(string releaseTag, string assetName)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(releaseTag), "releaseTag must not be null or empty");
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(assetName), "assetName must not be null or empty");

            return $"{CliConstants.RAW_CONTENT_BASE_URL}/{releaseTag}/{CliConstants.SCRIPTS_DIR_NAME}/{assetName}";
        }
    }

    /// <summary>
    /// Provides Native CLI Installer operations for its owning module.
    /// </summary>
    public sealed class NativeCliInstallerService : INativeCliInstaller
    {
        public bool IsPackageOwnedCurrentUserInstallPath(string cliExecutablePath, RuntimePlatform platform)
        {
            return NativeCliInstaller.IsPackageOwnedCurrentUserInstallPath(cliExecutablePath, platform);
        }

        public bool HasPackageOwnedCurrentUserInstall(RuntimePlatform platform)
        {
            return NativeCliInstaller.HasPackageOwnedCurrentUserInstall(platform);
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
