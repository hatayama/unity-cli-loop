using System;
using System.Diagnostics;
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
    /// Centralizes native installer invocation so editor setup UI and Go CLI update use the same direct-distribution channel.
    /// </summary>
    public static class NativeCliInstaller
    {
        public static NativeCliInstallCommand GetInstallCommand(RuntimePlatform platform, string packageVersion)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(packageVersion), "packageVersion must not be null or empty");

            string releaseTag = BuildReleaseTag(packageVersion);
            if (platform == RuntimePlatform.WindowsEditor)
            {
                string scriptUrl = BuildReleaseAssetUrl(releaseTag, CliConstants.WINDOWS_INSTALL_SCRIPT_NAME);
                string command =
                    $"$env:{CliConstants.INSTALL_VERSION_ENVIRONMENT_VARIABLE}='{releaseTag}'; " +
                    $"irm '{scriptUrl}' | iex";
                return new NativeCliInstallCommand(
                    "powershell",
                    $"-NoProfile -ExecutionPolicy Bypass -Command \"{command}\"",
                    command);
            }

            string posixScriptUrl = BuildReleaseAssetUrl(releaseTag, CliConstants.POSIX_INSTALL_SCRIPT_NAME);
            string posixCommand =
                $"curl -fsSL '{posixScriptUrl}' | " +
                $"{CliConstants.INSTALL_VERSION_ENVIRONMENT_VARIABLE}='{releaseTag}' sh";
            return new NativeCliInstallCommand(
                "/bin/sh",
                $"-c \"{posixCommand}\"",
                posixCommand);
        }

        public static async Task<CliInstallResult> InstallAsync(RuntimePlatform platform, string packageVersion)
        {
            NativeCliInstallCommand command = GetInstallCommand(platform, packageVersion);
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = command.FileName,
                Arguments = command.Arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            bool success = false;
            string errorOutput = "";

            await Task.Run(() =>
            {
                Process process = ProcessStartHelper.TryStart(startInfo);
                if (process == null)
                {
                    errorOutput = $"Failed to start installer process. Run manually:\n{command.ManualCommand}";
                    return;
                }

                using (process)
                {
                    StringBuilder errorBuilder = new StringBuilder();
                    process.OutputDataReceived += (s, e) => { };
                    process.ErrorDataReceived += (s, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    if (!process.WaitForExit(CliConstants.GLOBAL_INSTALL_TIMEOUT_MS))
                    {
                        if (!process.HasExited) process.Kill();
                        errorOutput = $"Installation timed out after {CliConstants.GLOBAL_INSTALL_TIMEOUT_MS / 1000} seconds.\nRun manually:\n{command.ManualCommand}";
                        return;
                    }

                    process.WaitForExit();
                    errorOutput = errorBuilder.ToString();
                    success = process.ExitCode == 0;
                }
            });

            CliInstallationDetector.InvalidateCache();
            return new CliInstallResult(success, errorOutput);
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
