using System.Diagnostics;
using System.Threading;

using NUnit.Framework;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies CLI detection command execution behavior.
    /// </summary>
    public class CliDetectionCommandRunnerTests
    {
        /// <summary>
        /// Verifies that command execution preserves stdout line order and the exit code.
        /// </summary>
        [Test]
        public void Execute_WhenCommandWritesMultipleLines_ReturnsOrderedLinesAndExitCode()
        {
            ProcessStartInfo startInfo = BuildOutputProcessStartInfo(0);

            CliDetectionCommandResult result = CliDetectionCommandRunner.Execute(
                startInfo,
                CancellationToken.None);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.StandardOutputLines, Is.EqualTo(new[] { "first", "second" }));
            Assert.That(result.ExitCode, Is.Zero);
        }

        /// <summary>
        /// Verifies that command execution returns captured output for the detection policy to reject.
        /// </summary>
        [Test]
        public void Execute_WhenCommandExitsWithFailure_ReturnsOutputAndExitCode()
        {
            ProcessStartInfo startInfo = BuildOutputProcessStartInfo(7);

            CliDetectionCommandResult result = CliDetectionCommandRunner.Execute(
                startInfo,
                CancellationToken.None);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.StandardOutputLines, Is.EqualTo(new[] { "first", "second" }));
            Assert.That(result.ExitCode, Is.EqualTo(7));
        }

        /// <summary>
        /// Verifies that a missing executable is represented by an absent execution result.
        /// </summary>
        [Test]
        public void Execute_WhenExecutableDoesNotExist_ReturnsNull()
        {
            ProcessStartInfo startInfo = CreateStartInfo(
                "__uloop_missing_cli_detection_test_executable__",
                string.Empty);

            CliDetectionCommandResult result = CliDetectionCommandRunner.Execute(
                startInfo,
                CancellationToken.None);

            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Verifies that process cleanup tolerates the child exiting before Kill.
        /// </summary>
        [Test]
        public void KillProcessIfRunning_WhenProcessAlreadyExited_DoesNotThrow()
        {
            ProcessStartInfo startInfo = BuildImmediateExitProcessStartInfo();

            using Process process = Process.Start(startInfo);
            process.WaitForExit();

            Assert.DoesNotThrow(() => CliDetectionCommandRunner.KillProcessIfRunning(process));
        }

        private static ProcessStartInfo BuildOutputProcessStartInfo(int exitCode)
        {
            if (UnityEngine.Application.platform == RuntimePlatform.WindowsEditor)
            {
                return CreateStartInfo(
                    "cmd.exe",
                    $"/d /s /c \"echo first&&echo second&&exit /b {exitCode}\"");
            }

            return CreateStartInfo(
                "/bin/sh",
                $"-c \"printf 'first\\nsecond\\n'; exit {exitCode}\"");
        }

        private static ProcessStartInfo BuildImmediateExitProcessStartInfo()
        {
            if (UnityEngine.Application.platform == RuntimePlatform.WindowsEditor)
            {
                return CreateStartInfo("cmd.exe", "/c exit 0");
            }

            return CreateStartInfo("/bin/sh", "-c \"exit 0\"");
        }

        private static ProcessStartInfo CreateStartInfo(string fileName, string arguments)
        {
            return new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
        }
    }
}
