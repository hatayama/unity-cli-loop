using System;
using System.IO;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Builds native CLI installer commands from platform and package inputs.
    /// </summary>
    internal static class NativeCliCommandBuilder
    {
        internal static NativeCliInstallCommand BuildRemoteInstallCommand(
            RuntimePlatform platform,
            string cliReleaseTag,
            bool removeLegacyLaunchers,
            string posixShellPath)
        {
            return BuildInstallCommandWithPackagePath(
                platform,
                cliReleaseTag,
                removeLegacyLaunchers,
                posixShellPath,
                null);
        }

        internal static NativeCliInstallCommand BuildInstallCommandWithPackagePath(
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

        internal static string QuoteProcessArgument(string value)
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
            if (cliReleaseTag.StartsWith(CliConstants.PROJECT_RUNNER_RELEASE_TAG_PREFIX, StringComparison.Ordinal))
            {
                return $"{CliConstants.DISPATCHER_RELEASE_TAG_PREFIX}{cliReleaseTag.Substring(CliConstants.PROJECT_RUNNER_RELEASE_TAG_PREFIX.Length)}";
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
}
