using System.Diagnostics;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop
{
    internal readonly struct CliInstallationDetection
    {
        public CliInstallationDetection(string version, string executablePath)
        {
            Version = version;
            ExecutablePath = executablePath;
        }

        public string Version { get; }
        public string ExecutablePath { get; }
    }

    public static class CliInstallationDetector
    {
        private const int PROCESS_TIMEOUT_MS = 5000;

        private static string _cachedCliVersion;
        private static string _cachedCliExecutablePath;
        private static bool _cacheInitialized;
        private static bool _isRefreshing;

        public static bool IsCliInstalled()
        {
            return GetCachedCliVersion() != null;
        }

        public static string GetCachedCliVersion()
        {
            return _cacheInitialized ? _cachedCliVersion : null;
        }

        public static string GetCachedCliExecutablePath()
        {
            return _cacheInitialized ? _cachedCliExecutablePath : null;
        }

        public static bool IsCheckCompleted()
        {
            return _cacheInitialized;
        }

        public static async Task RefreshCliVersionAsync(CancellationToken ct)
        {
            if (_cacheInitialized || _isRefreshing)
            {
                return;
            }

            _isRefreshing = true;
            try
            {
                CliInstallationDetection detection = await DetectCliInstallationAsync(ct);
                _cachedCliVersion = detection.Version;
                _cachedCliExecutablePath = detection.ExecutablePath;
                _cacheInitialized = true;
            }
            finally
            {
                _isRefreshing = false;
            }
        }

        public static bool AreSkillsInstalled(string targetDir)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(targetDir), "targetDir must not be null or empty");

            string projectRoot = UnityMcpPathResolver.GetProjectRoot();
            return AreSkillsInstalled(projectRoot, targetDir);
        }

        public static bool AreSkillsInstalled(string targetDir, bool groupSkillsUnderUnityCliLoop)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(targetDir), "targetDir must not be null or empty");

            string projectRoot = UnityMcpPathResolver.GetProjectRoot();
            return AreSkillsInstalled(projectRoot, targetDir, groupSkillsUnderUnityCliLoop);
        }

        internal static bool AreSkillsInstalled(string projectRoot, string targetDir)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(targetDir), "targetDir must not be null or empty");

            string targetRoot = Path.Combine(projectRoot, targetDir);
            return SkillInstallLayout.HasInstalledSkills(projectRoot, targetRoot);
        }

        internal static bool AreSkillsInstalled(
            string projectRoot,
            string targetDir,
            bool groupSkillsUnderUnityCliLoop)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(targetDir), "targetDir must not be null or empty");

            string targetRoot = Path.Combine(projectRoot, targetDir);
            return SkillInstallLayout.HasInstalledSkills(projectRoot, targetRoot, groupSkillsUnderUnityCliLoop);
        }

        public static async Task ForceRefreshCliVersionAsync(CancellationToken ct)
        {
            CliInstallationDetection detection = await DetectCliInstallationAsync(ct);
            _cachedCliVersion = detection.Version;
            _cachedCliExecutablePath = detection.ExecutablePath;
            _cacheInitialized = true;
        }

        public static void InvalidateCache()
        {
            _cachedCliVersion = null;
            _cachedCliExecutablePath = null;
            _cacheInitialized = false;
            _isRefreshing = false;
        }

        private static Task<CliInstallationDetection> DetectCliInstallationAsync(CancellationToken ct)
        {
            RuntimePlatform platform = Application.platform;
            return Task.Run(() => DetectCliInstallationBlocking(platform, ct), ct);
        }

        internal static string DetectCliVersionBlocking(RuntimePlatform platform, CancellationToken ct)
        {
            return DetectCliInstallationBlocking(platform, ct).Version;
        }

        internal static CliInstallationDetection DetectCliInstallationBlocking(RuntimePlatform platform, CancellationToken ct)
        {
            string executablePath = NodeEnvironmentResolver.FindExecutablePathAtPlatform(
                CliConstants.EXECUTABLE_NAME,
                platform);
            // FindExecutablePath resolves .cmd shims on Windows via 'where' command
            string fileName = executablePath ?? CliConstants.EXECUTABLE_NAME;

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = CliConstants.VERSION_FLAG,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            Process process = ProcessStartHelper.TryStart(startInfo);
            if (process == null)
            {
                return new CliInstallationDetection(null, executablePath);
            }

            StringBuilder outputBuilder = new StringBuilder();

            process.OutputDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    outputBuilder.Append(e.Data);
                }
            };
            process.ErrorDataReceived += (sender, e) => { };

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using CancellationTokenRegistration registration = ct.Register(() =>
            {
                try { process.Kill(); } catch (System.InvalidOperationException) { }
            });

            try
            {
                bool exited = process.WaitForExit(PROCESS_TIMEOUT_MS);

                if (!exited)
                {
                    try { process.Kill(); } catch (System.InvalidOperationException) { }
                    process.Dispose();
                    return new CliInstallationDetection(null, executablePath);
                }

                // Parameterless WaitForExit flushes async output buffers
                process.WaitForExit();

                string output = outputBuilder.ToString().Trim();
                bool failed = process.ExitCode != 0 || string.IsNullOrEmpty(output);
                process.Dispose();

                string version = failed ? null : output;
                return new CliInstallationDetection(version, executablePath);
            }
            catch (Exception ex)
            {
                process.Dispose();
                if (!ct.IsCancellationRequested)
                {
                    UnityEngine.Debug.LogWarning($"[UnityCliLoop] Failed to detect CLI version: {ex.Message}");
                }
                return new CliInstallationDetection(null, executablePath);
            }
        }
    }

    internal static class EditorTimestamp
    {
        public static double Now()
        {
            return UnityEditor.EditorApplication.timeSinceStartup;
        }
    }
}
