using System;
using System.IO;
using System.Text;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Resolves and compares package-owned native CLI install paths.
    /// </summary>
    internal static class NativeCliInstallPathResolver
    {
        private const string WINDOWS_FILE_PATH_SEPARATOR = "\\";
        private const string POSIX_FILE_PATH_SEPARATOR = "/";

        internal static string GetGlobalCliInstallPath(string installDirectory, RuntimePlatform platform)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(installDirectory), "installDirectory must not be null or empty");

            string separator = GetFilePathSeparator(platform);
            string normalizedInstallDirectory = installDirectory.TrimEnd('\\', '/');
            return normalizedInstallDirectory + separator + GetGlobalCliInstallFileName(platform);
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

        internal static string GetInstallDirectoryForCurrentUser(RuntimePlatform platform)
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

        internal static string GetPathEnvironmentVariableName(RuntimePlatform platform)
        {
            return platform == RuntimePlatform.WindowsEditor
                ? CliConstants.WINDOWS_PATH_ENVIRONMENT_VARIABLE
                : CliConstants.POSIX_PATH_ENVIRONMENT_VARIABLE;
        }

        internal static bool DoesUserPathContainInstallDirectory(
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

        private static string GetGlobalCliInstallFileName(RuntimePlatform platform)
        {
            return platform == RuntimePlatform.WindowsEditor
                ? CliConstants.GLOBAL_WINDOWS_COMMAND_NAME
                : CliConstants.GLOBAL_UNIX_COMMAND_NAME;
        }
    }
}
