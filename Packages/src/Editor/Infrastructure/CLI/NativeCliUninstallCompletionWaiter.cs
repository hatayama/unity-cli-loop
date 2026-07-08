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

        internal static async Task<CliInstallResult> WaitForUninstallCompletionAsync(
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
            while (true)
            {
                bool targetStillExists = fileExists(targetPath);
                if (!targetStillExists)
                {
                    return new CliInstallResult(true, "");
                }

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
        }
    }
}
