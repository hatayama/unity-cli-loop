using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop
{
    public readonly struct NativeCliInstallCommand
    {
        public NativeCliInstallCommand(string fileName, string arguments, string manualCommand)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(fileName), "fileName must not be null or empty");
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(arguments), "arguments must not be null or empty");
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(manualCommand), "manualCommand must not be null or empty");

            FileName = fileName;
            Arguments = arguments;
            ManualCommand = manualCommand;
        }

        public string FileName { get; }
        public string Arguments { get; }
        public string ManualCommand { get; }
    }

    /// <summary>
    /// Installs the package-owned global dispatcher while keeping release-script commands available for CLI-only users.
    /// </summary>
    public static class NativeCliInstaller
    {
        private const int CHMOD_TIMEOUT_MS = 5000;

        public static NativeCliInstallCommand GetInstallCommand(
            RuntimePlatform platform,
            string packageVersion,
            bool removeLegacyNpm)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(packageVersion), "packageVersion must not be null or empty");

            string releaseTag = BuildReleaseTag(packageVersion);
            if (platform == RuntimePlatform.WindowsEditor)
            {
                string scriptUrl = BuildReleaseAssetUrl(releaseTag, CliConstants.WINDOWS_INSTALL_SCRIPT_NAME);
                string command =
                    $"$env:{CliConstants.INSTALL_VERSION_ENVIRONMENT_VARIABLE}='{releaseTag}'; " +
                    BuildWindowsRemoveLegacyAssignment(removeLegacyNpm) +
                    $"irm '{scriptUrl}' | iex";
                return new NativeCliInstallCommand(
                    "powershell",
                    $"-NoProfile -ExecutionPolicy Bypass -Command \"{command}\"",
                    command);
            }

            string posixScriptUrl = BuildReleaseAssetUrl(releaseTag, CliConstants.POSIX_INSTALL_SCRIPT_NAME);
            string posixCommand =
                $"curl -fsSL '{posixScriptUrl}' | " +
                BuildPosixRemoveLegacyAssignment(removeLegacyNpm) +
                $"{CliConstants.INSTALL_VERSION_ENVIRONMENT_VARIABLE}='{releaseTag}' sh";
            return new NativeCliInstallCommand(
                "/bin/sh",
                $"-c \"{posixCommand}\"",
                posixCommand);
        }

        public static async Task<CliInstallResult> InstallAsync(
            RuntimePlatform platform,
            string packageVersion)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(packageVersion), "packageVersion must not be null or empty");

            string installDirectory = GetInstallDirectoryForCurrentUser(platform);
            if (string.IsNullOrWhiteSpace(installDirectory))
            {
                return new CliInstallResult(
                    false,
                    $"Could not resolve the global CLI install directory. Set {CliConstants.INSTALL_DIR_ENVIRONMENT_VARIABLE} and try again.");
            }

            string sourceBinaryPath = GetGlobalCliBundlePath(
                McpConstants.PackageResolvedPath,
                platform,
                RuntimeInformation.ProcessArchitecture);

            CliInstallResult result = await Task.Run(() => InstallGlobalCliFromBundle(
                sourceBinaryPath,
                installDirectory,
                platform));
            CliInstallationDetector.InvalidateCache();

            if (result.Success)
            {
                result = FinishSuccessfulBundleInstall(
                    result,
                    installDirectory,
                    platform,
                    currentPlatform => RemoveLegacyNpmPackageIfPresent(currentPlatform, RunInstallCommand),
                    ApplyInstallDirectoryToCurrentProcessPath,
                    (currentInstallDirectory, currentPlatform) => PersistInstallDirectoryToUserPath(
                        currentInstallDirectory,
                        currentPlatform,
                        Environment.GetEnvironmentVariable,
                        Environment.SetEnvironmentVariable));
            }

            return result;
        }

        internal static CliInstallResult InstallGlobalCliFromBundle(
            string sourceBinaryPath,
            string installDirectory,
            RuntimePlatform platform)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(sourceBinaryPath), "sourceBinaryPath must not be null or empty");
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(installDirectory), "installDirectory must not be null or empty");

            if (!File.Exists(sourceBinaryPath))
            {
                return new CliInstallResult(
                    false,
                    $"Global CLI dispatcher binary was not found for {platform}/{RuntimeInformation.ProcessArchitecture}: {sourceBinaryPath}");
            }

            try
            {
                string installPath = GetGlobalCliInstallPath(installDirectory, platform);
                string stagedInstallPath = GetStagedGlobalCliInstallPath(installDirectory, platform);
                Directory.CreateDirectory(installDirectory);
                File.Copy(sourceBinaryPath, stagedInstallPath, overwrite: true);

                CliInstallResult executableResult = MakeGlobalCliExecutable(stagedInstallPath, platform);
                if (!executableResult.Success)
                {
                    File.Delete(stagedInstallPath);
                    return executableResult;
                }

                ReplaceInstalledCliFromStaged(stagedInstallPath, installPath);
                return executableResult;
            }
            catch (IOException ex)
            {
                return BuildBundledCliInstallFailure(ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                return BuildBundledCliInstallFailure(ex);
            }
            catch (ArgumentException ex)
            {
                return BuildBundledCliInstallFailure(ex);
            }
            catch (NotSupportedException ex)
            {
                return BuildBundledCliInstallFailure(ex);
            }
        }

        internal static string GetGlobalCliBundlePath(
            string packageResolvedPath,
            RuntimePlatform platform,
            Architecture architecture)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(packageResolvedPath), "packageResolvedPath must not be null or empty");

            return Path.Combine(
                packageResolvedPath,
                CliConstants.GO_CLI_PACKAGE_DIR_NAME,
                CliConstants.DIST_DIR_NAME,
                GetNativeCliPlatformDir(platform, architecture),
                GetGlobalCliBundleFileName(platform));
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

        internal static CliInstallResult RemoveLegacyNpmPackageIfPresent(
            RuntimePlatform platform,
            Func<NativeCliInstallCommand, RuntimePlatform, CliInstallResult> runCommand)
        {
            UnityEngine.Debug.Assert(runCommand != null, "runCommand must not be null");

            NativeCliInstallCommand detectCommand = GetLegacyNpmListCommand(platform);
            CliInstallResult detectResult = runCommand(detectCommand, platform);
            if (!detectResult.Success)
            {
                return new CliInstallResult(true, "");
            }

            NativeCliInstallCommand uninstallCommand = GetLegacyNpmUninstallCommand(platform);
            CliInstallResult uninstallResult = runCommand(uninstallCommand, platform);
            if (uninstallResult.Success)
            {
                return new CliInstallResult(true, "");
            }

            string errorOutput =
                $"Failed to remove legacy npm installation: {CliConstants.LEGACY_NPM_PACKAGE_NAME}\n"
                + "The bundled dispatcher was installed, but an older npm launcher may still shadow it.\n"
                + "Run manually:\n"
                + $"  {uninstallCommand.ManualCommand}\n"
                + uninstallResult.ErrorOutput;
            return new CliInstallResult(false, errorOutput);
        }

        internal static CliInstallResult FinishSuccessfulBundleInstall(
            CliInstallResult installResult,
            string installDirectory,
            RuntimePlatform platform,
            Func<RuntimePlatform, CliInstallResult> removeLegacyNpmPackage,
            Action<RuntimePlatform> applyInstallDirectoryToCurrentProcessPath,
            Func<string, RuntimePlatform, CliInstallResult> persistInstallDirectoryToUserPath)
        {
            UnityEngine.Debug.Assert(installResult.Success, "installResult must be successful");
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(installDirectory), "installDirectory must not be null or empty");
            UnityEngine.Debug.Assert(removeLegacyNpmPackage != null, "removeLegacyNpmPackage must not be null");
            UnityEngine.Debug.Assert(applyInstallDirectoryToCurrentProcessPath != null, "applyInstallDirectoryToCurrentProcessPath must not be null");
            UnityEngine.Debug.Assert(persistInstallDirectoryToUserPath != null, "persistInstallDirectoryToUserPath must not be null");

            CliInstallResult legacyResult = removeLegacyNpmPackage(platform);
            CliInstallResult result = legacyResult.Success ? installResult : legacyResult;

            applyInstallDirectoryToCurrentProcessPath(platform);
            CliInstallResult persistResult = persistInstallDirectoryToUserPath(installDirectory, platform);
            return persistResult.Success ? result : persistResult;
        }

        private static string GetStagedGlobalCliInstallPath(string installDirectory, RuntimePlatform platform)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(installDirectory), "installDirectory must not be null or empty");

            string fileName = GetGlobalCliInstallFileName(platform);
            return Path.Combine(
                installDirectory,
                $".{fileName}.install-{Guid.NewGuid():N}");
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
            StringBuilder builder = new StringBuilder(installDirectory);
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

        private static void ReplaceInstalledCliFromStaged(string stagedInstallPath, string installPath)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(stagedInstallPath), "stagedInstallPath must not be null or empty");
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(installPath), "installPath must not be null or empty");

            if (!File.Exists(installPath))
            {
                File.Move(stagedInstallPath, installPath);
                return;
            }

            File.Replace(stagedInstallPath, installPath, null, true);
        }

        private static CliInstallResult BuildUserPathPersistenceFailure(Exception ex)
        {
            UnityEngine.Debug.Assert(ex != null, "ex must not be null");

            string errorOutput =
                "Installed the uLoop CLI binary, but failed to persist the uLoop CLI install directory in the Windows User PATH. "
                + $"Update {CliConstants.WINDOWS_PATH_ENVIRONMENT_VARIABLE} manually or run the CLI-only installer.\n{ex.Message}";
            return new CliInstallResult(false, errorOutput);
        }

        private static CliInstallResult BuildBundledCliInstallFailure(Exception ex)
        {
            UnityEngine.Debug.Assert(ex != null, "ex must not be null");

            string errorOutput = $"Failed to install bundled CLI dispatcher: {ex.Message}";
            return new CliInstallResult(false, errorOutput);
        }

        private static string GetGlobalCliBundleFileName(RuntimePlatform platform)
        {
            return platform == RuntimePlatform.WindowsEditor
                ? CliConstants.GLOBAL_DISPATCHER_WINDOWS_BUNDLE_NAME
                : CliConstants.GLOBAL_DISPATCHER_UNIX_BUNDLE_NAME;
        }

        private static string GetGlobalCliInstallFileName(RuntimePlatform platform)
        {
            return platform == RuntimePlatform.WindowsEditor
                ? CliConstants.GLOBAL_WINDOWS_COMMAND_NAME
                : CliConstants.GLOBAL_UNIX_COMMAND_NAME;
        }

        private static string GetNativeCliPlatformDir(RuntimePlatform platform, Architecture architecture)
        {
            if (platform == RuntimePlatform.OSXEditor)
            {
                return architecture == Architecture.Arm64 ? "darwin-arm64" : "darwin-amd64";
            }

            if (platform == RuntimePlatform.WindowsEditor)
            {
                return "windows-amd64";
            }

            return "unsupported";
        }

        private static CliInstallResult MakeGlobalCliExecutable(string installPath, RuntimePlatform platform)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(installPath), "installPath must not be null or empty");

            if (platform == RuntimePlatform.WindowsEditor)
            {
                return new CliInstallResult(true, "");
            }

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "/bin/chmod",
                Arguments = $"+x {QuoteProcessArgument(installPath)}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            Process process = ProcessStartHelper.TryStart(startInfo);
            if (process == null)
            {
                return new CliInstallResult(false, "Failed to start chmod process");
            }

            bool exited = process.WaitForExit(CHMOD_TIMEOUT_MS);
            if (!exited)
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }

                process.Dispose();
                return new CliInstallResult(false, "Making global CLI executable timed out");
            }

            process.WaitForExit();
            string errorOutput = process.StandardError.ReadToEnd();
            bool success = process.ExitCode == 0;
            process.Dispose();

            return new CliInstallResult(success, errorOutput);
        }

        private static string QuoteProcessArgument(string value)
        {
            UnityEngine.Debug.Assert(value != null, "value must not be null");
            return $"\"{value.Replace("\"", "\\\"")}\"";
        }

        private static NativeCliInstallCommand GetLegacyNpmListCommand(RuntimePlatform platform)
        {
            string command = $"npm list -g {CliConstants.LEGACY_NPM_PACKAGE_NAME} --depth=0";
            return BuildShellCommand(platform, command);
        }

        private static NativeCliInstallCommand GetLegacyNpmUninstallCommand(RuntimePlatform platform)
        {
            string command = $"npm uninstall -g {CliConstants.LEGACY_NPM_PACKAGE_NAME}";
            return BuildShellCommand(platform, command);
        }

        private static NativeCliInstallCommand BuildShellCommand(RuntimePlatform platform, string command)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(command), "command must not be null or empty");

            if (platform == RuntimePlatform.WindowsEditor)
            {
                return new NativeCliInstallCommand(
                    "cmd.exe",
                    $"/c {command}",
                    command);
            }

            return new NativeCliInstallCommand(
                "/bin/sh",
                $"-c \"{command}\"",
                command);
        }

        private static CliInstallResult RunInstallCommand(NativeCliInstallCommand command, RuntimePlatform platform)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = command.FileName,
                Arguments = command.Arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            ApplyInstallerSearchPath(startInfo, platform);

            Process process = ProcessStartHelper.TryStart(startInfo);
            if (process == null)
            {
                return new CliInstallResult(false, $"Failed to start command: {command.ManualCommand}");
            }

            StringBuilder errorBuilder = new StringBuilder();
            process.OutputDataReceived += (sender, e) => { };
            process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    errorBuilder.AppendLine(e.Data);
                }
            };

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            bool exited = process.WaitForExit(CliConstants.GLOBAL_INSTALL_TIMEOUT_MS);
            if (!exited)
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }

                process.Dispose();
                return new CliInstallResult(false, $"Command timed out: {command.ManualCommand}");
            }

            process.WaitForExit();
            string errorOutput = errorBuilder.ToString();
            bool success = process.ExitCode == 0;
            process.Dispose();
            return new CliInstallResult(success, errorOutput);
        }

        private static void ApplyInstallerSearchPath(ProcessStartInfo startInfo, RuntimePlatform platform)
        {
            UnityEngine.Debug.Assert(startInfo != null, "startInfo must not be null");

            if (platform == RuntimePlatform.WindowsEditor)
            {
                return;
            }

            string loginShellPath = NodeEnvironmentResolver.GetLoginShellPathAtPlatform(platform);
            if (string.IsNullOrEmpty(loginShellPath))
            {
                return;
            }

            startInfo.Environment[CliConstants.POSIX_PATH_ENVIRONMENT_VARIABLE] = loginShellPath;
        }

        private static string BuildWindowsRemoveLegacyAssignment(bool removeLegacyNpm)
        {
            if (!removeLegacyNpm)
            {
                return "";
            }

            return $"$env:{CliConstants.REMOVE_LEGACY_ENVIRONMENT_VARIABLE}='{CliConstants.REMOVE_LEGACY_ENABLED_VALUE}'; ";
        }

        private static string BuildPosixRemoveLegacyAssignment(bool removeLegacyNpm)
        {
            if (!removeLegacyNpm)
            {
                return "";
            }

            return $"{CliConstants.REMOVE_LEGACY_ENVIRONMENT_VARIABLE}='{CliConstants.REMOVE_LEGACY_ENABLED_VALUE}' ";
        }

        private static string BuildReleaseTag(string packageVersion)
        {
            if (packageVersion.StartsWith(CliConstants.RELEASE_TAG_PREFIX, StringComparison.Ordinal))
            {
                return packageVersion;
            }
            return $"{CliConstants.RELEASE_TAG_PREFIX}{packageVersion}";
        }

        private static string BuildReleaseAssetUrl(string releaseTag, string assetName)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(releaseTag), "releaseTag must not be null or empty");
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(assetName), "assetName must not be null or empty");

            return $"{CliConstants.RELEASE_DOWNLOAD_BASE_URL}/{releaseTag}/{assetName}";
        }
    }
}
