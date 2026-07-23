using System;
using System.Diagnostics;
using System.Text;
using System.Threading;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Runs native CLI install and uninstall processes.
    /// </summary>
    internal static class NativeCliSetupCommandRunner
    {
        internal const int INSTALL_PROCESS_WAIT_SLICE_MS = 250;

        internal static CliInstallResult RunInstallCommand(
            NativeCliInstallCommand command,
            CancellationToken ct,
            int timeoutMs,
            Action<string> onOutputLine)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(command.FileName), "command.FileName must not be null or empty");
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(command.Arguments), "command.Arguments must not be null or empty");
            UnityEngine.Debug.Assert(timeoutMs > 0, "timeoutMs must be greater than zero");
            UnityEngine.Debug.Assert(onOutputLine != null, "onOutputLine must not be null");
            ct.ThrowIfCancellationRequested();

            return RunCliSetupCommand(
                command,
                ct,
                timeoutMs,
                "release CLI installer",
                onOutputLine,
                startInfo => { });
        }

        internal static CliInstallResult RunUninstallCommand(
            NativeCliInstallCommand command,
            string installDirectory,
            CancellationToken ct,
            int timeoutMs)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(installDirectory), "installDirectory must not be null or empty");

            return RunCliSetupCommand(
                command,
                ct,
                timeoutMs,
                "global CLI uninstall command",
                static _ => { },
                startInfo =>
                {
                    startInfo.EnvironmentVariables[CliConstants.INSTALL_DIR_ENVIRONMENT_VARIABLE] = installDirectory;
                });
        }

        private static CliInstallResult RunCliSetupCommand(
            NativeCliInstallCommand command,
            CancellationToken ct,
            int timeoutMs,
            string commandDescription,
            Action<string> onOutputLine,
            Action<ProcessStartInfo> configureStartInfo)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(command.FileName), "command.FileName must not be null or empty");
            UnityEngine.Debug.Assert(!string.IsNullOrEmpty(command.Arguments), "command.Arguments must not be null or empty");
            UnityEngine.Debug.Assert(timeoutMs > 0, "timeoutMs must be greater than zero");
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(commandDescription), "commandDescription must not be null or empty");
            UnityEngine.Debug.Assert(onOutputLine != null, "onOutputLine must not be null");
            UnityEngine.Debug.Assert(configureStartInfo != null, "configureStartInfo must not be null");
            ct.ThrowIfCancellationRequested();

            ProcessStartInfo startInfo = new()
            {
                FileName = command.FileName,
                Arguments = command.Arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            configureStartInfo(startInfo);

            Process process = ProcessStartHelper.TryStart(startInfo);
            if (process == null)
            {
                return new CliInstallResult(
                    false,
                    $"Failed to start {commandDescription}: {command.FileName}");
            }

            StringBuilder standardOutputBuilder = new();
            StringBuilder errorOutputBuilder = new();
            process.OutputDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    standardOutputBuilder.AppendLine(e.Data);
                    onOutputLine(e.Data);
                }
            };
            process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    errorOutputBuilder.AppendLine(e.Data);
                    onOutputLine(e.Data);
                }
            };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            bool canceled;
            bool exited = WaitForInstallProcessExit(process, ct, timeoutMs, out canceled);
            if (!exited)
            {
                KillProcessIfRunning(process);
                process.WaitForExit(INSTALL_PROCESS_WAIT_SLICE_MS);
                string timedOutStandardOutput = standardOutputBuilder.ToString();
                string timedOutErrorOutput = errorOutputBuilder.ToString();
                process.Dispose();

                if (canceled)
                {
                    return new CliInstallResult(
                        false,
                        $"{BuildSentenceSubject(commandDescription)} was canceled.");
                }

                return new CliInstallResult(
                    false,
                    BuildCliSetupCommandTimeoutFailure(
                        commandDescription,
                        timeoutMs,
                        timedOutErrorOutput,
                        timedOutStandardOutput));
            }

            process.WaitForExit();
            string standardOutput = standardOutputBuilder.ToString();
            string errorOutput = errorOutputBuilder.ToString();
            bool success = process.ExitCode == 0;
            process.Dispose();

            return success
                ? new CliInstallResult(true, standardOutput)
                : new CliInstallResult(false, BuildCliSetupCommandFailure(commandDescription, errorOutput, standardOutput));
        }

        private static string BuildCliSetupCommandFailure(
            string commandDescription,
            string errorOutput,
            string standardOutput)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(commandDescription), "commandDescription must not be null or empty");

            if (!string.IsNullOrWhiteSpace(errorOutput))
            {
                return errorOutput;
            }

            if (!string.IsNullOrWhiteSpace(standardOutput))
            {
                return standardOutput;
            }

            return $"{BuildSentenceSubject(commandDescription)} failed without output.";
        }

        private static string BuildCliSetupCommandTimeoutFailure(
            string commandDescription,
            int timeoutMs,
            string errorOutput,
            string standardOutput)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(commandDescription), "commandDescription must not be null or empty");

            string capturedOutput = BuildCliSetupCommandFailure(commandDescription, errorOutput, standardOutput);
            string noOutputMessage = $"{BuildSentenceSubject(commandDescription)} failed without output.";
            if (string.Equals(capturedOutput, noOutputMessage, StringComparison.Ordinal))
            {
                return $"{BuildSentenceSubject(commandDescription)} timed out after {timeoutMs} ms.";
            }

            return $"{BuildSentenceSubject(commandDescription)} timed out after {timeoutMs} ms.\n{capturedOutput}";
        }

        private static string BuildSentenceSubject(string value)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(value), "value must not be null or empty");

            return char.ToUpperInvariant(value[0]) + value.Substring(1);
        }

        private static bool WaitForInstallProcessExit(
            Process process,
            CancellationToken ct,
            int timeoutMs,
            out bool canceled)
        {
            UnityEngine.Debug.Assert(process != null, "process must not be null");
            UnityEngine.Debug.Assert(timeoutMs > 0, "timeoutMs must be greater than zero");

            canceled = false;
            int remainingMs = timeoutMs;
            while (remainingMs > 0)
            {
                if (ct.IsCancellationRequested)
                {
                    canceled = true;
                    return false;
                }

                int waitMs = Math.Min(INSTALL_PROCESS_WAIT_SLICE_MS, remainingMs);
                if (process.WaitForExit(waitMs))
                {
                    return true;
                }

                remainingMs -= waitMs;
            }

            return false;
        }

        private static void KillProcessIfRunning(Process process)
        {
            UnityEngine.Debug.Assert(process != null, "process must not be null");

            try
            {
                if (process.HasExited)
                {
                    return;
                }

                process.Kill();
            }
            catch (InvalidOperationException)
            {
                // Process exit can race with Kill, and timeout/cancel still needs to return a CliInstallResult.
            }
        }
    }
}
