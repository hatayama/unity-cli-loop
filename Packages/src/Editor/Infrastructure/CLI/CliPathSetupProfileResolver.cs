using System;
using System.IO;
using System.Text;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Selects the shell profile target for the single PATH line owned by Unity CLI Loop.
    /// </summary>
    internal static class CliPathSetupProfileResolver
    {
        private const string HomeReference = "$HOME";
        private const string ZshProfileFileName = ".zshrc";
        private const string BashProfileFileName = ".bash_profile";
        private const string BashLoginFileName = ".bash_login";
        private const string PosixProfileFileName = ".profile";
        private const string FishConfigDirectoryName = "fish";
        private const string FishConfigFileName = "config.fish";
        private const string DefaultXdgConfigDirectoryName = ".config";

        public static CliPathSetupPlan ResolveCurrentUserPlan(RuntimePlatform platform)
        {
            string installDirectory = NativeCliInstaller.GetCurrentUserGlobalCliInstallDirectory(platform);
            if (string.IsNullOrWhiteSpace(installDirectory))
            {
                return CreateUnsupportedPlan("unknown", CliConstants.EXECUTABLE_NAME);
            }

            return ResolvePlan(
                platform,
                NodeEnvironmentResolver.GetUserShell(),
                Environment.GetEnvironmentVariable(CliConstants.POSIX_HOME_ENVIRONMENT_VARIABLE),
                Environment.GetEnvironmentVariable("ZDOTDIR"),
                Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"),
                installDirectory,
                File.Exists);
        }

        internal static CliPathSetupPlan ResolvePlan(
            RuntimePlatform platform,
            string shellPath,
            string homeDirectory,
            string zDotDirectory,
            string xdgConfigHome,
            string installDirectory,
            Func<string, bool> fileExists)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(installDirectory), "installDirectory must not be null or empty");
            Debug.Assert(fileExists != null, "fileExists must not be null");

            if (platform == RuntimePlatform.WindowsEditor)
            {
                return CreateUnsupportedPlan("windows", installDirectory);
            }

            string resolvedHomeDirectory = string.IsNullOrWhiteSpace(homeDirectory)
                ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                : homeDirectory;
            string shellName = GetShellName(shellPath);
            string profileInstallDirectory = FormatInstallDirectoryForProfile(
                installDirectory,
                resolvedHomeDirectory);

            if (string.Equals(shellName, "zsh", StringComparison.Ordinal))
            {
                string configurationRoot = string.IsNullOrWhiteSpace(zDotDirectory)
                    ? resolvedHomeDirectory
                    : zDotDirectory;
                string configurationPath = Path.Combine(configurationRoot, ZshProfileFileName);
                string configurationLine = BuildPosixExportLine(profileInstallDirectory);
                return CreateSupportedPlan(
                    CliPathSetupShellKind.Zsh,
                    shellName,
                    installDirectory,
                    profileInstallDirectory,
                    configurationPath,
                    configurationLine);
            }

            if (string.Equals(shellName, "bash", StringComparison.Ordinal))
            {
                string configurationPath = SelectBashProfilePath(resolvedHomeDirectory, fileExists);
                string configurationLine = BuildPosixExportLine(profileInstallDirectory);
                return CreateSupportedPlan(
                    CliPathSetupShellKind.Bash,
                    shellName,
                    installDirectory,
                    profileInstallDirectory,
                    configurationPath,
                    configurationLine);
            }

            if (string.Equals(shellName, "fish", StringComparison.Ordinal))
            {
                string configRoot = string.IsNullOrWhiteSpace(xdgConfigHome)
                    ? Path.Combine(resolvedHomeDirectory, DefaultXdgConfigDirectoryName)
                    : xdgConfigHome;
                string configurationPath = Path.Combine(
                    configRoot,
                    FishConfigDirectoryName,
                    FishConfigFileName);
                string configurationLine = $"fish_add_path --move \"{EscapeDoubleQuotedPathValue(profileInstallDirectory, false)}\"";
                return CreateSupportedPlan(
                    CliPathSetupShellKind.Fish,
                    shellName,
                    installDirectory,
                    profileInstallDirectory,
                    configurationPath,
                    configurationLine);
            }

            return CreateUnsupportedPlan(
                string.IsNullOrWhiteSpace(shellName) ? "unknown" : shellName,
                installDirectory);
        }

        private static CliPathSetupPlan CreateSupportedPlan(
            CliPathSetupShellKind shellKind,
            string shellName,
            string installDirectory,
            string profileInstallDirectory,
            string configurationPath,
            string configurationLine)
        {
            return new CliPathSetupPlan(
                shellKind,
                shellName,
                true,
                installDirectory,
                profileInstallDirectory,
                configurationPath,
                configurationLine,
                BuildManualCommand(configurationPath, configurationLine));
        }

        private static CliPathSetupPlan CreateUnsupportedPlan(string shellName, string installDirectory)
        {
            return new CliPathSetupPlan(
                CliPathSetupShellKind.Unsupported,
                shellName,
                false,
                installDirectory,
                installDirectory,
                "",
                "",
                "");
        }

        private static string SelectBashProfilePath(string homeDirectory, Func<string, bool> fileExists)
        {
            string bashProfilePath = Path.Combine(homeDirectory, BashProfileFileName);
            if (fileExists(bashProfilePath))
            {
                return bashProfilePath;
            }

            string bashLoginPath = Path.Combine(homeDirectory, BashLoginFileName);
            if (fileExists(bashLoginPath))
            {
                return bashLoginPath;
            }

            string profilePath = Path.Combine(homeDirectory, PosixProfileFileName);
            return fileExists(profilePath) ? profilePath : bashProfilePath;
        }

        private static string GetShellName(string shellPath)
        {
            return string.IsNullOrWhiteSpace(shellPath)
                ? string.Empty
                : Path.GetFileName(shellPath.Trim()).ToLowerInvariant();
        }

        private static string FormatInstallDirectoryForProfile(string installDirectory, string homeDirectory)
        {
            if (string.IsNullOrWhiteSpace(homeDirectory))
            {
                return installDirectory;
            }

            string normalizedHome = homeDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(installDirectory, normalizedHome, StringComparison.Ordinal))
            {
                return HomeReference;
            }

            string homePrefix = normalizedHome + Path.DirectorySeparatorChar;
            return installDirectory.StartsWith(homePrefix, StringComparison.Ordinal)
                ? HomeReference + "/" + installDirectory.Substring(homePrefix.Length)
                : installDirectory;
        }

        private static string BuildPosixExportLine(string installDirectory)
        {
            return $"export PATH=\"{EscapeDoubleQuotedPathValue(installDirectory, true)}:$PATH\"";
        }

        private static string EscapeDoubleQuotedPathValue(string value, bool escapeBacktick)
        {
            StringBuilder builder = new StringBuilder();
            int cursor = 0;
            if (string.Equals(value, HomeReference, StringComparison.Ordinal)
                || value.StartsWith(HomeReference + "/", StringComparison.Ordinal))
            {
                builder.Append(HomeReference);
                cursor = HomeReference.Length;
            }

            while (cursor < value.Length)
            {
                char character = value[cursor];
                if (character == '\\'
                    || character == '"'
                    || character == '$'
                    || (escapeBacktick && character == '`'))
                {
                    builder.Append('\\');
                }

                builder.Append(character);
                cursor++;
            }

            return builder.ToString();
        }

        private static string BuildManualCommand(string configurationPath, string configurationLine)
        {
            string configurationDirectory = Path.GetDirectoryName(configurationPath);
            string appendCommand = "printf '\\n%s\\n' "
                + $"{QuotePosixShellValue(configurationLine)} >> {QuotePosixShellValue(configurationPath)}";
            if (string.IsNullOrEmpty(configurationDirectory))
            {
                return appendCommand;
            }

            return $"mkdir -p {QuotePosixShellValue(configurationDirectory)} && " + appendCommand;
        }

        private static string QuotePosixShellValue(string value)
        {
            return $"'{value.Replace("'", "'\"'\"'")}'";
        }
    }
}
