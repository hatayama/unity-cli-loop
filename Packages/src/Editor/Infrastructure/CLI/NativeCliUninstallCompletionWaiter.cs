using System;
using System.Threading;
using System.Threading.Tasks;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.Application;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Waits for native CLI uninstall cleanup to finish.
    /// </summary>
    internal static class NativeCliUninstallCompletionWaiter
    {
        internal const int UNINSTALL_COMPLETION_TIMEOUT_MS = 30000;

        internal static async Task<CliInstallResult> WaitForUninstallTargetRemovalAsync(
            string targetPath,
            CancellationToken ct,
            int timeoutMs,
            int pollMs,
            Func<string, bool> fileExists,
            Func<int, CancellationToken, Task> delayAsync)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(targetPath), "targetPath must not be null or empty");
            UnityEngine.Debug.Assert(timeoutMs > 0, "timeoutMs must be greater than zero");
            UnityEngine.Debug.Assert(pollMs > 0, "pollMs must be greater than zero");
            UnityEngine.Debug.Assert(fileExists != null, "fileExists must not be null");
            UnityEngine.Debug.Assert(delayAsync != null, "delayAsync must not be null");

            int elapsedMs = 0;
            while (fileExists(targetPath))
            {
                ct.ThrowIfCancellationRequested();
                if (elapsedMs >= timeoutMs)
                {
                    return new CliInstallResult(
                        false,
                        $"Timed out waiting for uLoop CLI uninstall to remove {targetPath}.");
                }

                int delayMs = Math.Min(pollMs, timeoutMs - elapsedMs);
                await delayAsync(delayMs, ct);
                elapsedMs += delayMs;
            }

            return new CliInstallResult(true, "");
        }

        internal static async Task<CliInstallResult> WaitForUninstallCompletionAsync(
            string targetPath,
            string installDirectory,
            RuntimePlatform platform,
            CancellationToken ct,
            int timeoutMs,
            int pollMs,
            Func<string, bool> fileExists,
            bool requireUserPathRemoval,
            Func<string, EnvironmentVariableTarget, string> getEnvironmentVariable,
            Func<int, CancellationToken, Task> delayAsync)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(targetPath), "targetPath must not be null or empty");
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(installDirectory), "installDirectory must not be null or empty");
            UnityEngine.Debug.Assert(timeoutMs > 0, "timeoutMs must be greater than zero");
            UnityEngine.Debug.Assert(pollMs > 0, "pollMs must be greater than zero");
            UnityEngine.Debug.Assert(fileExists != null, "fileExists must not be null");
            UnityEngine.Debug.Assert(getEnvironmentVariable != null, "getEnvironmentVariable must not be null");
            UnityEngine.Debug.Assert(delayAsync != null, "delayAsync must not be null");

            int elapsedMs = 0;
            while (true)
            {
                bool targetStillExists = fileExists(targetPath);
                bool userPathStillContainsInstallDirectory = ShouldWaitForUserPathRemoval(
                    requireUserPathRemoval,
                    installDirectory,
                    platform,
                    getEnvironmentVariable);
                if (!targetStillExists && !userPathStillContainsInstallDirectory)
                {
                    return new CliInstallResult(true, "");
                }

                ct.ThrowIfCancellationRequested();
                if (elapsedMs >= timeoutMs)
                {
                    return new CliInstallResult(
                        false,
                        BuildUninstallCompletionTimeoutFailure(
                            targetPath,
                            installDirectory,
                            platform,
                            targetStillExists,
                            userPathStillContainsInstallDirectory));
                }

                int delayMs = Math.Min(pollMs, timeoutMs - elapsedMs);
                await delayAsync(delayMs, ct);
                elapsedMs += delayMs;
            }
        }

        private static bool ShouldWaitForUserPathRemoval(
            bool requireUserPathRemoval,
            string installDirectory,
            RuntimePlatform platform,
            Func<string, EnvironmentVariableTarget, string> getEnvironmentVariable)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(installDirectory), "installDirectory must not be null or empty");
            UnityEngine.Debug.Assert(getEnvironmentVariable != null, "getEnvironmentVariable must not be null");

            return requireUserPathRemoval
                && NativeCliInstallPathResolver.DoesUserPathContainInstallDirectory(
                    installDirectory,
                    platform,
                    getEnvironmentVariable);
        }

        private static string BuildUninstallCompletionTimeoutFailure(
            string targetPath,
            string installDirectory,
            RuntimePlatform platform,
            bool targetStillExists,
            bool userPathStillContainsInstallDirectory)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(targetPath), "targetPath must not be null or empty");
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(installDirectory), "installDirectory must not be null or empty");
            UnityEngine.Debug.Assert(targetStillExists || userPathStillContainsInstallDirectory, "at least one uninstall cleanup condition must still be pending");

            if (platform != RuntimePlatform.WindowsEditor || !userPathStillContainsInstallDirectory)
            {
                return $"Timed out waiting for uLoop CLI uninstall to remove {targetPath}.";
            }

            if (!targetStillExists)
            {
                return $"Timed out waiting for uLoop CLI uninstall to remove {installDirectory} from Windows User PATH.";
            }

            return $"Timed out waiting for uLoop CLI uninstall to remove {targetPath} and remove {installDirectory} from Windows User PATH.";
        }
    }
}
