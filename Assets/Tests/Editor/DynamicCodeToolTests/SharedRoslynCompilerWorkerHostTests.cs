using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEditor.Compilation;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Test fixture that verifies Shared Roslyn Compiler Worker Host behavior.
    /// </summary>
    [TestFixture]
    public class SharedRoslynCompilerWorkerHostTests
    {
        /// <summary>
        /// Verifies lifecycle closure is returned as a non-error compile outcome.
        /// </summary>
        [Test]
        public void SharedWorkerCompileOutcome_WhenLifecycleCloses_ShouldCarryLifecycleClosedReason()
        {
            SharedWorkerCompileOutcome outcome = SharedWorkerCompileOutcome.Failed(
                SharedWorkerFailureReasons.LifecycleClosed,
                new { reason = "lifecycle_generation_advanced" });

            Assert.That(outcome.Succeeded, Is.False);
            Assert.That(outcome.FailureReason, Is.EqualTo(SharedWorkerFailureReasons.LifecycleClosed));
            Assert.That(outcome.IsLifecycleClosed, Is.True);
        }

        /// <summary>
        /// Verifies non-lifecycle worker failures retain their failure reason for error reporting.
        /// </summary>
        [Test]
        public void SharedWorkerCompileOutcome_WhenWorkerStartFails_ShouldCarryFailureReason()
        {
            SharedWorkerCompileOutcome outcome = SharedWorkerCompileOutcome.Failed(
                "worker_start_failed",
                new { reason = "process_start_failed" });

            Assert.That(outcome.Succeeded, Is.False);
            Assert.That(outcome.FailureReason, Is.EqualTo("worker_start_failed"));
        }

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
        /// Verifies the worker reference set includes System.Security.Cryptography.Primitives when that assembly exists in the Unity runtime.
        /// </summary>
        [Test]
        public void BuildWorkerReferenceSet_WhenPrimitivesAssemblyExists_ShouldIncludePrimitivesReference()
        {
            ExternalCompilerPaths externalCompilerPaths = ExternalCompilerPathResolver.Resolve();
            Assert.That(externalCompilerPaths, Is.Not.Null, "Unity external compiler layout should be available.");

            string primitivesAssemblyPath = Path.Combine(
                externalCompilerPaths.NetCoreRuntimeSharedDirectoryPath,
                "System.Security.Cryptography.Primitives.dll");
            if (!File.Exists(primitivesAssemblyPath))
            {
                Assert.Ignore(
                    "System.Security.Cryptography.Primitives.dll is not present in this Unity NetCoreRuntime shared directory.");
            }

            List<string> references =
                SharedRoslynCompilerWorkerAssemblyBuilder.BuildWorkerReferenceSet(externalCompilerPaths);

            Assert.That(references, Does.Contain(primitivesAssemblyPath));
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
        /// Verifies full Shutdown advances lifecycle generation so in-flight retries cannot restart the worker.
        /// </summary>
        [Test]
        public void Shutdown_WhenCalled_ShouldAdvanceLifecycleGeneration()
        {
            SharedRoslynCompilerWorkerSession session = new();
            string unusedWorkerDirectoryPath = Path.Combine(
                Path.GetTempPath(),
                $"SharedRoslynCompilerWorkerSessionTests_{Guid.NewGuid():N}");

            int generationBefore = session.ExecuteWithStateLock(session.GetLifecycleGenerationLocked);
            session.Shutdown(unusedWorkerDirectoryPath);
            int generationAfter = session.ExecuteWithStateLock(session.GetLifecycleGenerationLocked);

            Assert.That(generationBefore, Is.EqualTo(0));
            Assert.That(generationAfter, Is.EqualTo(1));
            Assert.That(
                session.ExecuteWithStateLock(() => session.IsLifecycleGenerationCurrentLocked(generationBefore)),
                Is.False);
            Assert.That(
                session.ExecuteWithStateLock(() => session.IsLifecycleGenerationCurrentLocked(generationAfter)),
                Is.True);
        }

        /// <summary>
        /// Verifies retry-path process kill keeps the lifecycle open so a replacement worker may start.
        /// </summary>
        [Test]
        public void ShutdownProcessLocked_WhenCalledAlone_ShouldStillAllowWorkerRestart()
        {
            SharedRoslynCompilerWorkerSession session = new();
            Process startedProcess = null;
            int startCallCount = 0;
            session.SwapProcessStarterForTests(_ =>
            {
                startCallCount++;
                startedProcess = new Process();
                return startedProcess;
            });

            try
            {
                int generationAtStart = session.ExecuteWithStateLock(session.GetLifecycleGenerationLocked);
                session.ExecuteWithStateLock(session.ShutdownProcessLocked);

                bool started = session.ExecuteWithStateLock(
                    () =>
                    {
                        if (!session.IsLifecycleGenerationCurrentLocked(generationAtStart))
                        {
                            return false;
                        }

                        return session.StartProcessLocked(new ProcessStartInfo());
                    });

                Assert.That(started, Is.True);
                Assert.That(startCallCount, Is.EqualTo(1));
                Assert.That(
                    session.ExecuteWithStateLock(session.GetLifecycleGenerationLocked),
                    Is.EqualTo(generationAtStart));
            }
            finally
            {
                startedProcess?.Dispose();
            }
        }

        /// <summary>
        /// Verifies a stale lifecycle generation refuses StartProcess after full Shutdown.
        /// </summary>
        [Test]
        public void StartProcessLocked_WhenLifecycleGenerationIsStale_ShouldBeDetectableBeforeStart()
        {
            SharedRoslynCompilerWorkerSession session = new();
            string unusedWorkerDirectoryPath = Path.Combine(
                Path.GetTempPath(),
                $"SharedRoslynCompilerWorkerSessionTests_{Guid.NewGuid():N}");
            int startCallCount = 0;
            session.SwapProcessStarterForTests(_ =>
            {
                startCallCount++;
                return new Process();
            });

            int generationAtStart = session.ExecuteWithStateLock(session.GetLifecycleGenerationLocked);
            session.Shutdown(unusedWorkerDirectoryPath);

            bool wouldStart = session.ExecuteWithStateLock(
                () => session.IsLifecycleGenerationCurrentLocked(generationAtStart));
            Assert.That(wouldStart, Is.False);

            bool started = session.ExecuteWithStateLock(
                () =>
                {
                    if (!session.IsLifecycleGenerationCurrentLocked(generationAtStart))
                    {
                        return false;
                    }

                    return session.StartProcessLocked(new ProcessStartInfo());
                });

            Assert.That(started, Is.False);
            Assert.That(startCallCount, Is.Zero);
        }

        /// <summary>
        /// Verifies the async offload path returns the test-hook build result without requiring a real csc.
        /// </summary>
        [Test]
        public async Task CompileWorkerAssemblyAsync_WhenTestHookIsInstalled_ShouldReturnHookResult()
        {
            SharedRoslynCompilerWorkerSession session = new();
            CompilerMessage[] expectedMessages =
            {
                new CompilerMessage
                {
                    type = CompilerMessageType.Error,
                    message = "hooked"
                }
            };
            session.SwapWorkerAssemblyCompilerForTests(
                (paths, sourcePath, assemblyPath, responsePath) => expectedMessages);

            SharedRoslynCompilerWorkerAssemblyBuilder.WorkerAssemblyBuildResult result =
                await session.CompileWorkerAssemblyAsync(
                    externalCompilerPaths: null,
                    workerSourcePath: "unused.cs",
                    workerAssemblyPath: "unused.dll",
                    workerCompileResponseFilePath: "unused.rsp");

            Assert.That(result.StartedSuccessfully, Is.True);
            Assert.That(result.Messages, Is.SameAs(expectedMessages));
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
        /// Verifies a broken graceful shutdown channel still falls back to forced termination.
        /// </summary>
        [Test]
        public void ExecuteProcessShutdown_WhenGracefulRequestThrowsIOException_ShouldStillForceKill()
        {
            IOException shutdownFailure = new("worker input closed");
            Exception loggedFailure = null;
            int forceKillCallCount = 0;
            int disposeCallCount = 0;

            SharedRoslynCompilerWorkerSession.ExecuteProcessShutdown(
                hasExited: () => false,
                requestGracefulShutdown: () => throw shutdownFailure,
                forceKill: () => forceKillCallCount++,
                dispose: () => disposeCallCount++,
                logFailure: ex => loggedFailure = ex);

            Assert.That(loggedFailure, Is.SameAs(shutdownFailure));
            Assert.That(forceKillCallCount, Is.EqualTo(1));
            Assert.That(disposeCallCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies a disposed graceful shutdown channel still falls back to forced termination.
        /// </summary>
        [Test]
        public void ExecuteProcessShutdown_WhenGracefulRequestThrowsObjectDisposedException_ShouldStillForceKill()
        {
            ObjectDisposedException shutdownFailure = new("worker input");
            Exception loggedFailure = null;
            int forceKillCallCount = 0;
            int disposeCallCount = 0;

            SharedRoslynCompilerWorkerSession.ExecuteProcessShutdown(
                hasExited: () => false,
                requestGracefulShutdown: () => throw shutdownFailure,
                forceKill: () => forceKillCallCount++,
                dispose: () => disposeCallCount++,
                logFailure: ex => loggedFailure = ex);

            Assert.That(loggedFailure, Is.SameAs(shutdownFailure));
            Assert.That(forceKillCallCount, Is.EqualTo(1));
            Assert.That(disposeCallCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies an operating-system kill failure is logged without escaping process disposal.
        /// </summary>
        [Test]
        public void ExecuteProcessShutdown_WhenForceKillThrowsWin32Exception_ShouldLogAndDispose()
        {
            Win32Exception shutdownFailure = new(5, "worker kill denied");
            Exception loggedFailure = null;
            int disposeCallCount = 0;

            Assert.DoesNotThrow(() => SharedRoslynCompilerWorkerSession.ExecuteProcessShutdown(
                hasExited: () => false,
                requestGracefulShutdown: () => { },
                forceKill: () => throw shutdownFailure,
                dispose: () => disposeCallCount++,
                logFailure: ex => loggedFailure = ex));

            Assert.That(loggedFailure, Is.SameAs(shutdownFailure));
            Assert.That(disposeCallCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies an unknown exit state still attempts forced termination after logging the query failure.
        /// </summary>
        [Test]
        public void ExecuteProcessShutdown_WhenForceExitQueryThrowsWin32Exception_ShouldStillForceKill()
        {
            Win32Exception queryFailure = new(5, "worker exit code unavailable");
            Exception loggedFailure = null;
            int hasExitedCallCount = 0;
            int forceKillCallCount = 0;
            int disposeCallCount = 0;
            int failureLogCount = 0;

            SharedRoslynCompilerWorkerSession.ExecuteProcessShutdown(
                hasExited: () =>
                {
                    hasExitedCallCount++;
                    if (hasExitedCallCount == 2)
                    {
                        throw queryFailure;
                    }

                    return false;
                },
                requestGracefulShutdown: () => { },
                forceKill: () => forceKillCallCount++,
                dispose: () => disposeCallCount++,
                logFailure: ex =>
                {
                    failureLogCount++;
                    loggedFailure = ex;
                });

            Assert.That(loggedFailure, Is.SameAs(queryFailure));
            Assert.That(failureLogCount, Is.EqualTo(1));
            Assert.That(forceKillCallCount, Is.EqualTo(1));
            Assert.That(disposeCallCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies a missing associated process skips forced termination after logging the query failure.
        /// </summary>
        [Test]
        public void ExecuteProcessShutdown_WhenForceExitQueryThrowsInvalidOperationException_ShouldSkipForceKill()
        {
            InvalidOperationException queryFailure = new("worker process unavailable");
            Exception loggedFailure = null;
            int hasExitedCallCount = 0;
            int forceKillCallCount = 0;
            int disposeCallCount = 0;
            int failureLogCount = 0;

            SharedRoslynCompilerWorkerSession.ExecuteProcessShutdown(
                hasExited: () =>
                {
                    hasExitedCallCount++;
                    if (hasExitedCallCount == 2)
                    {
                        throw queryFailure;
                    }

                    return false;
                },
                requestGracefulShutdown: () => { },
                forceKill: () => forceKillCallCount++,
                dispose: () => disposeCallCount++,
                logFailure: ex =>
                {
                    failureLogCount++;
                    loggedFailure = ex;
                });

            Assert.That(loggedFailure, Is.SameAs(queryFailure));
            Assert.That(failureLogCount, Is.EqualTo(1));
            Assert.That(forceKillCallCount, Is.Zero);
            Assert.That(disposeCallCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies successful graceful shutdown skips forced termination and disposes once.
        /// </summary>
        [Test]
        public void ExecuteProcessShutdown_WhenGracefulRequestExitsProcess_ShouldSkipForceKill()
        {
            bool processExited = false;
            int forceKillCallCount = 0;
            int disposeCallCount = 0;
            int failureLogCount = 0;

            SharedRoslynCompilerWorkerSession.ExecuteProcessShutdown(
                hasExited: () => processExited,
                requestGracefulShutdown: () => processExited = true,
                forceKill: () => forceKillCallCount++,
                dispose: () => disposeCallCount++,
                logFailure: ex => failureLogCount++);

            Assert.That(forceKillCallCount, Is.Zero);
            Assert.That(failureLogCount, Is.Zero);
            Assert.That(disposeCallCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies an already exited process skips both termination phases and disposes once.
        /// </summary>
        [Test]
        public void ExecuteProcessShutdown_WhenProcessAlreadyExited_ShouldOnlyDispose()
        {
            int gracefulRequestCallCount = 0;
            int forceKillCallCount = 0;
            int disposeCallCount = 0;
            int failureLogCount = 0;

            SharedRoslynCompilerWorkerSession.ExecuteProcessShutdown(
                hasExited: () => true,
                requestGracefulShutdown: () => gracefulRequestCallCount++,
                forceKill: () => forceKillCallCount++,
                dispose: () => disposeCallCount++,
                logFailure: ex => failureLogCount++);

            Assert.That(gracefulRequestCallCount, Is.Zero);
            Assert.That(forceKillCallCount, Is.Zero);
            Assert.That(failureLogCount, Is.Zero);
            Assert.That(disposeCallCount, Is.EqualTo(1));
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

        /// <summary>
        /// Verifies the shared worker template still emits portable PDB debug information.
        /// </summary>
        [Test]
        public void CreateProgramSource_IncludesPortablePdbEmitOptions()
        {
            string programSource = SharedRoslynCompilerWorkerProtocol.CreateProgramSource();

            Assert.That(programSource, Does.Contain("DebugInformationFormat.PortablePdb"));
        }

        /// <summary>
        /// Verifies the worker template forces UTF-8 stdout so diagnostics survive non-UTF-8 default codepages.
        /// </summary>
        [Test]
        public void CreateProgramSource_SetsUtf8ConsoleOutputEncoding()
        {
            string programSource = SharedRoslynCompilerWorkerProtocol.CreateProgramSource();

            Assert.That(programSource, Does.Contain("Console.OutputEncoding = Encoding.UTF8;"));
        }

        /// <summary>
        /// Verifies the worker start info decodes stdout as UTF-8 to match the worker-side Console.OutputEncoding.
        /// </summary>
        [Test]
        public void CreateWorkerStartInfo_SetsUtf8StandardOutputEncoding()
        {
            ExternalCompilerPaths externalCompilerPaths = ExternalCompilerPathResolver.Resolve();
            Assert.That(externalCompilerPaths, Is.Not.Null, "Unity external compiler layout should be available.");
            SharedRoslynCompilerWorkerHostProcess.WorkerPaths workerPaths = new(
                "worker-dir",
                "worker-dir/RoslynCompilerWorker.cs",
                "worker-dir/RoslynCompilerWorker.dll",
                "worker-dir/RoslynCompilerWorker.rsp");

            ProcessStartInfo startInfo = SharedRoslynCompilerWorkerHostProcess.CreateWorkerStartInfo(
                externalCompilerPaths,
                workerPaths);

            Assert.That(startInfo.StandardOutputEncoding, Is.EqualTo(Encoding.UTF8));
            Assert.That(startInfo.StandardErrorEncoding, Is.Null);
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
