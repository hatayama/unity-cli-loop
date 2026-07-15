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
            string dispatcherReleaseTag,
            string dispatcherArchiveManifest,
            bool removeLegacyLaunchers,
            string posixShellPath)
        {
            return BuildInstallCommandWithPackagePath(
                platform,
                dispatcherReleaseTag,
                dispatcherArchiveManifest,
                removeLegacyLaunchers,
                posixShellPath,
                null);
        }

        internal static NativeCliInstallCommand BuildInstallCommandWithPackagePath(
            RuntimePlatform platform,
            string dispatcherReleaseTag,
            string dispatcherArchiveManifest,
            bool removeLegacyLaunchers,
            string posixShellPath,
            string packageResolvedPath)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(dispatcherReleaseTag), "dispatcherReleaseTag must not be null or empty");
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(dispatcherArchiveManifest), "dispatcherArchiveManifest must not be null or empty");
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(posixShellPath), "posixShellPath must not be null or empty");
            _ = removeLegacyLaunchers;

            string releaseTag = dispatcherReleaseTag;
            if (platform == RuntimePlatform.WindowsEditor)
            {
                string localScriptPath = ResolvePackageLocalInstallerScriptPath(
                    packageResolvedPath,
                    CliConstants.WINDOWS_INSTALL_SCRIPT_NAME);
                string windowsScriptUrl = BuildInstallerScriptUrl(releaseTag, CliConstants.WINDOWS_INSTALL_SCRIPT_NAME);
                string command = string.IsNullOrEmpty(localScriptPath)
                    ? BuildWindowsRemoteInstallScriptCommand(
                        windowsScriptUrl,
                        GetManifestDigest(dispatcherArchiveManifest, CliConstants.WINDOWS_INSTALL_SCRIPT_NAME),
                        releaseTag,
                        dispatcherArchiveManifest)
                    : BuildWindowsLocalInstallScriptCommand(localScriptPath, releaseTag, dispatcherArchiveManifest);
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
                    GetManifestDigest(dispatcherArchiveManifest, CliConstants.POSIX_INSTALL_SCRIPT_NAME),
                    releaseTag,
                    dispatcherArchiveManifest)
                : BuildPosixLocalInstallScriptCommand(posixLocalScriptPath, releaseTag, dispatcherArchiveManifest);
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

        private static string BuildPosixRemoteInstallScriptCommand(string scriptUrl, string expectedDigest, string releaseTag, string archiveManifest)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(scriptUrl), "scriptUrl must not be null or empty");
            UnityEngine.Debug.Assert(IsSHA256Digest(expectedDigest), "expectedDigest must be a SHA-256 digest");
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(releaseTag), "releaseTag must not be null or empty");

            // Why: The whole command runs as a single /bin/sh -c string where `set -e` is not applied
            // for us, so every step is && chained to fail fast when curl, checksum verification, or
            // execution fails. `trap ... EXIT` ensures the temp directory is removed even on failure.
            string scriptName = CliConstants.POSIX_INSTALL_SCRIPT_NAME;
            return "tmp_dir=$(mktemp -d) && "
                + "trap 'rm -rf \"$tmp_dir\"' EXIT && "
                + $"curl -fsSL {QuotePosixShellValue(scriptUrl)} -o \"$tmp_dir/{scriptName}\" && "
                + "( if command -v sha256sum >/dev/null 2>&1; then actual_digest=$(sha256sum \"$tmp_dir/" + scriptName + "\" | awk '{print $1}'); "
                + "elif command -v shasum >/dev/null 2>&1; then actual_digest=$(shasum -a 256 \"$tmp_dir/" + scriptName + "\" | awk '{print $1}'); "
                + $"else echo 'sha256sum or shasum is required to verify {scriptName}' >&2; exit 1; fi && "
                + $"[ \"$actual_digest\" = {QuotePosixShellValue(expectedDigest)} ] ) && "
                + $"{CliConstants.INSTALL_VERSION_ENVIRONMENT_VARIABLE}={QuotePosixShellValue(releaseTag)} "
                + $"{CliConstants.ARCHIVE_MANIFEST_ENVIRONMENT_VARIABLE}={QuotePosixShellValue(archiveManifest)} sh \"$tmp_dir/{scriptName}\"";
        }

        private static string BuildPosixLocalInstallScriptCommand(string scriptPath, string releaseTag, string archiveManifest)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(scriptPath), "scriptPath must not be null or empty");
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(releaseTag), "releaseTag must not be null or empty");

            return $"{CliConstants.INSTALL_VERSION_ENVIRONMENT_VARIABLE}={QuotePosixShellValue(releaseTag)} "
                + $"{CliConstants.ARCHIVE_MANIFEST_ENVIRONMENT_VARIABLE}={QuotePosixShellValue(archiveManifest)} sh {QuotePosixShellValue(scriptPath)}";
        }

        internal static string BuildLoginShellPosixInstallScriptCommand(string posixCommand)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(posixCommand), "posixCommand must not be null or empty");
            return $"{CliConstants.POSIX_SHELL_EXECUTABLE_PATH} -c {QuotePosixShellValue(posixCommand)}";
        }

        private static string BuildWindowsRemoteInstallScriptCommand(string scriptUrl, string expectedDigest, string releaseTag, string archiveManifest)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(scriptUrl), "scriptUrl must not be null or empty");
            UnityEngine.Debug.Assert(IsSHA256Digest(expectedDigest), "expectedDigest must be a SHA-256 digest");
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(releaseTag), "releaseTag must not be null or empty");

            // Why: Downloading to a file and running it with -File avoids the previous streaming
            // execution path. The trusted package pin supplies the expected digest, so the downloaded
            // script is never executed until the locally calculated SHA-256 matches that pin.
            string scriptName = CliConstants.WINDOWS_INSTALL_SCRIPT_NAME;
            return "$tmp_dir = New-Item -ItemType Directory -Path (Join-Path $env:TEMP ([System.Guid]::NewGuid().ToString())) -Force; "
                + $"$script_path = Join-Path $tmp_dir.FullName {QuotePowerShellSingleQuotedValue(scriptName)}; "
                + "try { "
                + "$ErrorActionPreference = 'Stop'; "
                + "$ProgressPreference = 'SilentlyContinue'; "
                + $"Invoke-WebRequest -UseBasicParsing -Uri {QuotePowerShellSingleQuotedValue(scriptUrl)} -OutFile $script_path; "
                + "$actual_hash = (Get-FileHash -Algorithm SHA256 -Path $script_path).Hash.ToLowerInvariant(); "
                + $"if ($actual_hash -ne {QuotePowerShellSingleQuotedValue(expectedDigest)}) {{ throw ('Pinned digest mismatch for {scriptName}: actual=' + $actual_hash) }}; "
                + $"$env:{CliConstants.INSTALL_VERSION_ENVIRONMENT_VARIABLE} = {QuotePowerShellSingleQuotedValue(releaseTag)}; "
                + $"$env:{CliConstants.ARCHIVE_MANIFEST_ENVIRONMENT_VARIABLE} = {BuildPowerShellManifestExpression(archiveManifest)}; "
                + "& powershell -NoProfile -ExecutionPolicy Bypass -File $script_path; "
                + $"if ($LASTEXITCODE -ne 0) {{ throw ('{scriptName} exited with code ' + $LASTEXITCODE) }} "
                + "} finally { "
                + "Remove-Item -Recurse -Force -LiteralPath $tmp_dir.FullName -ErrorAction SilentlyContinue "
                + "}";
        }

        private static string BuildWindowsLocalInstallScriptCommand(string scriptPath, string releaseTag, string archiveManifest)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(scriptPath), "scriptPath must not be null or empty");
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(releaseTag), "releaseTag must not be null or empty");

            return $"$env:{CliConstants.INSTALL_VERSION_ENVIRONMENT_VARIABLE}={QuotePowerShellSingleQuotedValue(releaseTag)}; "
                + $"$env:{CliConstants.ARCHIVE_MANIFEST_ENVIRONMENT_VARIABLE}={BuildPowerShellManifestExpression(archiveManifest)}; "
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

        private static string BuildPowerShellManifestExpression(string archiveManifest)
        {
            UnityEngine.Debug.Assert(!archiveManifest.Contains("\r"), "archiveManifest must use LF line endings");
            string[] entries = archiveManifest.Split('\n');
            return string.Join(" + [char]10 + ", System.Array.ConvertAll(entries, QuotePowerShellSingleQuotedValue));
        }

        private static string QuoteProcessArgument(string value)
        {
            UnityEngine.Debug.Assert(value != null, "value must not be null");
            return $"\"{value.Replace("\"", "\\\"")}\"";
        }

        private static string GetManifestDigest(string archiveManifest, string assetName)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(archiveManifest), "archiveManifest must not be null or empty");
            UnityEngine.Debug.Assert(IsSafeAssetName(assetName), "assetName must be safe for command construction");
            string expectedSuffix = "  " + assetName;
            string[] entries = archiveManifest.Split('\n');
            foreach (string entry in entries)
            {
                if (!entry.EndsWith(expectedSuffix, StringComparison.Ordinal))
                {
                    continue;
                }
                string digest = entry.Substring(0, entry.Length - expectedSuffix.Length);
                if (IsSHA256Digest(digest))
                {
                    return digest.ToLowerInvariant();
                }
            }
            throw new ArgumentException($"dispatcher archive manifest is missing a valid digest for {assetName}", nameof(archiveManifest));
        }

        internal static string BuildInstallerScriptUrl(string releaseTag, string assetName)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(releaseTag), "releaseTag must not be null or empty");
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(assetName), "assetName must not be null or empty");

            // Why: The immutable dispatcher Release URL avoids mutable branch content while the package
            // pin authenticates the downloaded asset by its SHA-256 digest.
            return $"{CliConstants.RELEASE_DOWNLOAD_BASE_URL}/{releaseTag}/{assetName}";
        }

        private static bool IsSHA256Digest(string value)
        {
            if (value == null || value.Length != 64)
            {
                return false;
            }
            foreach (char character in value)
            {
                if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f') || (character >= 'A' && character <= 'F')))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsSafeAssetName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }
            foreach (char character in value)
            {
                if (!(char.IsLetterOrDigit(character) || character == '.' || character == '-' || character == '_'))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
