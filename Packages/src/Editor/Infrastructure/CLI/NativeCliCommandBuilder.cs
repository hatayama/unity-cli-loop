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
                string windowsScriptUrl = BuildInstallerScriptUrl(releaseTag, CliConstants.WINDOWS_INSTALL_SCRIPT_NAME);
                string command = string.IsNullOrEmpty(localScriptPath)
                    ? BuildWindowsRemoteInstallScriptCommand(
                        windowsScriptUrl,
                        BuildInstallerChecksumUrl(windowsScriptUrl),
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
            string posixScriptUrl = BuildInstallerScriptUrl(releaseTag, CliConstants.POSIX_INSTALL_SCRIPT_NAME);
            string posixCommand = string.IsNullOrEmpty(posixLocalScriptPath)
                ? BuildPosixRemoteInstallScriptCommand(
                    posixScriptUrl,
                    BuildInstallerChecksumUrl(posixScriptUrl),
                    releaseTag)
                : BuildPosixLocalInstallScriptCommand(posixLocalScriptPath, releaseTag);
            string loginShellCommand = BuildLoginShellPosixInstallScriptCommand(posixCommand);
            return new NativeCliInstallCommand(
                posixShellPath,
                $"-l -i -c {QuoteProcessArgument(loginShellCommand)}",
                loginShellCommand);
        }

        internal static NativeCliInstallCommand BuildUninstallCommand(
            string installDirectory,
            RuntimePlatform platform)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(installDirectory), "installDirectory must not be null or empty");

            string installPath = NativeCliInstallPathResolver.GetGlobalCliInstallPath(installDirectory, platform);
            return new NativeCliInstallCommand(
                installPath,
                "uninstall",
                $"{QuoteProcessArgument(installPath)} uninstall");
        }

        private static string BuildPosixRemoteInstallScriptCommand(string scriptUrl, string checksumUrl, string releaseTag)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(scriptUrl), "scriptUrl must not be null or empty");
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(checksumUrl), "checksumUrl must not be null or empty");
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(releaseTag), "releaseTag must not be null or empty");

            // Why: The whole command runs as a single /bin/sh -c string where `set -e` is not applied
            // for us, so every step is && chained to fail fast when curl, checksum verification, or
            // execution fails. `trap ... EXIT` ensures the temp directory is removed even on failure.
            string scriptName = CliConstants.POSIX_INSTALL_SCRIPT_NAME;
            return "tmp_dir=$(mktemp -d) && "
                + "trap 'rm -rf \"$tmp_dir\"' EXIT && "
                + $"curl -fsSL {QuotePosixShellValue(scriptUrl)} -o \"$tmp_dir/{scriptName}\" && "
                + $"curl -fsSL {QuotePosixShellValue(checksumUrl)} -o \"$tmp_dir/{scriptName}.sha256\" && "
                + "( cd \"$tmp_dir\" && "
                + $"if command -v sha256sum >/dev/null 2>&1; then sha256sum -c {scriptName}.sha256; "
                + $"elif command -v shasum >/dev/null 2>&1; then shasum -a 256 -c {scriptName}.sha256; "
                + $"else echo 'sha256sum or shasum is required to verify {scriptName}' >&2; exit 1; fi ) && "
                + $"{CliConstants.INSTALL_VERSION_ENVIRONMENT_VARIABLE}={QuotePosixShellValue(releaseTag)} sh \"$tmp_dir/{scriptName}\"";
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

        private static string BuildWindowsRemoteInstallScriptCommand(string scriptUrl, string checksumUrl, string releaseTag)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(scriptUrl), "scriptUrl must not be null or empty");
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(checksumUrl), "checksumUrl must not be null or empty");
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(releaseTag), "releaseTag must not be null or empty");

            // Why: Downloading with Invoke-WebRequest to a file and re-launching PowerShell with -File
            // replaces the previous `irm | iex` streaming path so the script is verified before it runs
            // and so the child process owns exit-code propagation. `$ErrorActionPreference = 'Stop'`
            // upgrades cmdlet non-terminating errors (e.g. missing checksum file) into throws so the
            // fail-close path does not depend on incidental null dereferences downstream. The .sha256
            // file contains "<hex-hash>  <filename>", so the leading whitespace-delimited token is
            // compared as lower case against Get-FileHash's upper-case output.
            string scriptName = CliConstants.WINDOWS_INSTALL_SCRIPT_NAME;
            return "$tmp_dir = New-Item -ItemType Directory -Path (Join-Path $env:TEMP ([System.Guid]::NewGuid().ToString())) -Force; "
                + $"$script_path = Join-Path $tmp_dir.FullName {QuotePowerShellSingleQuotedValue(scriptName)}; "
                + $"$checksum_path = Join-Path $tmp_dir.FullName {QuotePowerShellSingleQuotedValue(scriptName + ".sha256")}; "
                + "try { "
                + "$ErrorActionPreference = 'Stop'; "
                + "$ProgressPreference = 'SilentlyContinue'; "
                + $"Invoke-WebRequest -UseBasicParsing -Uri {QuotePowerShellSingleQuotedValue(scriptUrl)} -OutFile $script_path; "
                + $"Invoke-WebRequest -UseBasicParsing -Uri {QuotePowerShellSingleQuotedValue(checksumUrl)} -OutFile $checksum_path; "
                + "$expected_hash = ((Get-Content -Raw -Encoding UTF8 $checksum_path) -split '\\s+')[0].ToLowerInvariant(); "
                + "$actual_hash = (Get-FileHash -Algorithm SHA256 -Path $script_path).Hash.ToLowerInvariant(); "
                + $"if ($actual_hash -ne $expected_hash) {{ throw ('Checksum mismatch for {scriptName}: expected=' + $expected_hash + ' actual=' + $actual_hash) }}; "
                + $"$env:{CliConstants.INSTALL_VERSION_ENVIRONMENT_VARIABLE} = {QuotePowerShellSingleQuotedValue(releaseTag)}; "
                + "& powershell -NoProfile -ExecutionPolicy Bypass -File $script_path; "
                + $"if ($LASTEXITCODE -ne 0) {{ throw ('{scriptName} exited with code ' + $LASTEXITCODE) }} "
                + "} finally { "
                + "Remove-Item -Recurse -Force -LiteralPath $tmp_dir.FullName -ErrorAction SilentlyContinue "
                + "}";
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

            // Why: Installer scripts are shipped as dispatcher release assets alongside their .sha256
            // sidecars, so the release download URL is used instead of raw content refs to keep the
            // Unity install path in sync with `uloop update`'s verified installer download.
            return $"{CliConstants.RELEASE_DOWNLOAD_BASE_URL}/{releaseTag}/{assetName}";
        }

        internal static string BuildInstallerChecksumUrl(string scriptUrl)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(scriptUrl), "scriptUrl must not be null or empty");

            return scriptUrl + ".sha256";
        }
    }
}
