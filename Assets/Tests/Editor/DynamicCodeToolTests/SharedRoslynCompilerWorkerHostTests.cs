using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Test fixture that verifies Shared Roslyn Compiler Worker Host behavior.
    /// </summary>
    [TestFixture]
    public class SharedRoslynCompilerWorkerHostTests
    {
        [Test]
        public void ConfigureWorkerDotnetRuntimeEnvironment_WhenCalled_ShouldDisableMultilevelLookup()
        {
            ProcessStartInfo startInfo = new();
            startInfo.EnvironmentVariables[SharedRoslynCompilerWorkerAssemblyBuilder.DotnetMultilevelLookupEnvironmentVariableName] = "1";

            SharedRoslynCompilerWorkerAssemblyBuilder.ConfigureWorkerDotnetRuntimeEnvironment(startInfo);

            Assert.That(
                startInfo.EnvironmentVariables[SharedRoslynCompilerWorkerAssemblyBuilder.DotnetMultilevelLookupEnvironmentVariableName],
                Is.EqualTo(SharedRoslynCompilerWorkerAssemblyBuilder.DotnetMultilevelLookupDisabledValue));
        }

        /// <summary>
        /// Verifies shutdown remains an idempotent no-op before a worker process or directory exists.
        /// </summary>
        [Test]
        public void Shutdown_WhenWorkerWasNeverStarted_ShouldRemainIdempotent()
        {
            SharedRoslynCompilerWorkerSession session = new();
            string unusedWorkerDirectoryPath = Path.Combine(
                Path.GetTempPath(),
                $"SharedRoslynCompilerWorkerSessionTests_{Guid.NewGuid():N}");

            Assert.That(Directory.Exists(unusedWorkerDirectoryPath), Is.False);
            Assert.DoesNotThrow(() => session.Shutdown(unusedWorkerDirectoryPath));
            Assert.DoesNotThrow(() => session.Shutdown(unusedWorkerDirectoryPath));
        }

        /// <summary>
        /// Verifies replacing the cached worker releases the previously owned process handle.
        /// </summary>
        [Test]
        public void StartProcessLocked_WhenReplacingCachedProcess_ShouldDisposePreviousHandle()
        {
            SharedRoslynCompilerWorkerSession session = new();
            Process previousProcess = new();
            bool previousProcessDisposed = false;
            int startAttempt = 0;
            previousProcess.Disposed += (sender, args) => previousProcessDisposed = true;
            session.SwapProcessStarterForTests(startInfo =>
            {
                startAttempt++;
                return startAttempt == 1 ? previousProcess : null;
            });
            ProcessStartInfo ignoredStartInfo = new();

            try
            {
                bool firstStartSucceeded = session.ExecuteLocked(
                    () => session.StartProcessLocked(ignoredStartInfo));
                bool secondStartSucceeded = session.ExecuteLocked(
                    () => session.StartProcessLocked(ignoredStartInfo));

                Assert.That(firstStartSucceeded, Is.True);
                Assert.That(secondStartSucceeded, Is.False);
                Assert.That(previousProcessDisposed, Is.True);
            }
            finally
            {
                previousProcess.Dispose();
            }
        }

        /// <summary>
        /// Verifies a pending compiler stream does not keep the bounded drain waiting.
        /// </summary>
        [Test]
        public void WaitForCompilerStreamDrain_WhenStreamIsPending_ShouldReturnFalse()
        {
            TaskCompletionSource<string> pendingStream = new();

            bool completed = SharedRoslynCompilerWorkerAssemblyBuilder.WaitForCompilerStreamDrain(
                Task.FromResult("stdout"),
                pendingStream.Task,
                0);
            pendingStream.SetResult("stderr");

            Assert.That(completed, Is.False);
        }

        /// <summary>
        /// Verifies completed compiler streams satisfy the bounded drain immediately.
        /// </summary>
        [Test]
        public void WaitForCompilerStreamDrain_WhenStreamsAreCompleted_ShouldReturnTrue()
        {
            bool completed = SharedRoslynCompilerWorkerAssemblyBuilder.WaitForCompilerStreamDrain(
                Task.FromResult("stdout"),
                Task.FromResult("stderr"),
                0);

            Assert.That(completed, Is.True);
        }

        /// <summary>
        /// Verifies a faulted compiler stream is treated as drained without escaping timeout recovery.
        /// </summary>
        [Test]
        public void WaitForCompilerStreamDrain_WhenStreamFaults_ShouldReturnTrue()
        {
            Task<string> faultedStream = Task.FromException<string>(new IOException("stream read failed"));

            bool completed = SharedRoslynCompilerWorkerAssemblyBuilder.WaitForCompilerStreamDrain(
                Task.FromResult("stdout"),
                faultedStream,
                0);

            Assert.That(completed, Is.True);
        }

        /// <summary>
        /// Verifies one faulted stream does not make a still-pending drain appear complete.
        /// </summary>
        [Test]
        public void WaitForCompilerStreamDrain_WhenFaultedStreamHasPendingPeer_ShouldReturnFalse()
        {
            TaskCompletionSource<string> pendingStream = new();
            Task<string> faultedStream = Task.FromException<string>(new IOException("stream read failed"));

            bool completed = SharedRoslynCompilerWorkerAssemblyBuilder.WaitForCompilerStreamDrain(
                faultedStream,
                pendingStream.Task,
                0);
            pendingStream.SetResult("stderr");

            Assert.That(completed, Is.False);
        }

        [Test]
        public void CreateCompileRequestCommand_WhenPathIsWindowsAbsolutePath_ShouldEncodeAsciiPayload()
        {
            string requestFilePath =
                @"C:\Users\ExampleUser\Documents\unity\SampleWorkspace\SampleUnityProject\Temp\UnityCliLoopCompilation\DynamicCommand_1.worker";

            string command = SharedRoslynCompilerWorkerProtocol.CreateCompileRequestCommand(requestFilePath);

            Assert.That(command, Does.StartWith(SharedRoslynCompilerWorkerProtocol.CompileRequestPathPrefix));
            Assert.That(command, Does.Not.Contain(requestFilePath));
            foreach (char character in command)
            {
                Assert.That(character, Is.LessThanOrEqualTo((char)127));
            }

            string encodedPath = command.Substring(SharedRoslynCompilerWorkerProtocol.CompileRequestPathPrefix.Length);
            string decodedPath = Encoding.UTF8.GetString(Convert.FromBase64String(encodedPath));
            Assert.That(decodedPath, Is.EqualTo(Path.GetFullPath(requestFilePath)));
        }

        [Test]
        public void TryParseResponseHeader_WhenHeaderContainsExitCode_ShouldReturnParsedCode()
        {
            // Verifies the worker protocol accepts its result prefix followed by a numeric exit code.
            bool parsed = SharedRoslynCompilerWorkerProtocol.TryParseResponseHeader("__ULOOP_RESULT__ 7", out int exitCode);

            Assert.That(parsed, Is.True);
            Assert.That(exitCode, Is.EqualTo(7));
        }

        [Test]
        public void GetResponseHeaderFailureReason_WhenPrefixIsInvalid_ShouldReportInvalidHeader()
        {
            // Verifies a response without the worker result prefix is classified as an invalid header.
            string failureReason = SharedRoslynCompilerWorkerProtocol.GetResponseHeaderFailureReason("unexpected response");

            Assert.That(failureReason, Is.EqualTo("worker_invalid_header"));
        }

        [Test]
        public void GetResponseHeaderFailureReason_WhenExitCodeIsInvalid_ShouldReportInvalidExitCode()
        {
            // Verifies a prefixed response with a non-numeric status is classified as an invalid exit code.
            string failureReason = SharedRoslynCompilerWorkerProtocol.GetResponseHeaderFailureReason(
                "__ULOOP_RESULT__ not-a-number");

            Assert.That(failureReason, Is.EqualTo("worker_invalid_exit_code"));
        }

        [Test]
        public void CreateProgramSource_WhenRequestPathHasNoPrefix_ShouldRecoverRawPath()
        {
            string programSource = SharedRoslynCompilerWorkerProtocol.CreateProgramSource();

            Assert.That(programSource, Does.Contain("return RecoverRawRequestPath(requestPath);"));
            Assert.That(programSource, Does.Contain("FindWindowsDrivePathIndex"));
            Assert.That(programSource, Does.Not.Contain("Unsupported request path protocol"));
        }

        [Test]
        public void CreateProgramSource_WhenTemplateIsLoaded_ShouldReplaceTokens()
        {
            string templatePath = SharedRoslynCompilerWorkerProtocol.GetWorkerProgramTemplatePath();
            string programSource = SharedRoslynCompilerWorkerProtocol.CreateProgramSource();

            Assert.That(File.Exists(templatePath), Is.True);
            Assert.That(programSource, Does.Contain(SharedRoslynCompilerWorkerProtocol.CompileRequestPathPrefix));
            Assert.That(programSource, Does.Contain(
                SharedRoslynCompilerWorkerProtocol.SharedCompilerWorkerResultPrefix));
            Assert.That(programSource, Does.Contain(
                SharedRoslynCompilerWorkerProtocol.SharedCompilerWorkerEndMarker));
            Assert.That(programSource, Does.Contain(
                SharedRoslynCompilerWorkerProtocol.SharedCompilerWorkerQuitCommand));
            Assert.That(programSource, Does.Not.Contain("{{"));
        }

        [Test]
        public void CreateProgramSource_WhenRequestPathPrefixHasLeadingGarbage_ShouldDecodeEncodedPath()
        {
            string programSource = SharedRoslynCompilerWorkerProtocol.CreateProgramSource();

            Assert.That(programSource, Does.Contain("FindRequestPathPrefixIndex"));
            Assert.That(programSource, Does.Contain("IndexOf(RequestPathPrefix"));
            Assert.That(programSource, Does.Contain("encodedPathIndex + RequestPathPrefix.Length"));
        }

        [Test]
        public void CreateProgramSource_WhenRawPathContainsPrefixAfterDirectorySeparator_ShouldRecoverRawPath()
        {
            string programSource = SharedRoslynCompilerWorkerProtocol.CreateProgramSource();

            Assert.That(programSource, Does.Contain("HasDirectorySeparatorBeforePrefix"));
            Assert.That(programSource, Does.Contain("return HasDirectorySeparatorBeforePrefix(requestPath, encodedPathIndex) ? -1 : encodedPathIndex;"));
        }

        [Test]
        public void CreateProgramSource_WhenEncodedPayloadIsMalformed_ShouldRecoverRawPath()
        {
            string programSource = SharedRoslynCompilerWorkerProtocol.CreateProgramSource();

            Assert.That(programSource, Does.Contain("IsBase64Payload"));
            Assert.That(programSource, Does.Contain("HasValidBase64Padding"));
            Assert.That(programSource, Does.Contain("return RecoverRawRequestPath(requestPath);"));
            Assert.That(programSource, Does.Not.Contain("catch (FormatException)"));
        }
    }
}
