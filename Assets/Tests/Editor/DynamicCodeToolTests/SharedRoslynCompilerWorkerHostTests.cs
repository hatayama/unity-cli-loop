using System;
using System.Diagnostics;
using System.IO;
using System.Text;
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
            startInfo.EnvironmentVariables[SharedRoslynCompilerWorkerHost.DotnetMultilevelLookupEnvironmentVariableName] = "1";

            SharedRoslynCompilerWorkerHost.ConfigureWorkerDotnetRuntimeEnvironment(startInfo);

            Assert.That(
                startInfo.EnvironmentVariables[SharedRoslynCompilerWorkerHost.DotnetMultilevelLookupEnvironmentVariableName],
                Is.EqualTo(SharedRoslynCompilerWorkerHost.DotnetMultilevelLookupDisabledValue));
        }

        [Test]
        public void CreateCompileRequestCommand_WhenPathIsWindowsAbsolutePath_ShouldEncodeAsciiPayload()
        {
            string requestFilePath =
                @"C:\Users\ExampleUser\Documents\unity\SampleWorkspace\SampleUnityProject\Temp\UnityCliLoopCompilation\DynamicCommand_1.worker";

            string command = SharedRoslynCompilerWorkerHost.CreateCompileRequestCommandForTests(requestFilePath);

            Assert.That(command, Does.StartWith(SharedRoslynCompilerWorkerHost.CompileRequestPathPrefix));
            Assert.That(command, Does.Not.Contain(requestFilePath));
            foreach (char character in command)
            {
                Assert.That(character, Is.LessThanOrEqualTo((char)127));
            }

            string encodedPath = command.Substring(SharedRoslynCompilerWorkerHost.CompileRequestPathPrefix.Length);
            string decodedPath = Encoding.UTF8.GetString(Convert.FromBase64String(encodedPath));
            Assert.That(decodedPath, Is.EqualTo(Path.GetFullPath(requestFilePath)));
        }

        [Test]
        public void TryParseResponseHeader_WhenHeaderContainsExitCode_ShouldReturnParsedCode()
        {
            // Verifies the worker protocol accepts its result prefix followed by a numeric exit code.
            bool parsed = SharedRoslynCompilerWorkerHost.TryParseResponseHeader("__ULOOP_RESULT__ 7", out int exitCode);

            Assert.That(parsed, Is.True);
            Assert.That(exitCode, Is.EqualTo(7));
        }

        [Test]
        public void GetResponseHeaderFailureReason_WhenPrefixIsInvalid_ShouldReportInvalidHeader()
        {
            // Verifies a response without the worker result prefix is classified as an invalid header.
            string failureReason = SharedRoslynCompilerWorkerHost.GetResponseHeaderFailureReason("unexpected response");

            Assert.That(failureReason, Is.EqualTo("worker_invalid_header"));
        }

        [Test]
        public void GetResponseHeaderFailureReason_WhenExitCodeIsInvalid_ShouldReportInvalidExitCode()
        {
            // Verifies a prefixed response with a non-numeric status is classified as an invalid exit code.
            string failureReason = SharedRoslynCompilerWorkerHost.GetResponseHeaderFailureReason(
                "__ULOOP_RESULT__ not-a-number");

            Assert.That(failureReason, Is.EqualTo("worker_invalid_exit_code"));
        }

        [Test]
        public void CreateProgramSource_WhenRequestPathHasNoPrefix_ShouldRecoverRawPath()
        {
            string programSource = SharedRoslynCompilerWorkerHost.CreateProgramSourceForTests();

            Assert.That(programSource, Does.Contain("return RecoverRawRequestPath(requestPath);"));
            Assert.That(programSource, Does.Contain("FindWindowsDrivePathIndex"));
            Assert.That(programSource, Does.Not.Contain("Unsupported request path protocol"));
        }

        [Test]
        public void CreateProgramSource_WhenTemplateIsLoaded_ShouldReplaceTokens()
        {
            string templatePath = SharedRoslynCompilerWorkerHost.GetWorkerProgramTemplatePathForTests();
            string programSource = SharedRoslynCompilerWorkerHost.CreateProgramSourceForTests();

            Assert.That(File.Exists(templatePath), Is.True);
            Assert.That(programSource, Does.Contain(SharedRoslynCompilerWorkerHost.CompileRequestPathPrefix));
            Assert.That(programSource, Does.Contain(
                SharedRoslynCompilerWorkerHost.GetSharedCompilerWorkerResultPrefixForTests()));
            Assert.That(programSource, Does.Contain(
                SharedRoslynCompilerWorkerHost.GetSharedCompilerWorkerEndMarkerForTests()));
            Assert.That(programSource, Does.Contain(
                SharedRoslynCompilerWorkerHost.GetSharedCompilerWorkerQuitCommandForTests()));
            Assert.That(programSource, Does.Not.Contain("{{"));
        }

        [Test]
        public void CreateProgramSource_WhenRequestPathPrefixHasLeadingGarbage_ShouldDecodeEncodedPath()
        {
            string programSource = SharedRoslynCompilerWorkerHost.CreateProgramSourceForTests();

            Assert.That(programSource, Does.Contain("FindRequestPathPrefixIndex"));
            Assert.That(programSource, Does.Contain("IndexOf(RequestPathPrefix"));
            Assert.That(programSource, Does.Contain("encodedPathIndex + RequestPathPrefix.Length"));
        }

        [Test]
        public void CreateProgramSource_WhenRawPathContainsPrefixAfterDirectorySeparator_ShouldRecoverRawPath()
        {
            string programSource = SharedRoslynCompilerWorkerHost.CreateProgramSourceForTests();

            Assert.That(programSource, Does.Contain("HasDirectorySeparatorBeforePrefix"));
            Assert.That(programSource, Does.Contain("return HasDirectorySeparatorBeforePrefix(requestPath, encodedPathIndex) ? -1 : encodedPathIndex;"));
        }

        [Test]
        public void CreateProgramSource_WhenEncodedPayloadIsMalformed_ShouldRecoverRawPath()
        {
            string programSource = SharedRoslynCompilerWorkerHost.CreateProgramSourceForTests();

            Assert.That(programSource, Does.Contain("IsBase64Payload"));
            Assert.That(programSource, Does.Contain("HasValidBase64Padding"));
            Assert.That(programSource, Does.Contain("return RecoverRawRequestPath(requestPath);"));
            Assert.That(programSource, Does.Not.Contain("catch (FormatException)"));
        }
    }
}
