using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

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
            // Do not pass ct to WaitForExit — cancel must take the timeout branch so we can kill
            // and drain before throwing; otherwise the child keeps writing cache/temp files.
            Task waitForExitTask = Task.Run(() => process.WaitForExit());
            Task delayTask = Task.Delay(timeout, ct);
            // No Unity APIs here — ConfigureAwait(false) is required so a paused Play Mode
            // SynchronizationContext cannot strand these process waits.
            Task completedTask = await Task.WhenAny(waitForExitTask, delayTask).ConfigureAwait(false);

            if (completedTask != waitForExitTask)
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }

                // Drain redirected streams before disposing the process so a late ObjectDisposedException
                // cannot surface as an unobserved task fault in the Editor.
                string timedOutStdout = await stdoutTask.ConfigureAwait(false);
                string timedOutStderr = await stderrTask.ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                return (
                    -1,
                    timedOutStdout,
                    "Process timed out after " + timeout.TotalSeconds + "s.\n" + timedOutStderr);
            }

            // Process finished; return its result even if ct fired at the same moment.
            process.WaitForExit();
            string standardOutput = await stdoutTask.ConfigureAwait(false);
            string standardError = await stderrTask.ConfigureAwait(false);
            return (process.ExitCode, standardOutput, standardError);
        }
    }
}
