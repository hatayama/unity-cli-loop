using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Places the package-owned native CLI binary into each Unity project so the global command can stay a dispatcher.
    /// </summary>
    public static class ProjectLocalCliInstaller
    {
        private const int CHMOD_TIMEOUT_MS = 5000;

        public static CliInstallResult InstallProjectLocalCli(string projectRoot)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            string sourceBinaryPath = GetProjectCliBundlePath();
            return InstallProjectLocalCliFromBundle(sourceBinaryPath, projectRoot);
        }

        public static bool IsProjectLocalCliVersionCurrent(string projectRoot, string expectedVersion)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(expectedVersion), "expectedVersion must not be null or empty");

            string version = DetectProjectLocalCliVersion(projectRoot);
            return string.Equals(version, expectedVersion, System.StringComparison.Ordinal);
        }

        internal static bool IsProjectLocalCliCurrentForBundle(
            string sourceBinaryPath,
            string projectRoot,
            string expectedVersion)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(sourceBinaryPath), "sourceBinaryPath must not be null or empty");
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(expectedVersion), "expectedVersion must not be null or empty");

            if (!File.Exists(sourceBinaryPath))
            {
                return false;
            }

            string projectLocalCliPath = GetProjectLocalCliPath(projectRoot);
            if (!File.Exists(projectLocalCliPath))
            {
                return false;
            }

            if (!IsProjectLocalCliVersionCurrent(projectRoot, expectedVersion))
            {
                return false;
            }

            return FilesHaveSameSha256(sourceBinaryPath, projectLocalCliPath);
        }

        internal static CliInstallResult InstallProjectLocalCliFromBundle(
            string sourceBinaryPath,
            string projectRoot)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(sourceBinaryPath), "sourceBinaryPath must not be null or empty");
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            if (!File.Exists(sourceBinaryPath))
            {
                return new CliInstallResult(
                    false,
                    $"Project-local CLI binary was not found for {Application.platform}/{RuntimeInformation.ProcessArchitecture}: {sourceBinaryPath}");
            }

            string projectLocalBinDir = GetProjectLocalBinDir(projectRoot);
            Directory.CreateDirectory(projectLocalBinDir);

            string projectLocalCliPath = GetProjectLocalCliPath(projectRoot);
            File.Copy(sourceBinaryPath, projectLocalCliPath, overwrite: true);

            return MakeProjectLocalCliExecutable(projectLocalCliPath);
        }

        internal static string GetProjectCliBundlePath()
        {
            return Path.Combine(
                McpConstants.PackageResolvedPath,
                CliConstants.GO_CLI_PACKAGE_DIR_NAME,
                CliConstants.DIST_DIR_NAME,
                GetNativeCliPlatformDir(),
                GetNativeCliFileName());
        }

        internal static string GetProjectLocalCliPath(string projectRoot)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            return Path.Combine(
                GetProjectLocalBinDir(projectRoot),
                GetProjectLocalCliFileName());
        }

        internal static string GetProjectLocalUnixCommandPath(string projectRoot)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            return Path.Combine(
                GetProjectLocalBinDir(projectRoot),
                CliConstants.PROJECT_LOCAL_UNIX_COMMAND_NAME);
        }

        internal static string GetProjectLocalWindowsCommandPath(string projectRoot)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            return Path.Combine(
                GetProjectLocalBinDir(projectRoot),
                CliConstants.PROJECT_LOCAL_WINDOWS_COMMAND_NAME);
        }

        internal static string DetectProjectLocalCliVersion(string projectRoot)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            string projectLocalCliPath = GetProjectLocalCliPath(projectRoot);
            if (!File.Exists(projectLocalCliPath))
            {
                return null;
            }

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = projectLocalCliPath,
                Arguments = CliConstants.VERSION_FLAG,
                WorkingDirectory = projectRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            Process process = ProcessStartHelper.TryStart(startInfo);
            if (process == null)
            {
                return null;
            }

            bool exited = process.WaitForExit(CHMOD_TIMEOUT_MS);
            if (!exited)
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }

                process.Dispose();
                return null;
            }

            process.WaitForExit();
            string output = process.StandardOutput.ReadToEnd().Trim().TrimStart('v', 'V');
            bool failed = process.ExitCode != 0 || string.IsNullOrEmpty(output);
            process.Dispose();

            return failed ? null : output;
        }

        private static string GetProjectLocalBinDir(string projectRoot)
        {
            return Path.Combine(
                projectRoot,
                McpConstants.ULOOP_DIR,
                CliConstants.PROJECT_LOCAL_BIN_DIR_NAME);
        }

        private static string GetProjectLocalCliFileName()
        {
            return Application.platform == RuntimePlatform.WindowsEditor
                ? CliConstants.PROJECT_LOCAL_WINDOWS_COMMAND_NAME
                : CliConstants.PROJECT_LOCAL_UNIX_COMMAND_NAME;
        }

        private static string GetNativeCliFileName()
        {
            return Application.platform == RuntimePlatform.WindowsEditor
                ? CliConstants.PROJECT_LOCAL_WINDOWS_COMMAND_NAME
                : CliConstants.PROJECT_LOCAL_UNIX_COMMAND_NAME;
        }

        private static string GetNativeCliPlatformDir()
        {
            RuntimePlatform platform = Application.platform;
            Architecture architecture = RuntimeInformation.ProcessArchitecture;

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

        private static CliInstallResult MakeProjectLocalCliExecutable(string projectLocalCliPath)
        {
            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                return new CliInstallResult(true, "");
            }

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "/bin/chmod",
                Arguments = $"+x {QuoteProcessArgument(projectLocalCliPath)}",
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
                return new CliInstallResult(false, "Making project-local CLI executable timed out");
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

        private static bool FilesHaveSameSha256(string leftPath, string rightPath)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(leftPath), "leftPath must not be null or empty");
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(rightPath), "rightPath must not be null or empty");

            FileInfo leftInfo = new FileInfo(leftPath);
            FileInfo rightInfo = new FileInfo(rightPath);
            if (leftInfo.Length != rightInfo.Length)
            {
                return false;
            }

            byte[] leftHash = ComputeSha256(leftPath);
            byte[] rightHash = ComputeSha256(rightPath);
            return System.Linq.Enumerable.SequenceEqual(leftHash, rightHash);
        }

        private static byte[] ComputeSha256(string path)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(path), "path must not be null or empty");

            using SHA256 sha256 = SHA256.Create();
            using FileStream stream = File.OpenRead(path);
            return sha256.ComputeHash(stream);
        }
    }
}
