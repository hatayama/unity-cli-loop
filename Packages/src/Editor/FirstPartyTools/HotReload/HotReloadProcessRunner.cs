using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using UnityEngine;

using Debug = UnityEngine.Debug;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Shared process launch helper for the hot-reload worker bootstrap and client.
    /// </summary>
    internal static class HotReloadProcessRunner
    {
        public static async Task<(int exitCode, string standardOutput, string standardError)> RunAsync(
            string fileName,
            string arguments,
            string workingDirectoryPath,
            TimeSpan timeout,
            CancellationToken ct)
        {
            Debug.Assert(!string.IsNullOrEmpty(fileName), "fileName must not be empty.");

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectoryPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using Process process = Process.Start(startInfo);
            Debug.Assert(process != null, "Failed to start process: " + fileName);

            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();
            // Task.Run around WaitForExit mirrors RoslynCompilerBackend / spike S2 — Process has
            // no awaitable wait on this runtime.
            Task waitForExitTask = Task.Run(() => process.WaitForExit(), ct);
            Task delayTask = Task.Delay(timeout, ct);
            Task completedTask = await Task.WhenAny(waitForExitTask, delayTask).ConfigureAwait(true);
            ct.ThrowIfCancellationRequested();

            if (completedTask != waitForExitTask)
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }

                // Drain redirected streams before disposing the process so a late ObjectDisposedException
                // cannot surface as an unobserved task fault in the Editor.
                string timedOutStdout = await stdoutTask.ConfigureAwait(true);
                string timedOutStderr = await stderrTask.ConfigureAwait(true);
                return (
                    -1,
                    timedOutStdout,
                    "Process timed out after " + timeout.TotalSeconds + "s.\n" + timedOutStderr);
            }

            process.WaitForExit();
            string standardOutput = await stdoutTask.ConfigureAwait(true);
            string standardError = await stderrTask.ConfigureAwait(true);
            return (process.ExitCode, standardOutput, standardError);
        }
    }
}
