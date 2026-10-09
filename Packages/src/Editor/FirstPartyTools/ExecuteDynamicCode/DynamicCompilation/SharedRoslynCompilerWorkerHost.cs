using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Compilation;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Provides Shared Roslyn Compiler Worker Host behavior for Unity CLI Loop.
    /// </summary>
    internal static class SharedRoslynCompilerWorkerHost
    {
        private const int SharedCompilerWorkerMaxAttempts = 2;
        private const string WarmUpSourceFileName = "SharedWorkerWarmUp.cs";
        private const string WarmUpAssemblyFileName = "SharedWorkerWarmUp.dll";
        private const string WarmUpRequestFileName = "SharedWorkerWarmUp.worker";
        private const string WarmUpSource = "internal sealed class UnityCliLoopSharedWorkerWarmUp { }";

        private static readonly SharedRoslynCompilerWorkerSession ServiceValue = new();

        internal static void RegisterLifecycleForEditorStartup()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= ShutdownForReload;
            AssemblyReloadEvents.beforeAssemblyReload += ShutdownForReload;
            EditorApplication.quitting -= ShutdownForQuit;
            EditorApplication.quitting += ShutdownForQuit;
        }

        public static Task<SharedWorkerCompileOutcome> TryCompileAsync(
            string requestFilePath,
            ExternalCompilerPaths externalCompilerPaths,
            CancellationToken ct,
            Action markBuildStarted,
            Action markBuildFinished,
            Action incrementBuildCount)
        {
            return ServiceValue.RunSerializedCompileAsync(
                operationCt => TryCompileWithRetriesAsync(
                    requestFilePath,
                    externalCompilerPaths,
                    operationCt,
                    markBuildStarted,
                    markBuildFinished,
                    incrementBuildCount),
                ct);
        }

        /// <summary>
        /// Starts the shared worker when none runs and has it compile a tiny source against
        /// <paramref name="references"/>, so a later compile finds the worker and those references
        /// loaded. Any thread once the package path has been read on the main thread. Reports nothing
        /// to the health monitor: a failure here is retried by the next real compile, which reports it.
        /// </summary>
        internal static Task<SharedWorkerWarmUpOutcome> WarmUpAsync(
            IReadOnlyList<string> references,
            ExternalCompilerPaths externalCompilerPaths)
        {
            return ServiceValue.RunSerializedCompileAsync(
                _ => WarmUpInsideCompileGateAsync(references, externalCompilerPaths),
                CancellationToken.None);
        }

        private static async Task<SharedWorkerWarmUpOutcome> WarmUpInsideCompileGateAsync(
            IReadOnlyList<string> references,
            ExternalCompilerPaths externalCompilerPaths)
        {
            int lifecycleGeneration = 0;
            bool hasLiveProcess = false;
            ServiceValue.ExecuteWithStateLock(() =>
            {
                lifecycleGeneration = ServiceValue.GetLifecycleGenerationLocked();
                hasLiveProcess = ServiceValue.HasLiveProcessLocked();
            });
            // Why not compile again: a compile that got the gate first started the worker, and its
            // session already paid the cold start this warm-up exists to move.
            if (hasLiveProcess)
            {
                return SharedWorkerWarmUpOutcome.AlreadyRunning();
            }

            WorkerStartupResult startupResult = await SharedRoslynCompilerWorkerHostProcess.EnsureWorkerReadyAsync(
                ServiceValue,
                externalCompilerPaths,
                lifecycleGeneration).ConfigureAwait(false);
            if (!startupResult.IsReady)
            {
                ServiceValue.ExecuteWithStateLock(ServiceValue.ShutdownProcessLocked);
                return SharedWorkerWarmUpOutcome.StartFailed(startupResult.FailureReason);
            }

            string directory = SharedRoslynCompilerWorkerHostProcess.GetWarmUpDirectoryPath();
            Directory.CreateDirectory(directory);
            try
            {
                string sourcePath = Path.Combine(directory, WarmUpSourceFileName);
                string requestFilePath = Path.Combine(directory, WarmUpRequestFileName);
                File.WriteAllText(sourcePath, WarmUpSource);
                // Same options as the hot-reload shim compile, so the same compiler paths get compiled.
                RoslynCompilerRequestFileWriter.WriteWorkerRequestFile(
                    requestFilePath,
                    sourcePath,
                    Path.Combine(directory, WarmUpAssemblyFileName),
                    references,
                    Array.Empty<string>(),
                    allowUnsafeCode: false,
                    emitDebugCode: true);
                // Why no token: cancelling a read kills the worker, and only a shutdown stops a warm-up.
                WorkerAttemptResult attemptResult = await InvokeWorkerOnceAsync(
                    requestFilePath,
                    CancellationToken.None,
                    NoOperation,
                    NoOperation,
                    NoOperation).ConfigureAwait(false);
                if (!attemptResult.Succeeded)
                {
                    ServiceValue.ExecuteWithStateLock(ServiceValue.ShutdownProcessLocked);
                    return SharedWorkerWarmUpOutcome.RequestFailed(attemptResult.FailureReason);
                }

                return SharedWorkerWarmUpOutcome.Answered(CountErrors(attemptResult.Messages));
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        private static void NoOperation()
        {
        }

        private static int CountErrors(CompilerMessage[] messages)
        {
            int errorCount = 0;
            foreach (CompilerMessage message in messages)
            {
                if (message.type == CompilerMessageType.Error)
                {
                    errorCount++;
                }
            }

            return errorCount;
        }

        private static async Task<SharedWorkerCompileOutcome> TryCompileWithRetriesAsync(
            string requestFilePath,
            ExternalCompilerPaths externalCompilerPaths,
            CancellationToken ct,
            Action markBuildStarted,
            Action markBuildFinished,
            Action incrementBuildCount)
        {
            int lifecycleGenerationAtStart = ServiceValue.ExecuteWithStateLock(
                ServiceValue.GetLifecycleGenerationLocked);

            for (int attempt = 1; attempt <= SharedCompilerWorkerMaxAttempts; attempt++)
            {
                WorkerAttemptResult attemptResult = await TryCompileOnceAsync(
                    requestFilePath,
                    externalCompilerPaths,
                    lifecycleGenerationAtStart,
                    ct,
                    markBuildStarted,
                    markBuildFinished,
                    incrementBuildCount).ConfigureAwait(false);

                if (attemptResult.Succeeded)
                {
                    return SharedWorkerCompileOutcome.SucceededWith(attemptResult.Messages);
                }

                ServiceValue.ExecuteWithStateLock(ServiceValue.ShutdownProcessLocked);

                if (attemptResult.ShouldRetry && attempt < SharedCompilerWorkerMaxAttempts)
                {
                    continue;
                }

                object failureContext = AppendAttempt(attemptResult.FailureContext, attempt);
                if (attemptResult.FailureReason == SharedWorkerFailureReasons.LifecycleClosed)
                {
                    DynamicCompilationHealthMonitor.ReportSharedWorkerLifecycleClosed(failureContext);
                }
                else
                {
                    DynamicCompilationHealthMonitor.ReportSharedWorkerFailure(
                        attemptResult.FailureReason,
                        failureContext);
                }

                return SharedWorkerCompileOutcome.Failed(attemptResult.FailureReason, failureContext);
            }

            return SharedWorkerCompileOutcome.Failed(
                "worker_unknown_failure",
                new { reason = "retry_loop_exhausted" });
        }

        private static async Task<WorkerAttemptResult> TryCompileOnceAsync(
            string requestFilePath,
            ExternalCompilerPaths externalCompilerPaths,
            int lifecycleGenerationAtStart,
            CancellationToken ct,
            Action markBuildStarted,
            Action markBuildFinished,
            Action incrementBuildCount)
        {
            WorkerStartupResult startupResult = await SharedRoslynCompilerWorkerHostProcess.EnsureWorkerReadyAsync(
                ServiceValue,
                externalCompilerPaths,
                lifecycleGenerationAtStart).ConfigureAwait(false);
            if (!startupResult.IsReady)
            {
                if (!startupResult.IsRetryable)
                {
                    return WorkerAttemptResult.NonRetryableFailure(
                        startupResult.FailureReason,
                        startupResult.FailureContext);
                }

                return WorkerAttemptResult.RetryableFailure(
                    startupResult.FailureReason,
                    startupResult.FailureContext);
            }

            return await InvokeWorkerOnceAsync(
                requestFilePath,
                ct,
                markBuildStarted,
                markBuildFinished,
                incrementBuildCount).ConfigureAwait(false);
        }

        private static async Task<WorkerAttemptResult> InvokeWorkerOnceAsync(
            string requestFilePath,
            CancellationToken ct,
            Action markBuildStarted,
            Action markBuildFinished,
            Action incrementBuildCount)
        {
            StreamReader reader = null;
            // Why not send here: stdin WriteLine/Flush can throw IOException if the worker dies
            // after HasLiveProcessLocked; keep send inside the retryable try below.
            bool prepared = ServiceValue.ExecuteWithStateLock(() =>
            {
                if (!ServiceValue.HasLiveProcessLocked())
                {
                    return false;
                }

                reader = ServiceValue.GetOutputReaderLocked();
                return true;
            });

            if (!prepared)
            {
                return WorkerAttemptResult.RetryableFailure(
                    "worker_process_missing",
                    new { request_file_path = requestFilePath });
            }

            ct.ThrowIfCancellationRequested();
            incrementBuildCount();
            markBuildStarted();

            try
            {
                ServiceValue.ExecuteWithStateLock(() => SendCompileRequestLocked(requestFilePath));
                return await ReadWorkerResponseAsync(requestFilePath, reader, ct).ConfigureAwait(false);
            }
            catch (IOException ex)
            {
                return CreateRetryableWorkerCommunicationFailure(requestFilePath, ex);
            }
            catch (ObjectDisposedException ex)
            {
                return CreateRetryableWorkerCommunicationFailure(requestFilePath, ex);
            }
            catch (OperationCanceledException)
            {
                ServiceValue.ExecuteWithStateLock(ServiceValue.ShutdownProcessLocked);
                throw;
            }
            finally
            {
                markBuildFinished();
            }
        }

        private static void SendCompileRequestLocked(string requestFilePath)
        {
            string absoluteRequestFilePath = Path.GetFullPath(requestFilePath);
            ServiceValue.SendCompileRequestLocked(absoluteRequestFilePath);
        }

        private static async Task<WorkerAttemptResult> ReadWorkerResponseAsync(
            string requestFilePath,
            StreamReader reader,
            CancellationToken ct)
        {
            int timeoutMilliseconds = ServiceValue.ResponseTimeoutMilliseconds;
            string responseHeader = await SharedRoslynCompilerWorkerProtocol.ReadProtocolLineAsync(
                reader,
                ct,
                timeoutMilliseconds).ConfigureAwait(false);
            if (string.IsNullOrEmpty(responseHeader))
            {
                // Why kill immediately: abandoned ReadLine tasks unblock only after the pipe closes.
                ServiceValue.ExecuteWithStateLock(ServiceValue.ShutdownProcessLocked);
                return WorkerAttemptResult.RetryableFailure(
                    "worker_empty_header",
                    new { request_file_path = requestFilePath });
            }

            if (!SharedRoslynCompilerWorkerProtocol.TryParseResponseHeader(responseHeader, out int exitCode))
            {
                ServiceValue.ExecuteWithStateLock(ServiceValue.ShutdownProcessLocked);
                return WorkerAttemptResult.RetryableFailure(
                    SharedRoslynCompilerWorkerProtocol.GetResponseHeaderFailureReason(responseHeader),
                    new { header = responseHeader });
            }

            List<string> outputLines = await SharedRoslynCompilerWorkerProtocol.ReadDiagnosticLinesAsync(
                reader,
                ct,
                timeoutMilliseconds).ConfigureAwait(false);
            if (outputLines == null)
            {
                ServiceValue.ExecuteWithStateLock(ServiceValue.ShutdownProcessLocked);
                return WorkerAttemptResult.RetryableFailure(
                    "worker_missing_end_marker",
                    new { request_file_path = requestFilePath });
            }

            string combinedOutput = string.Join("\n", outputLines);
            CompilerMessage[] compilerMessages = ExternalCompilerMessageParser.Parse(
                combinedOutput,
                string.Empty,
                exitCode);
            return WorkerAttemptResult.Successful(compilerMessages);
        }

        private static void ShutdownForReload()
        {
            Shutdown();
        }

        private static void ShutdownForQuit()
        {
            Shutdown();
        }

        internal static void ShutdownForTests()
        {
            Shutdown();
        }

        internal static void ShutdownForServerReset()
        {
            Shutdown();
        }

        internal static Func<ExternalCompilerPaths, string, string, string, CompilerMessage[]>
            SwapWorkerAssemblyCompilerForTests(
                Func<ExternalCompilerPaths, string, string, string, CompilerMessage[]> compiler)
        {
            return ServiceValue.SwapWorkerAssemblyCompilerForTests(compiler);
        }

        internal static Func<string> SwapWorkerAssemblyCacheRootForTests(Func<string> resolver)
        {
            return ServiceValue.SwapWorkerAssemblyCacheRootForTests(resolver);
        }

        private static void Shutdown()
        {
            ServiceValue.Shutdown(SharedRoslynCompilerWorkerHostProcess.GetWorkerDirectoryPath());
        }

        private static WorkerAttemptResult CreateRetryableWorkerCommunicationFailure(
            string requestFilePath,
            Exception ex)
        {
            return WorkerAttemptResult.RetryableFailure(
                "worker_communication_failed",
                new
                {
                    request_file_path = requestFilePath,
                    exception_type = ex.GetType().FullName,
                    exception_message = ex.Message
                });
        }

        private static object AppendAttempt(object failureContext, int attempt)
        {
            return new
            {
                attempt,
                details = failureContext
            };
        }
    }
}
