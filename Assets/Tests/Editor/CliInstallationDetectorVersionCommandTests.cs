using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;

using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies how the CLI installation detector runs and interprets the CLI version commands.
    /// </summary>
    public sealed class CliInstallationDetectorVersionCommandTests
    {
        private const string ExecutablePath = "<PROJECT_ROOT>/bin/uloop";
        private const string ContractArguments = CliConstants.VERSION_FLAG + " " + CliConstants.JSON_FLAG;

        /// <summary>
        /// Verifies a version read from the JSON contract output is used without running the plain version command.
        /// </summary>
        [Test]
        public void DetectCliInstallationAtExecutablePath_WhenContractReportsVersion_UsesContractVersion()
        {
            RecordingCommandRunner runner = new();
            runner.Add(ContractArguments, new CliDetectionCommandResult(new[] { "{\"DispatcherVersion\":\"3.6.2\"}" }, 0));

            CliInstallationDetection detection = CliInstallationDetector.DetectCliInstallationAtExecutablePath(
                ExecutablePath,
                CancellationToken.None,
                runner.Execute);

            Assert.That(detection.Version, Is.EqualTo("3.6.2"));
            Assert.That(detection.IsDispatcher, Is.True);
            Assert.That(detection.ExecutablePath, Is.EqualTo(ExecutablePath));
            Assert.That(runner.Arguments, Is.EqualTo(new[] { ContractArguments }));
            Assert.That(runner.FileNames, Is.EqualTo(new[] { ExecutablePath }));
        }

        /// <summary>
        /// Verifies the plain version output is used when the JSON contract output has no version.
        /// </summary>
        [Test]
        public void DetectCliInstallationAtExecutablePath_WhenContractHasNoVersion_FallsBackToPlainVersion()
        {
            RecordingCommandRunner runner = new();
            runner.Add(ContractArguments, new CliDetectionCommandResult(new[] { "not json" }, 0));
            runner.Add(CliConstants.VERSION_FLAG, new CliDetectionCommandResult(new[] { "2.9.0" }, 0));

            CliInstallationDetection detection = CliInstallationDetector.DetectCliInstallationAtExecutablePath(
                ExecutablePath,
                CancellationToken.None,
                runner.Execute);

            Assert.That(detection.Version, Is.EqualTo("2.9.0"));
            Assert.That(detection.IsDispatcher, Is.False);
            Assert.That(detection.ExecutablePath, Is.EqualTo(ExecutablePath));
            Assert.That(runner.Arguments, Is.EqualTo(new[] { ContractArguments, CliConstants.VERSION_FLAG }));
        }

        /// <summary>
        /// Verifies both version commands failing reports no version while keeping the executable path.
        /// </summary>
        [Test]
        public void DetectCliInstallationAtExecutablePath_WhenBothCommandsFail_ReportsNoVersion()
        {
            RecordingCommandRunner runner = new();
            runner.Add(ContractArguments, new CliDetectionCommandResult(Array.Empty<string>(), 1));
            runner.Add(CliConstants.VERSION_FLAG, new CliDetectionCommandResult(new[] { "2.9.0" }, 1));

            CliInstallationDetection detection = CliInstallationDetector.DetectCliInstallationAtExecutablePath(
                ExecutablePath,
                CancellationToken.None,
                runner.Execute);

            Assert.That(detection.Version, Is.Null);
            Assert.That(detection.ExecutablePath, Is.EqualTo(ExecutablePath));
        }

        /// <summary>
        /// Verifies a missing executable path runs the commands through the bare executable name.
        /// </summary>
        [Test]
        public void DetectCliInstallationAtExecutablePath_WhenPathIsNull_RunsExecutableName()
        {
            RecordingCommandRunner runner = new();
            runner.Add(ContractArguments, new CliDetectionCommandResult(new[] { "{\"ProjectRunnerVersion\":\"3.6.0\"}" }, 0));

            CliInstallationDetection detection = CliInstallationDetector.DetectCliInstallationAtExecutablePath(
                null,
                CancellationToken.None,
                runner.Execute);

            Assert.That(detection.Version, Is.EqualTo("3.6.0"));
            Assert.That(detection.ExecutablePath, Is.Null);
            Assert.That(runner.FileNames, Is.EqualTo(new[] { CliConstants.EXECUTABLE_NAME }));
        }

        /// <summary>
        /// Verifies a successful command returns its output lines joined and trimmed, and runs with redirected output.
        /// </summary>
        [Test]
        public void ExecuteCliVersionCommand_WhenCommandSucceeds_ReturnsTrimmedOutput()
        {
            RecordingCommandRunner runner = new();
            runner.Add(CliConstants.VERSION_FLAG, new CliDetectionCommandResult(new[] { "  3.6", ".0  " }, 0));

            string output = CliInstallationDetector.ExecuteCliVersionCommand(
                ExecutablePath,
                CliConstants.VERSION_FLAG,
                CancellationToken.None,
                runner.Execute);

            Assert.That(output, Is.EqualTo("3.6.0"));
            ProcessStartInfo startInfo = runner.StartInfos[0];
            Assert.That(startInfo.UseShellExecute, Is.False);
            Assert.That(startInfo.RedirectStandardOutput, Is.True);
            Assert.That(startInfo.RedirectStandardError, Is.True);
            Assert.That(startInfo.CreateNoWindow, Is.True);
        }

        /// <summary>
        /// Verifies a command that could not run, exited with an error, or printed nothing yields no output.
        /// </summary>
        [TestCase(false, 0, "3.6.0")]
        [TestCase(true, 1, "3.6.0")]
        [TestCase(true, 0, "   ")]
        public void ExecuteCliVersionCommand_WhenCommandDoesNotSucceed_ReturnsNull(
            bool hasResult,
            int exitCode,
            string outputLine)
        {
            RecordingCommandRunner runner = new();
            runner.Add(
                CliConstants.VERSION_FLAG,
                hasResult ? new CliDetectionCommandResult(new[] { outputLine }, exitCode) : null);

            string output = CliInstallationDetector.ExecuteCliVersionCommand(
                ExecutablePath,
                CliConstants.VERSION_FLAG,
                CancellationToken.None,
                runner.Execute);

            Assert.That(output, Is.Null);
        }

        /// <summary>
        /// Verifies a command that throws yields no output and logs a warning.
        /// </summary>
        [Test]
        public void ExecuteCliVersionCommand_WhenCommandThrows_ReturnsNullAndWarns()
        {
            LogAssert.Expect(LogType.Warning, new Regex("Failed to detect CLI version: command failed in this test"));

            string output = CliInstallationDetector.ExecuteCliVersionCommand(
                ExecutablePath,
                CliConstants.VERSION_FLAG,
                CancellationToken.None,
                ThrowingRunner);

            Assert.That(output, Is.Null);
        }

        /// <summary>
        /// Verifies a command that throws after cancellation yields no output without logging a warning.
        /// </summary>
        [Test]
        public void ExecuteCliVersionCommand_WhenCommandThrowsAfterCancellation_ReturnsNullSilently()
        {
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();

            string output = CliInstallationDetector.ExecuteCliVersionCommand(
                ExecutablePath,
                CliConstants.VERSION_FLAG,
                cancellation.Token,
                ThrowingRunner);

            Assert.That(output, Is.Null);
            LogAssert.NoUnexpectedReceived();
        }

        private static CliDetectionCommandResult ThrowingRunner(ProcessStartInfo startInfo, CancellationToken ct)
        {
            throw new InvalidOperationException("command failed in this test");
        }

        private sealed class RecordingCommandRunner
        {
            private readonly Dictionary<string, CliDetectionCommandResult> _resultsByArguments = new();

            public List<ProcessStartInfo> StartInfos { get; } = new();

            public List<string> Arguments
            {
                get
                {
                    List<string> arguments = new();
                    foreach (ProcessStartInfo startInfo in StartInfos)
                    {
                        arguments.Add(startInfo.Arguments);
                    }

                    return arguments;
                }
            }

            public List<string> FileNames
            {
                get
                {
                    List<string> fileNames = new();
                    foreach (ProcessStartInfo startInfo in StartInfos)
                    {
                        fileNames.Add(startInfo.FileName);
                    }

                    return fileNames;
                }
            }

            public void Add(string arguments, CliDetectionCommandResult result)
            {
                _resultsByArguments.Add(arguments, result);
            }

            public CliDetectionCommandResult Execute(ProcessStartInfo startInfo, CancellationToken ct)
            {
                StartInfos.Add(startInfo);
                return _resultsByArguments[startInfo.Arguments];
            }
        }
    }
}
