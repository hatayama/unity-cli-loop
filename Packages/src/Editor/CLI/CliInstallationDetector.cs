using System.Diagnostics;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace io.github.hatayama.uLoopMCP
{
    public static class CliInstallationDetector
    {
        private const int PROCESS_TIMEOUT_MS = 5000;

        private static string _cachedCliVersion;
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
                string version = await DetectCliVersionAsync(ct);
                _cachedCliVersion = version;
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
            return SkillInstallLayout.HasInstalledSkills(targetRoot);
        }

        internal static bool AreSkillsInstalled(
            string projectRoot,
            string targetDir,
            bool groupSkillsUnderUnityCliLoop)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(targetDir), "targetDir must not be null or empty");

            string targetRoot = Path.Combine(projectRoot, targetDir);
            return SkillInstallLayout.HasInstalledSkills(targetRoot, groupSkillsUnderUnityCliLoop);
        }

        public static async Task ForceRefreshCliVersionAsync(CancellationToken ct)
        {
            string version = await DetectCliVersionAsync(ct);
            _cachedCliVersion = version;
            _cacheInitialized = true;
        }

        public static void InvalidateCache()
        {
            _cachedCliVersion = null;
            _cacheInitialized = false;
            _isRefreshing = false;
        }

        private static Task<string> DetectCliVersionAsync(CancellationToken ct)
        {
            RuntimePlatform platform = Application.platform;
            return Task.Run(() => DetectCliVersionBlocking(platform, ct), ct);
        }

        internal static string DetectCliVersionBlocking(RuntimePlatform platform, CancellationToken ct)
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

            string nodePath = NodeEnvironmentResolver.FindNodePathAtPlatform(platform);
            NodeEnvironmentResolver.SetupEnvironmentPathAtPlatform(startInfo, nodePath, platform);

            Process process = ProcessStartHelper.TryStart(startInfo);
            if (process == null)
            {
                return null;
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
                    return null;
                }

                // Parameterless WaitForExit flushes async output buffers
                process.WaitForExit();

                string output = outputBuilder.ToString().Trim();
                bool failed = process.ExitCode != 0 || string.IsNullOrEmpty(output);
                process.Dispose();

                return failed ? null : output;
            }
            catch (Exception ex)
            {
                process.Dispose();
                if (!ct.IsCancellationRequested)
                {
                    UnityEngine.Debug.LogWarning($"[uLoopMCP] Failed to detect CLI version: {ex.Message}");
                }
                return null;
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
