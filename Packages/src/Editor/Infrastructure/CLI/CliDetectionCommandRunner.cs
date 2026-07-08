using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Describes the observable result of a completed CLI detection command.
    /// </summary>
    internal sealed class CliDetectionCommandResult
    {
        internal CliDetectionCommandResult(IReadOnlyList<string> standardOutputLines, int exitCode)
        {
            UnityEngine.Debug.Assert(standardOutputLines != null, "standardOutputLines must not be null");

            StandardOutputLines = standardOutputLines;
            ExitCode = exitCode;
        }

        internal IReadOnlyList<string> StandardOutputLines { get; }
        internal int ExitCode { get; }
    }

    /// <summary>
    /// Runs CLI detection commands and captures their process-level results.
    /// </summary>
    internal static class CliDetectionCommandRunner
    {
        private const int PROCESS_TIMEOUT_MS = 5000;

        internal static CliDetectionCommandResult Execute(ProcessStartInfo startInfo, CancellationToken ct)
        {
            UnityEngine.Debug.Assert(startInfo != null, "startInfo must not be null");
            UnityEngine.Debug.Assert(startInfo.RedirectStandardOutput, "RedirectStandardOutput must be true");
            UnityEngine.Debug.Assert(startInfo.RedirectStandardError, "RedirectStandardError must be true");

            Process process = ProcessStartHelper.TryStart(startInfo);
            if (process == null)
            {
                return null;
            }

            using (process)
            {
                List<string> standardOutputLines = new();
                process.OutputDataReceived += (sender, e) =>
                {
                    if (e.Data != null)
                    {
                        standardOutputLines.Add(e.Data);
                    }
                };
                process.ErrorDataReceived += (sender, e) => { };

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                using CancellationTokenRegistration registration = ct.Register(() => KillProcessIfRunning(process));
                bool exited = process.WaitForExit(PROCESS_TIMEOUT_MS);
                if (!exited)
                {
                    KillProcessIfRunning(process);
                    return null;
                }

                // Parameterless WaitForExit flushes async output buffers.
                process.WaitForExit();
                return new CliDetectionCommandResult(standardOutputLines.ToArray(), process.ExitCode);
            }
        }

        internal static void KillProcessIfRunning(Process process)
        {
            UnityEngine.Debug.Assert(process != null, "process must not be null");

            try
            {
                process.Kill();
            }
            catch (System.InvalidOperationException)
            {
                // Process exit can race with cancellation or timeout cleanup.
            }
        }
    }
}
