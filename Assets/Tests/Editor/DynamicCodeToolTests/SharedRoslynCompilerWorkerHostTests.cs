using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEditor.Compilation;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Test fixture that verifies Shared Roslyn Compiler Worker Host behavior.
    /// </summary>
    [TestFixture]
    public class SharedRoslynCompilerWorkerHostTests
    {
        private const string WorkerSourceA = "// worker source A";
        private const string WorkerSourceB = "// worker source B";

        /// <summary>
        /// Waits for a shared-worker warm-up that started before the run.
        /// </summary>
        [UnitySetUp]
        public IEnumerator WaitForSharedWorkerWarmUp()
        {
            Task warmUp = DynamicCodeServices.GetRegistry().GetSharedWorkerWarmUpTaskForTests();
            while (!warmUp.IsCompleted)
            {
                yield return null;
            }

            // Why shut it down: the warm-up leaves a worker running, and tests here count and
            // replace the worker process.
            SharedRoslynCompilerWorkerHost.ShutdownForTests();
        }

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
        /// What: the worker reference set includes the consolidated System.Security.Cryptography assembly when that file exists.
        /// </summary>
        [Test]
        public void BuildWorkerReferenceSet_WhenConsolidatedCryptographyAssemblyExists_ShouldIncludeConsolidatedReference()
        {
            ExternalCompilerPaths externalCompilerPaths = ExternalCompilerPathResolver.Resolve();
            Assert.That(externalCompilerPaths, Is.Not.Null, "Unity external compiler layout should be available.");

            string consolidatedAssemblyPath = Path.Combine(
                externalCompilerPaths.NetCoreRuntimeSharedDirectoryPath,
                "System.Security.Cryptography.dll");
            if (!File.Exists(consolidatedAssemblyPath))
            {
                Assert.Ignore(
                    "System.Security.Cryptography.dll is not present in this Unity NetCoreRuntime shared directory.");
            }

            List<string> references =
                SharedRoslynCompilerWorkerAssemblyBuilder.BuildWorkerReferenceSet(externalCompilerPaths);

            Assert.That(references, Does.Contain(consolidatedAssemblyPath));
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

        /// <summary>
        /// Verifies that the first ensure in an empty cache builds the assembly and publishes the same bytes to the cache.
        /// </summary>
        [Test]
        public async Task EnsureWorkerAssemblyBuilt_FirstTime_BuildsAndPublishesToTheCache()
        {
            using WorkerAssemblyEnsureFixture fixture = new();
            fixture.SynchronizeSource(WorkerSourceA);

            WorkerAssemblyEnsureResult result = await fixture.EnsureAsync(WorkerSourceA);

            Assert.That(fixture.BuildCount, Is.EqualTo(1));
            Assert.That(result.Result.IsReady, Is.True);
            Assert.That(result.AssemblySource, Is.EqualTo("built"));
            Assert.That(result.Publish.Kind, Is.EqualTo(CachePublishKind.Published));
            string cachedAssemblyPath = fixture.ResolveCachedAssemblyPath(WorkerSourceA);
            Assert.That(File.Exists(cachedAssemblyPath), Is.True);
            Assert.That(
                File.ReadAllBytes(cachedAssemblyPath),
                Is.EqualTo(File.ReadAllBytes(fixture.WorkerPaths.AssemblyPath)));
        }

        /// <summary>
        /// Verifies that a fresh worker directory, as after a domain reload, gets the cached assembly
        /// copied without running the build again.
        /// </summary>
        [Test]
        public async Task EnsureWorkerAssemblyBuilt_InAFreshWorkerDirectoryWithACachedAssembly_CopiesAndDoesNotBuild()
        {
            using WorkerAssemblyEnsureFixture fixture = new();
            fixture.SynchronizeSource(WorkerSourceA);
            await fixture.EnsureAsync(WorkerSourceA);
            File.Delete(fixture.WorkerPaths.AssemblyPath);

            WorkerAssemblyEnsureResult result = await fixture.EnsureAsync(WorkerSourceA);

            Assert.That(fixture.BuildCount, Is.EqualTo(1));
            Assert.That(result.Result.IsReady, Is.True);
            Assert.That(result.AssemblySource, Is.EqualTo("cache"));
            Assert.That(result.Publish.Kind, Is.EqualTo(CachePublishKind.NotAttempted));
            Assert.That(File.Exists(fixture.WorkerPaths.AssemblyPath), Is.True);
        }

        /// <summary>
        /// Verifies that a changed worker source misses the cache, builds again, and is cached under a second key.
        /// </summary>
        [Test]
        public async Task EnsureWorkerAssemblyBuilt_WhenTheSourceChanged_BuildsAgain()
        {
            using WorkerAssemblyEnsureFixture fixture = new();
            fixture.SynchronizeSource(WorkerSourceA);
            await fixture.EnsureAsync(WorkerSourceA);
            fixture.SynchronizeSource(WorkerSourceB);

            WorkerAssemblyEnsureResult result = await fixture.EnsureAsync(WorkerSourceB);

            Assert.That(fixture.BuildCount, Is.EqualTo(2));
            Assert.That(result.AssemblySource, Is.EqualTo("built"));
            Assert.That(Directory.GetDirectories(fixture.CacheRoot).Length, Is.EqualTo(2));
        }

        /// <summary>
        /// Verifies that a build with errors fails the worker start and leaves nothing in the cache.
        /// </summary>
        [Test]
        public async Task EnsureWorkerAssemblyBuilt_WhenTheBuildHasErrors_PublishesNothing()
        {
            using WorkerAssemblyEnsureFixture fixture = new();
            fixture.BuildFails = true;
            fixture.SynchronizeSource(WorkerSourceA);

            WorkerAssemblyEnsureResult result = await fixture.EnsureAsync(WorkerSourceA);

            Assert.That(result.Result.IsReady, Is.False);
            Assert.That(result.Result.FailureReason, Is.EqualTo("worker_build_failed"));
            Assert.That(result.AssemblySource, Is.Empty);
            Assert.That(result.Publish.Kind, Is.EqualTo(CachePublishKind.NotAttempted));
            Assert.That(File.Exists(fixture.WorkerPaths.AssemblyPath), Is.False);
            Assert.That(
                !Directory.Exists(fixture.CacheRoot) || Directory.GetFileSystemEntries(fixture.CacheRoot).Length == 0,
                Is.True,
                "A failed build must not create a cache entry.");
        }

        /// <summary>
        /// Verifies that shutdown deletes the per-process worker directory but keeps the cached assembly.
        /// </summary>
        [Test]
        public async Task Shutdown_DeletesTheWorkerDirectoryAndKeepsTheCache()
        {
            using WorkerAssemblyEnsureFixture fixture = new();
            fixture.SynchronizeSource(WorkerSourceA);
            await fixture.EnsureAsync(WorkerSourceA);

            fixture.Session.Shutdown(fixture.WorkerPaths.DirectoryPath);

            Assert.That(Directory.Exists(fixture.WorkerPaths.DirectoryPath), Is.False);
            Assert.That(File.Exists(fixture.ResolveCachedAssemblyPath(WorkerSourceA)), Is.True);
        }

        /// <summary>
        /// Verifies that a cache that cannot be written still lets the worker start with the assembly it built,
        /// and that the failure is reported in the publish outcome.
        /// </summary>
        [Test]
        public async Task EnsureWorkerAssemblyBuilt_WhenTheCachePublishFails_IsStillReady()
        {
            using WorkerAssemblyEnsureFixture fixture = new();
            string blockingFile = Path.Combine(fixture.WorkerPaths.DirectoryPath, "cache-root-is-a-file");
            File.WriteAllText(blockingFile, "not a directory");
            Func<string> previousResolver = fixture.Session.SwapWorkerAssemblyCacheRootForTests(() => blockingFile);
            try
            {
                fixture.SynchronizeSource(WorkerSourceA);

                WorkerAssemblyEnsureResult result = await fixture.EnsureAsync(WorkerSourceA);

                Assert.That(fixture.BuildCount, Is.EqualTo(1));
                Assert.That(result.Result.IsReady, Is.True);
                Assert.That(result.AssemblySource, Is.EqualTo("built"));
                Assert.That(result.Publish.Kind, Is.EqualTo(CachePublishKind.Failed));
                Assert.That(result.Publish.Error, Is.Not.Empty);
                Assert.That(File.Exists(fixture.WorkerPaths.AssemblyPath), Is.True);
            }
            finally
            {
                fixture.Session.SwapWorkerAssemblyCacheRootForTests(previousResolver);
            }
        }

        /// <summary>
        /// Verifies that an assembly already in the worker directory is used as is: no build, and the
        /// cache is neither read nor written, because nothing checked where that assembly came from.
        /// </summary>
        [Test]
        public async Task EnsureWorkerAssemblyBuilt_WhenTheWorkerAssemblyExists_DoesNotTouchTheCache()
        {
            using WorkerAssemblyEnsureFixture fixture = new();
            fixture.SynchronizeSource(WorkerSourceA);
            File.WriteAllBytes(fixture.WorkerPaths.AssemblyPath, new byte[] { 0x4D, 0x5A, 0x07 });

            WorkerAssemblyEnsureResult result = await fixture.EnsureAsync(WorkerSourceA);

            Assert.That(fixture.BuildCount, Is.EqualTo(0));
            Assert.That(result.AssemblySource, Is.EqualTo("existing"));
            Assert.That(result.Publish.Kind, Is.EqualTo(CachePublishKind.NotAttempted));
            Assert.That(Directory.Exists(fixture.CacheRoot), Is.False);
        }

        /// <summary>
        /// Verifies a warm-up whose worker cannot start logs nothing, and leaves the once-only error
        /// report to the next real compile.
        /// </summary>
        [Test]
        public async Task WarmUpAsync_WhenTheWorkerCannotStart_LogsNothingAndLeavesTheErrorToTheNextCompile()
        {
            ExternalCompilerPaths paths = ExternalCompilerPathResolver.Resolve();
            Assert.That(paths, Is.Not.Null);
            string[] references = { typeof(object).Assembly.Location };
            string directory = CreateTemporaryDirectory();
            string requestFilePath = WriteWorkerRequest(directory, references);
            SharedRoslynCompilerWorkerHost.ShutdownForTests();
            DynamicCompilationHealthMonitor.ResetForTests();
            using SharedRoslynCompilerWorkerCacheScope cacheScope = SharedRoslynCompilerWorkerCacheScope.ForHost();
            Func<ExternalCompilerPaths, string, string, string, CompilerMessage[]> previousCompiler =
                SharedRoslynCompilerWorkerHost.SwapWorkerAssemblyCompilerForTests(
                    (ExternalCompilerPaths _, string __, string ___, string ____) =>
                        new[]
                        {
                            new CompilerMessage
                            {
                                type = CompilerMessageType.Error,
                                message = "worker unavailable"
                            }
                        });
            try
            {
                SharedWorkerWarmUpOutcome outcome = await Task.Run(
                    () => SharedRoslynCompilerWorkerHost.WarmUpAsync(references, paths));

                Assert.That(outcome.Outcome, Is.EqualTo(SharedWorkerWarmUpOutcome.OutcomeStartFailed));
                LogAssert.NoUnexpectedReceived();

                LogAssert.Expect(
                    UnityEngine.LogType.Error,
                    new System.Text.RegularExpressions.Regex(
                        "execute-dynamic-code shared Roslyn worker failed to operate correctly; reason=worker_build_failed"));
                SharedWorkerCompileOutcome compile = await Task.Run(
                    () => SharedRoslynCompilerWorkerHost.TryCompileAsync(
                        requestFilePath,
                        paths,
                        System.Threading.CancellationToken.None,
                        () => { },
                        () => { },
                        () => { }));
                Assert.That(compile.Succeeded, Is.False);
            }
            finally
            {
                SharedRoslynCompilerWorkerHost.SwapWorkerAssemblyCompilerForTests(previousCompiler);
                SharedRoslynCompilerWorkerHost.ShutdownForTests();
                DynamicCompilationHealthMonitor.ResetForTests();
                Directory.Delete(directory, true);
            }
        }

        /// <summary>
        /// Verifies a warm-up leaves the worker running, so the next compile reuses it instead of
        /// starting another.
        /// </summary>
        [Test]
        public async Task WarmUpAsync_LeavesARunningWorkerThatTheNextCompileReuses()
        {
            ExternalCompilerPaths paths = ExternalCompilerPathResolver.Resolve();
            Assert.That(paths, Is.Not.Null);
            string[] references = { typeof(object).Assembly.Location };
            string directory = CreateTemporaryDirectory();
            string requestFilePath = WriteWorkerRequest(directory, references);
            SharedRoslynCompilerWorkerHost.ShutdownForTests();
            using SharedRoslynCompilerWorkerCacheScope cacheScope = SharedRoslynCompilerWorkerCacheScope.ForHost();
            try
            {
                SharedWorkerWarmUpOutcome outcome = await Task.Run(
                    () => SharedRoslynCompilerWorkerHost.WarmUpAsync(references, paths));
                Assert.That(outcome.Outcome, Is.EqualTo(SharedWorkerWarmUpOutcome.OutcomeAnswered));
                Assert.That(outcome.ErrorCount, Is.EqualTo(0));

                VibeLogger.ClearMemoryLogs();
                SharedWorkerCompileOutcome compile = await Task.Run(
                    () => SharedRoslynCompilerWorkerHost.TryCompileAsync(
                        requestFilePath,
                        paths,
                        System.Threading.CancellationToken.None,
                        () => { },
                        () => { },
                        () => { }));

                Assert.That(compile.Succeeded, Is.True);
                Assert.That(
                    JArray.Parse(VibeLogger.GetLogsForAi("dynamic_code_shared_worker_started")).Count,
                    Is.EqualTo(0));
            }
            finally
            {
                SharedRoslynCompilerWorkerHost.ShutdownForTests();
                Directory.Delete(directory, true);
            }
        }

        /// <summary>
        /// Verifies a warm-up that finds the worker already running sends it nothing.
        /// </summary>
        [Test]
        public async Task WarmUpAsync_WhenAWorkerAlreadyRuns_SendsNothing()
        {
            ExternalCompilerPaths paths = ExternalCompilerPathResolver.Resolve();
            Assert.That(paths, Is.Not.Null);
            string[] references = { typeof(object).Assembly.Location };
            SharedRoslynCompilerWorkerHost.ShutdownForTests();
            using SharedRoslynCompilerWorkerCacheScope cacheScope = SharedRoslynCompilerWorkerCacheScope.ForHost();
            try
            {
                await Task.Run(() => SharedRoslynCompilerWorkerHost.WarmUpAsync(references, paths));
                SharedWorkerWarmUpOutcome second = await Task.Run(
                    () => SharedRoslynCompilerWorkerHost.WarmUpAsync(references, paths));

                Assert.That(second.Outcome, Is.EqualTo(SharedWorkerWarmUpOutcome.OutcomeAlreadyRunning));
            }
            finally
            {
                SharedRoslynCompilerWorkerHost.ShutdownForTests();
            }
        }

        /// <summary>
        /// Verifies the warm-up deletes the source, request and assembly it wrote once the worker answered.
        /// </summary>
        [Test]
        public async Task WarmUpAsync_RemovesItsFilesAfterTheWorkerAnswers()
        {
            ExternalCompilerPaths paths = ExternalCompilerPathResolver.Resolve();
            Assert.That(paths, Is.Not.Null);
            string[] references = { typeof(object).Assembly.Location };
            SharedRoslynCompilerWorkerHost.ShutdownForTests();
            using SharedRoslynCompilerWorkerCacheScope cacheScope = SharedRoslynCompilerWorkerCacheScope.ForHost();
            try
            {
                SharedWorkerWarmUpOutcome outcome = await Task.Run(
                    () => SharedRoslynCompilerWorkerHost.WarmUpAsync(references, paths));

                Assert.That(outcome.Outcome, Is.EqualTo(SharedWorkerWarmUpOutcome.OutcomeAnswered));
                Assert.That(Directory.Exists(SharedRoslynCompilerWorkerHostProcess.GetWarmUpDirectoryPath()), Is.False);
            }
            finally
            {
                SharedRoslynCompilerWorkerHost.ShutdownForTests();
            }
        }

        private static string CreateTemporaryDirectory()
        {
            string directory = Path.Combine(Path.GetTempPath(), "shared-worker-warm-up-" + Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static string WriteWorkerRequest(string directory, IReadOnlyList<string> references)
        {
            string sourcePath = Path.Combine(directory, "Probe.cs");
            string requestFilePath = Path.Combine(directory, "Probe.worker");
            File.WriteAllText(sourcePath, "internal static class SharedWorkerWarmUpProbe { }");
            RoslynCompilerRequestFileWriter.WriteWorkerRequestFile(
                requestFilePath,
                sourcePath,
                Path.Combine(directory, "Probe.dll"),
                references,
                Array.Empty<string>(),
                allowUnsafeCode: false,
                emitDebugCode: true);
            return requestFilePath;
        }

        /// <summary>
        /// A session with a fake csc, an empty test-owned cache, and a temp worker directory, for
        /// driving the worker assembly ensure step directly.
        /// </summary>
        private sealed class WorkerAssemblyEnsureFixture : IDisposable
        {
            private readonly SharedRoslynCompilerWorkerCacheScope _cacheScope;
            private readonly ExternalCompilerPaths _paths;

            public SharedRoslynCompilerWorkerSession Session { get; }

            public SharedRoslynCompilerWorkerHostProcess.WorkerPaths WorkerPaths { get; }

            public int BuildCount { get; private set; }

            public bool BuildFails { get; set; }

            public string CacheRoot => _cacheScope.CacheRoot;

            public WorkerAssemblyEnsureFixture()
            {
                _paths = ExternalCompilerPathResolver.Resolve();
                Assert.That(_paths, Is.Not.Null, "Unity external compiler layout should be available.");
                Session = new SharedRoslynCompilerWorkerSession();
                _cacheScope = SharedRoslynCompilerWorkerCacheScope.ForSession(Session);
                string workerDirectoryPath = Path.Combine(
                    Path.GetTempPath(),
                    "RoslynWorkerHostTests_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(workerDirectoryPath);
                WorkerPaths = new SharedRoslynCompilerWorkerHostProcess.WorkerPaths(
                    workerDirectoryPath,
                    Path.Combine(workerDirectoryPath, "RoslynCompilerWorker.cs"),
                    Path.Combine(workerDirectoryPath, "RoslynCompilerWorker.dll"),
                    Path.Combine(workerDirectoryPath, "RoslynCompilerWorker.rsp"));
                Session.SwapWorkerAssemblyCompilerForTests(BuildWithFakeCompiler);
            }

            public void SynchronizeSource(string workerSource)
            {
                SharedRoslynCompilerWorkerHostProcess.SynchronizeWorkerSource(WorkerPaths, workerSource);
            }

            public Task<WorkerAssemblyEnsureResult> EnsureAsync(string workerSource)
            {
                return SharedRoslynCompilerWorkerHostProcess.EnsureWorkerAssemblyBuiltAsync(
                    Session,
                    _paths,
                    WorkerPaths,
                    workerSource);
            }

            public string ResolveCachedAssemblyPath(string workerSource)
            {
                return SharedRoslynCompilerWorkerAssemblyCache.ResolveCachedAssemblyPath(
                    CacheRoot,
                    SharedRoslynCompilerWorkerAssemblyCache.ComputeCacheKey(workerSource, _paths));
            }

            public void Dispose()
            {
                _cacheScope.Dispose();
                if (Directory.Exists(WorkerPaths.DirectoryPath))
                {
                    Directory.Delete(WorkerPaths.DirectoryPath, true);
                }
            }

            private CompilerMessage[] BuildWithFakeCompiler(
                ExternalCompilerPaths paths,
                string sourcePath,
                string assemblyPath,
                string responseFilePath)
            {
                BuildCount++;
                if (BuildFails)
                {
                    return new[]
                    {
                        new CompilerMessage
                        {
                            type = CompilerMessageType.Error,
                            message = "synthetic worker build failure"
                        }
                    };
                }

                File.WriteAllBytes(assemblyPath, new byte[] { 0x4D, 0x5A, 0x01 });
                return Array.Empty<CompilerMessage>();
            }
        }
    }
}
