using System;
using System.Diagnostics;
using System.IO;
using System.Security;
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
    /// Installs the package-owned global dispatcher through the same installer scripts used by CLI commands.
    /// </summary>
    public static class NativeCliInstaller
    {
        private const int INSTALL_PROCESS_TIMEOUT_MS = 300000;
        private const int INSTALL_PROCESS_WAIT_SLICE_MS = 250;

        public static NativeCliInstallCommand GetInstallCommand(
            RuntimePlatform platform,
            string packageVersion,
            bool removeLegacyLaunchers)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(packageVersion), "packageVersion must not be null or empty");
            _ = removeLegacyLaunchers;

            string releaseTag = BuildReleaseTag(packageVersion);
            if (platform == RuntimePlatform.WindowsEditor)
            {
                string scriptUrl = BuildInstallerScriptUrl(releaseTag, CliConstants.WINDOWS_INSTALL_SCRIPT_NAME);
                string command =
                    $"$env:{CliConstants.INSTALL_VERSION_ENVIRONMENT_VARIABLE}='{releaseTag}'; " +
                    $"irm '{scriptUrl}' | iex";
                return new NativeCliInstallCommand(
                    "powershell",
                    $"-NoProfile -ExecutionPolicy Bypass -Command \"{command}\"",
                    command);
            }

            string posixScriptUrl = BuildInstallerScriptUrl(releaseTag, CliConstants.POSIX_INSTALL_SCRIPT_NAME);
            string posixCommand = BuildPosixInstallScriptCommand(posixScriptUrl, releaseTag);
            return new NativeCliInstallCommand(
                "/bin/sh",
                $"-c {QuoteProcessArgument(posixCommand)}",
                posixCommand);
        }

        public static async Task<CliInstallResult> InstallAsync(
            RuntimePlatform platform,
            string dispatcherVersion,
            CancellationToken ct)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(dispatcherVersion), "dispatcherVersion must not be null or empty");
            ct.ThrowIfCancellationRequested();

            string installDirectory = GetInstallDirectoryForCurrentUser(platform);
            if (string.IsNullOrWhiteSpace(installDirectory))
            {
                return new CliInstallResult(
                    false,
                    $"Could not resolve the global CLI install directory. Set {CliConstants.INSTALL_DIR_ENVIRONMENT_VARIABLE} and try again.");
            }

            NativeCliInstallCommand command = GetInstallCommand(platform, dispatcherVersion, true);
            CliInstallResult result = await Task.Run(
                () => RunInstallCommand(command, ct, INSTALL_PROCESS_TIMEOUT_MS),
                ct);

            if (result.Success)
            {
                result = FinishSuccessfulInstall(
                    result,
                    installDirectory,
                    platform,
                    ApplyInstallDirectoryToCurrentProcessPath,
                    (currentInstallDirectory, currentPlatform) => PersistInstallDirectoryToUserPath(
                        currentInstallDirectory,
                        currentPlatform,
                        Environment.GetEnvironmentVariable,
                        Environment.SetEnvironmentVariable));
            }

            return result;
        }

        public static async Task<CliInstallResult> UninstallAsync(RuntimePlatform platform)
        {
            string installDirectory = GetInstallDirectoryForCurrentUser(platform);
            if (string.IsNullOrWhiteSpace(installDirectory))
            {
                return new CliInstallResult(
                    false,
                    $"Could not resolve the global CLI install directory. Set {CliConstants.INSTALL_DIR_ENVIRONMENT_VARIABLE} and try again.");
            }

            CliInstallResult result = await Task.Run(() => UninstallGlobalCli(installDirectory, platform));
            if (!result.Success)
            {
                return result;
            }

            if (!ShouldRemoveInstallDirectoryFromPath(installDirectory, platform))
            {
                return result;
            }

            RemoveInstallDirectoryFromCurrentProcessPath(installDirectory, platform);
            return RemoveInstallDirectoryFromUserPath(
                installDirectory,
                platform,
                Environment.GetEnvironmentVariable,
                Environment.SetEnvironmentVariable);
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

            ProcessStartInfo startInfo = new()            {
                FileName = command.FileName,
                Arguments = command.Arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            Process process = ProcessStartHelper.TryStart(startInfo);
            if (process == null)
            {
                return new CliInstallResult(
                    false,
                    $"Failed to start release CLI dispatcher installer: {command.FileName}");
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
                    return new CliInstallResult(false, "Release CLI dispatcher installer was canceled.");
                }

                return new CliInstallResult(
                    false,
                    BuildReleaseCliInstallTimeoutFailure(timeoutMs, timedOutErrorOutput, timedOutStandardOutput));
            }

            process.WaitForExit();
            string standardOutput = standardOutputBuilder.ToString();
            string errorOutput = errorOutputBuilder.ToString();
            bool success = process.ExitCode == 0;
            process.Dispose();

            return success
                ? new CliInstallResult(true, standardOutput)
                : new CliInstallResult(false, BuildReleaseCliInstallFailure(errorOutput, standardOutput));
        }

        internal static CliInstallResult UninstallGlobalCli(string installDirectory, RuntimePlatform platform)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(installDirectory), "installDirectory must not be null or empty");

            try
            {
                string installPath = GetGlobalCliInstallPath(installDirectory, platform);
                if (File.Exists(installPath))
                {
                    File.Delete(installPath);
                }

                DeleteStagedInstallFiles(installDirectory, platform);
                DeleteNativeInstallTreeIfEmpty(installDirectory);
                return new CliInstallResult(true, "");
            }
            catch (IOException ex)
            {
                return BuildCliUninstallFailure(ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                return BuildCliUninstallFailure(ex);
            }
            catch (ArgumentException ex)
            {
                return BuildCliUninstallFailure(ex);
            }
            catch (NotSupportedException ex)
            {
                return BuildCliUninstallFailure(ex);
            }
            catch (SecurityException ex)
            {
                return BuildCliUninstallFailure(ex);
            }
        }

        internal static string GetGlobalCliInstallPath(string installDirectory, RuntimePlatform platform)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(installDirectory), "installDirectory must not be null or empty");

            return Path.Combine(installDirectory, GetGlobalCliInstallFileName(platform));
        }

        internal static CliInstallResult PersistInstallDirectoryToUserPath(
            string installDirectory,
            RuntimePlatform platform,
            Func<string, EnvironmentVariableTarget, string> getEnvironmentVariable,
            Action<string, string, EnvironmentVariableTarget> setEnvironmentVariable)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(installDirectory), "installDirectory must not be null or empty");
            UnityEngine.Debug.Assert(getEnvironmentVariable != null, "getEnvironmentVariable must not be null");
            UnityEngine.Debug.Assert(setEnvironmentVariable != null, "setEnvironmentVariable must not be null");

            if (platform != RuntimePlatform.WindowsEditor)
            {
                return new CliInstallResult(true, "");
            }

            string pathVariableName = GetPathEnvironmentVariableName(platform);
            try
            {
                string currentUserPath = getEnvironmentVariable(pathVariableName, EnvironmentVariableTarget.User);
                string updatedUserPath = BuildPathWithInstallDirectory(currentUserPath, installDirectory, platform);
                if (string.Equals(currentUserPath, updatedUserPath, GetPathComparison(platform)))
                {
                    return new CliInstallResult(true, "");
                }

                setEnvironmentVariable(pathVariableName, updatedUserPath, EnvironmentVariableTarget.User);
                return new CliInstallResult(true, "");
            }
            catch (SecurityException ex)
            {
                return BuildUserPathPersistenceFailure(ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                return BuildUserPathPersistenceFailure(ex);
            }
        }

        internal static CliInstallResult RemoveInstallDirectoryFromUserPath(
            string installDirectory,
            RuntimePlatform platform,
            Func<string, EnvironmentVariableTarget, string> getEnvironmentVariable,
            Action<string, string, EnvironmentVariableTarget> setEnvironmentVariable)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(installDirectory), "installDirectory must not be null or empty");
            UnityEngine.Debug.Assert(getEnvironmentVariable != null, "getEnvironmentVariable must not be null");
            UnityEngine.Debug.Assert(setEnvironmentVariable != null, "setEnvironmentVariable must not be null");

            if (platform != RuntimePlatform.WindowsEditor)
            {
                return new CliInstallResult(true, "");
            }

            string pathVariableName = GetPathEnvironmentVariableName(platform);
            try
            {
                string currentUserPath = getEnvironmentVariable(pathVariableName, EnvironmentVariableTarget.User);
                string updatedUserPath = BuildPathWithoutInstallDirectory(currentUserPath, installDirectory, platform);
                if (string.Equals(currentUserPath, updatedUserPath, GetPathComparison(platform)))
                {
                    return new CliInstallResult(true, "");
                }

                setEnvironmentVariable(pathVariableName, updatedUserPath, EnvironmentVariableTarget.User);
                return new CliInstallResult(true, "");
            }
            catch (SecurityException ex)
            {
                return BuildUserPathRemovalFailure(ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                return BuildUserPathRemovalFailure(ex);
            }
        }

        internal static CliInstallResult FinishSuccessfulInstall(
            CliInstallResult installResult,
            string installDirectory,
            RuntimePlatform platform,
            Action<RuntimePlatform> applyInstallDirectoryToCurrentProcessPath,
            Func<string, RuntimePlatform, CliInstallResult> persistInstallDirectoryToUserPath)
        {
            UnityEngine.Debug.Assert(installResult.Success, "installResult must be successful");
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(installDirectory), "installDirectory must not be null or empty");
            UnityEngine.Debug.Assert(applyInstallDirectoryToCurrentProcessPath != null, "applyInstallDirectoryToCurrentProcessPath must not be null");
            UnityEngine.Debug.Assert(persistInstallDirectoryToUserPath != null, "persistInstallDirectoryToUserPath must not be null");

            applyInstallDirectoryToCurrentProcessPath(platform);
            CliInstallResult persistResult = persistInstallDirectoryToUserPath(installDirectory, platform);
            if (!persistResult.Success)
            {
                return persistResult;
            }

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
            StringComparison comparison = GetPathComparison(platform);
            StringBuilder builder = new();
            foreach (string entry in entries)
            {
                if (string.Equals(entry, installDirectory, comparison))
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

        internal static bool IsDefaultInstallDirectoryForCurrentUser(
            string installDirectory,
            RuntimePlatform platform,
            string homeDirectory,
            string localAppData)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(installDirectory), "installDirectory must not be null or empty");

            string defaultInstallDirectory = GetDefaultInstallDirectoryFromRoots(
                platform,
                homeDirectory,
                localAppData);
            if (string.IsNullOrWhiteSpace(defaultInstallDirectory))
            {
                return false;
            }

            return string.Equals(
                installDirectory,
                defaultInstallDirectory,
                GetPathComparison(platform));
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

        private static void RemoveInstallDirectoryFromCurrentProcessPath(
            string installDirectory,
            RuntimePlatform platform)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(installDirectory), "installDirectory must not be null or empty");

            string pathVariableName = GetPathEnvironmentVariableName(platform);
            string currentPath = Environment.GetEnvironmentVariable(pathVariableName);
            string updatedPath = BuildPathWithoutInstallDirectory(currentPath, installDirectory, platform);
            Environment.SetEnvironmentVariable(pathVariableName, updatedPath);
        }

        private static bool ShouldRemoveInstallDirectoryFromPath(
            string installDirectory,
            RuntimePlatform platform)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(installDirectory), "installDirectory must not be null or empty");

            string homeDirectory = Environment.GetEnvironmentVariable(CliConstants.POSIX_HOME_ENVIRONMENT_VARIABLE);
            if (string.IsNullOrWhiteSpace(homeDirectory))
            {
                homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }

            string localAppData = Environment.GetEnvironmentVariable(CliConstants.WINDOWS_LOCAL_APPDATA_ENVIRONMENT_VARIABLE);
            return ShouldRemoveInstallDirectoryFromPath(
                installDirectory,
                platform,
                homeDirectory,
                localAppData);
        }

        internal static bool ShouldRemoveInstallDirectoryFromPath(
            string installDirectory,
            RuntimePlatform platform,
            string homeDirectory,
            string localAppData)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(installDirectory), "installDirectory must not be null or empty");

            if (platform != RuntimePlatform.WindowsEditor)
            {
                return false;
            }

            return IsDefaultInstallDirectoryForCurrentUser(
                installDirectory,
                platform,
                homeDirectory,
                localAppData);
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

        private static CliInstallResult BuildUserPathPersistenceFailure(Exception ex)
        {
            UnityEngine.Debug.Assert(ex != null, "ex must not be null");

            string errorOutput =
                "Installed the uLoop CLI binary, but failed to persist the uLoop CLI install directory in the Windows User PATH. "
                + $"Update {CliConstants.WINDOWS_PATH_ENVIRONMENT_VARIABLE} manually or run the CLI-only installer.\n{ex.Message}";
            return new CliInstallResult(false, errorOutput);
        }

        private static CliInstallResult BuildUserPathRemovalFailure(Exception ex)
        {
            UnityEngine.Debug.Assert(ex != null, "ex must not be null");

            string errorOutput =
                "Removed the uLoop CLI binary, but failed to remove the uLoop CLI install directory from the Windows User PATH. "
                + $"Update {CliConstants.WINDOWS_PATH_ENVIRONMENT_VARIABLE} manually.\n{ex.Message}";
            return new CliInstallResult(false, errorOutput);
        }

        private static CliInstallResult BuildCliUninstallFailure(Exception ex)
        {
            UnityEngine.Debug.Assert(ex != null, "ex must not be null");

            string errorOutput = $"Failed to uninstall CLI dispatcher: {ex.Message}";
            return new CliInstallResult(false, errorOutput);
        }

        private static string BuildReleaseCliInstallFailure(string errorOutput, string standardOutput)
        {
            if (!string.IsNullOrWhiteSpace(errorOutput))
            {
                return errorOutput;
            }

            if (!string.IsNullOrWhiteSpace(standardOutput))
            {
                return standardOutput;
            }

            return "Release CLI dispatcher installer failed without output.";
        }

        private static string BuildReleaseCliInstallTimeoutFailure(
            int timeoutMs,
            string errorOutput,
            string standardOutput)
        {
            string capturedOutput = BuildReleaseCliInstallFailure(errorOutput, standardOutput);
            if (string.Equals(capturedOutput, "Release CLI dispatcher installer failed without output.", StringComparison.Ordinal))
            {
                return $"Release CLI dispatcher installer timed out after {timeoutMs} ms.";
            }

            return $"Release CLI dispatcher installer timed out after {timeoutMs} ms.\n{capturedOutput}";
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

        private static void DeleteStagedInstallFiles(string installDirectory, RuntimePlatform platform)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(installDirectory), "installDirectory must not be null or empty");

            if (!Directory.Exists(installDirectory))
            {
                return;
            }

            string fileName = GetGlobalCliInstallFileName(platform);
            string stagedFilePattern = $".{fileName}.install-*";
            string[] stagedInstallFiles = Directory.GetFiles(installDirectory, stagedFilePattern);
            foreach (string stagedInstallFile in stagedInstallFiles)
            {
                File.Delete(stagedInstallFile);
            }
        }

        private static void DeleteNativeInstallTreeIfEmpty(string installDirectory)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(installDirectory), "installDirectory must not be null or empty");

            DirectoryInfo installDirectoryInfo = new(installDirectory);
            DirectoryInfo nativeInstallRoot = installDirectoryInfo.Parent;
            if (nativeInstallRoot == null)
            {
                return;
            }

            if (!string.Equals(installDirectoryInfo.Name, CliConstants.NATIVE_INSTALL_BIN_DIR_NAME, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(nativeInstallRoot.Name, CliConstants.NATIVE_INSTALL_DIR_NAME, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            DeleteDirectoryIfEmpty(installDirectoryInfo.FullName);
            DeleteDirectoryIfEmpty(nativeInstallRoot.FullName);
        }

        private static void DeleteDirectoryIfEmpty(string directoryPath)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(directoryPath), "directoryPath must not be null or empty");

            if (!Directory.Exists(directoryPath) || Directory.GetFileSystemEntries(directoryPath).Length > 0)
            {
                return;
            }

            Directory.Delete(directoryPath);
        }

        private static string BuildPosixInstallScriptCommand(string scriptUrl, string releaseTag)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(scriptUrl), "scriptUrl must not be null or empty");
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(releaseTag), "releaseTag must not be null or empty");

            return "tmp_script=$(mktemp) && "
                + "trap 'rm -f \"$tmp_script\"' EXIT && "
                + $"curl -fsSL '{scriptUrl}' -o \"$tmp_script\" && "
                + $"{CliConstants.INSTALL_VERSION_ENVIRONMENT_VARIABLE}='{releaseTag}' sh \"$tmp_script\"";
        }

        private static string QuoteProcessArgument(string value)
        {
            UnityEngine.Debug.Assert(value != null, "value must not be null");
            return $"\"{value.Replace("\"", "\\\"")}\"";
        }

        private static string BuildReleaseTag(string packageVersion)
        {
            if (packageVersion.StartsWith(CliConstants.RELEASE_TAG_PREFIX, StringComparison.Ordinal))
            {
                return packageVersion;
            }
            return $"{CliConstants.RELEASE_TAG_PREFIX}{packageVersion}";
        }

        internal static string BuildInstallerScriptUrl(string releaseTag, string assetName)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(releaseTag), "releaseTag must not be null or empty");
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(assetName), "assetName must not be null or empty");

            return $"{CliConstants.RAW_CONTENT_BASE_URL}/{SelectInstallerSourceRef(releaseTag)}/{CliConstants.SCRIPTS_DIR_NAME}/{assetName}";
        }

        internal static string SelectInstallerSourceRef(string releaseTag)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(releaseTag), "releaseTag must not be null or empty");

            return releaseTag.IndexOf(CliConstants.BETA_VERSION_MARKER, StringComparison.OrdinalIgnoreCase) >= 0
                ? CliConstants.BETA_INSTALLER_SOURCE_REF
                : CliConstants.STABLE_INSTALLER_SOURCE_REF;
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

        public Task<CliInstallResult> InstallGlobalCliAsync(
            RuntimePlatform platform,
            string dispatcherVersion,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return NativeCliInstaller.InstallAsync(platform, dispatcherVersion, ct);
        }

        public Task<CliInstallResult> UninstallGlobalCliAsync(RuntimePlatform platform, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return NativeCliInstaller.UninstallAsync(platform);
        }

        public NativeCliInstallCommand GetGlobalCliInstallCommand(
            RuntimePlatform platform,
            string packageVersion,
            bool removeLegacyLaunchers)
        {
            return NativeCliInstaller.GetInstallCommand(platform, packageVersion, removeLegacyLaunchers);
        }
    }
}
