using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEditor;
using UnityEditor.Compilation;
using Debug = UnityEngine.Debug;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Provides Shared Roslyn Compiler Worker Host behavior for Unity CLI Loop.
    /// </summary>
    internal static class SharedRoslynCompilerWorkerHost
    {
        private const int SharedCompilerWorkerMaxAttempts = 2;
        private const string RoslynWorkerSourceFileName = "RoslynCompilerWorker.cs";
        private const string RoslynWorkerAssemblyFileName = "RoslynCompilerWorker.dll";
        private const string RoslynWorkerCompileResponseFileName = "RoslynCompilerWorker.rsp";

        private static readonly object SharedCompilerWorkerLock = new();
        private static Action<string> s_deleteWorkerDirectory = path => Directory.Delete(path, true);
        private static Action<Process, string> s_sendCompileRequest = SendCompileRequestCore;
        private static Func<ExternalCompilerPaths, string, string, string, CompilerMessage[]>
            s_compileWorkerAssemblyForTests;
        private static Process _sharedCompilerWorkerProcess;
        private static string _workerDirectoryPath;

        /// <summary>
        /// Carries the result data produced by Worker Attempt behavior.
        /// </summary>
        private sealed class WorkerAttemptResult
        {
            public CompilerMessage[] Messages { get; }

            public bool ShouldRetry { get; }

            public string FailureReason { get; }

            public object FailureContext { get; }

            private WorkerAttemptResult(
                CompilerMessage[] messages,
                bool shouldRetry,
                string failureReason,
                object failureContext)
            {
                Messages = messages;
                ShouldRetry = shouldRetry;
                FailureReason = failureReason;
                FailureContext = failureContext;
            }

            public bool Succeeded => Messages != null;

            public static WorkerAttemptResult Successful(CompilerMessage[] messages)
            {
                return new WorkerAttemptResult(messages, false, null, null);
            }

            public static WorkerAttemptResult RetryableFailure(string failureReason, object failureContext)
            {
                return new WorkerAttemptResult(null, true, failureReason, failureContext);
            }
        }

        /// <summary>
        /// Carries the result data produced by Worker Startup behavior.
        /// </summary>
        private sealed class WorkerStartupResult
        {
            public bool IsReady { get; }

            public string FailureReason { get; }

            public object FailureContext { get; }

            private WorkerStartupResult(bool isReady, string failureReason, object failureContext)
            {
                IsReady = isReady;
                FailureReason = failureReason;
                FailureContext = failureContext;
            }

            public static WorkerStartupResult Ready()
            {
                return new WorkerStartupResult(true, null, null);
            }

            public static WorkerStartupResult Failure(string failureReason, object failureContext)
            {
                return new WorkerStartupResult(false, failureReason, failureContext);
            }
        }

        /// <summary>
        /// Provides Worker Paths behavior for Unity CLI Loop.
        /// </summary>
        private sealed class WorkerPaths
        {
            public string DirectoryPath { get; }

            public string SourcePath { get; }

            public string AssemblyPath { get; }

            public string CompileResponseFilePath { get; }

            public WorkerPaths(
                string directoryPath,
                string sourcePath,
                string assemblyPath,
                string compileResponseFilePath)
            {
                DirectoryPath = directoryPath;
                SourcePath = sourcePath;
                AssemblyPath = assemblyPath;
                CompileResponseFilePath = compileResponseFilePath;
            }
        }

        internal static void RegisterLifecycleForEditorStartup()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= ShutdownForReload;
            AssemblyReloadEvents.beforeAssemblyReload += ShutdownForReload;
            EditorApplication.quitting -= ShutdownForQuit;
            EditorApplication.quitting += ShutdownForQuit;
        }

        public static CompilerMessage[] TryCompile(
            string requestFilePath,
            ExternalCompilerPaths externalCompilerPaths,
            CancellationToken ct,
            Action markBuildStarted,
            Action markBuildFinished,
            Action incrementBuildCount)
        {
            lock (SharedCompilerWorkerLock)
            {
                return TryCompileWithRetries(
                    requestFilePath,
                    externalCompilerPaths,
                    ct,
                    markBuildStarted,
                    markBuildFinished,
                    incrementBuildCount);
            }
        }

        private static CompilerMessage[] TryCompileWithRetries(
            string requestFilePath,
            ExternalCompilerPaths externalCompilerPaths,
            CancellationToken ct,
            Action markBuildStarted,
            Action markBuildFinished,
            Action incrementBuildCount)
        {
            for (int attempt = 1; attempt <= SharedCompilerWorkerMaxAttempts; attempt++)
            {
                WorkerAttemptResult attemptResult = TryCompileOnce(
                    requestFilePath,
                    externalCompilerPaths,
                    ct,
                    markBuildStarted,
                    markBuildFinished,
                    incrementBuildCount);

                if (attemptResult.Succeeded)
                {
                    return attemptResult.Messages;
                }

                ShutdownWorkerProcessLocked();

                if (attemptResult.ShouldRetry && attempt < SharedCompilerWorkerMaxAttempts)
                {
                    continue;
                }

                DynamicCompilationHealthMonitor.ReportSharedWorkerFailure(
                    attemptResult.FailureReason,
                    AppendAttempt(attemptResult.FailureContext, attempt));
                return null;
            }

            return null;
        }

        private static WorkerAttemptResult TryCompileOnce(
            string requestFilePath,
            ExternalCompilerPaths externalCompilerPaths,
            CancellationToken ct,
            Action markBuildStarted,
            Action markBuildFinished,
            Action incrementBuildCount)
        {
            WorkerStartupResult startupResult = EnsureWorkerReady(externalCompilerPaths);
            if (!startupResult.IsReady)
            {
                return WorkerAttemptResult.RetryableFailure(
                    startupResult.FailureReason,
                    startupResult.FailureContext);
            }

            return InvokeWorkerOnce(
                requestFilePath,
                ct,
                markBuildStarted,
                markBuildFinished,
                incrementBuildCount);
        }

        private static WorkerStartupResult EnsureWorkerReady(ExternalCompilerPaths externalCompilerPaths)
        {
            if (HasLiveWorkerProcess())
            {
                return WorkerStartupResult.Ready();
            }

            WorkerPaths workerPaths = CreateWorkerPaths();
            SynchronizeWorkerSource(workerPaths);

            WorkerStartupResult workerAssemblyResult = EnsureWorkerAssemblyBuilt(
                externalCompilerPaths,
                workerPaths);
            if (!workerAssemblyResult.IsReady)
            {
                return workerAssemblyResult;
            }

            return StartWorkerProcess(externalCompilerPaths, workerPaths);
        }

        private static WorkerStartupResult EnsureWorkerAssemblyBuilt(
            ExternalCompilerPaths externalCompilerPaths,
            WorkerPaths workerPaths)
        {
            if (File.Exists(workerPaths.AssemblyPath))
            {
                return WorkerStartupResult.Ready();
            }

            SharedRoslynCompilerWorkerAssemblyBuilder.WorkerAssemblyBuildResult buildResult = CompileWorkerAssembly(
                externalCompilerPaths,
                workerPaths.SourcePath,
                workerPaths.AssemblyPath,
                workerPaths.CompileResponseFilePath);
            if (!buildResult.StartedSuccessfully)
            {
                return WorkerStartupResult.Failure(
                    buildResult.FailureReason,
                    buildResult.FailureContext);
            }

            if (!HasErrors(buildResult.Messages))
            {
                return WorkerStartupResult.Ready();
            }

            SharedRoslynCompilerWorkerAssemblyBuilder.DeleteWorkerAssemblyIfPresent(workerPaths.AssemblyPath);
            return WorkerStartupResult.Failure(
                "worker_build_failed",
                new
                {
                    first_error = FindFirstErrorMessage(buildResult.Messages),
                    worker_source_path = workerPaths.SourcePath
                });
        }

        private static WorkerStartupResult StartWorkerProcess(
            ExternalCompilerPaths externalCompilerPaths,
            WorkerPaths workerPaths)
        {
            ProcessStartInfo startInfo = CreateWorkerStartInfo(externalCompilerPaths, workerPaths);
            _sharedCompilerWorkerProcess = ProcessStartHelper.TryStart(startInfo);
            if (_sharedCompilerWorkerProcess == null)
            {
                return WorkerStartupResult.Failure(
                    "worker_start_failed",
                    new
                    {
                        dotnet_host_path = externalCompilerPaths.DotnetHostPath,
                        worker_assembly_path = workerPaths.AssemblyPath
                    });
            }

            return WorkerStartupResult.Ready();
        }

        private static WorkerAttemptResult InvokeWorkerOnce(
            string requestFilePath,
            CancellationToken ct,
            Action markBuildStarted,
            Action markBuildFinished,
            Action incrementBuildCount)
        {
            if (!HasLiveWorkerProcess())
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
                SendCompileRequest(requestFilePath);
                return ReadWorkerResponse(requestFilePath, ct);
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
                ShutdownWorkerProcessLocked();
                throw;
            }
            finally
            {
                markBuildFinished();
            }
        }

        private static void SendCompileRequest(string requestFilePath)
        {
            string absoluteRequestFilePath = Path.GetFullPath(requestFilePath);
            s_sendCompileRequest(_sharedCompilerWorkerProcess, absoluteRequestFilePath);
        }

        private static void SendCompileRequestCore(Process workerProcess, string requestFilePath)
        {
            string requestCommand = SharedRoslynCompilerWorkerProtocol.CreateCompileRequestCommand(requestFilePath);
            workerProcess.StandardInput.WriteLine(requestCommand);
            workerProcess.StandardInput.Flush();
        }

        private static WorkerAttemptResult ReadWorkerResponse(
            string requestFilePath,
            CancellationToken ct)
        {
            StreamReader reader = _sharedCompilerWorkerProcess.StandardOutput;
            string responseHeader = SharedRoslynCompilerWorkerProtocol.ReadProtocolLine(
                reader,
                ct);
            if (string.IsNullOrEmpty(responseHeader))
            {
                return WorkerAttemptResult.RetryableFailure(
                    "worker_empty_header",
                    new { request_file_path = requestFilePath });
            }

            if (!SharedRoslynCompilerWorkerProtocol.TryParseResponseHeader(responseHeader, out int exitCode))
            {
                return WorkerAttemptResult.RetryableFailure(
                    SharedRoslynCompilerWorkerProtocol.GetResponseHeaderFailureReason(responseHeader),
                    new { header = responseHeader });
            }

            List<string> outputLines = SharedRoslynCompilerWorkerProtocol.ReadDiagnosticLines(reader, ct);
            if (outputLines == null)
            {
                return WorkerAttemptResult.RetryableFailure(
                    "worker_missing_end_marker",
                    new { request_file_path = requestFilePath });
            }

            string combinedOutput = string.Join("\n", outputLines);
            CompilerMessage[] compilerMessages = ExternalCompilerMessageParser.Parse(combinedOutput, string.Empty, exitCode);
            return WorkerAttemptResult.Successful(compilerMessages);
        }

        private static WorkerPaths CreateWorkerPaths()
        {
            string workerDirectoryPath = GetWorkerDirectoryPath();
            Directory.CreateDirectory(workerDirectoryPath);
            _workerDirectoryPath = workerDirectoryPath;
            return new WorkerPaths(
                workerDirectoryPath,
                Path.Combine(workerDirectoryPath, RoslynWorkerSourceFileName),
                Path.Combine(workerDirectoryPath, RoslynWorkerAssemblyFileName),
                Path.Combine(workerDirectoryPath, RoslynWorkerCompileResponseFileName));
        }

        private static string GetWorkerDirectoryPath()
        {
            return Path.Combine(
                Path.GetTempPath(),
                "UnityCliLoopCompilation",
                $"RoslynWorker-{Process.GetCurrentProcess().Id}");
        }

        private static void SynchronizeWorkerSource(WorkerPaths workerPaths)
        {
            string workerSource = SharedRoslynCompilerWorkerProtocol.CreateProgramSource();
            if (File.Exists(workerPaths.SourcePath) && File.ReadAllText(workerPaths.SourcePath) == workerSource)
            {
                return;
            }

            File.WriteAllText(workerPaths.SourcePath, workerSource);
            if (File.Exists(workerPaths.AssemblyPath))
            {
                File.Delete(workerPaths.AssemblyPath);
            }
        }

        private static ProcessStartInfo CreateWorkerStartInfo(
            ExternalCompilerPaths externalCompilerPaths,
            WorkerPaths workerPaths)
        {
            ProcessStartInfo startInfo = new()            {
                FileName = externalCompilerPaths.DotnetHostPath,
                Arguments = "exec"
                    + " --runtimeconfig " + SharedRoslynCompilerWorkerAssemblyBuilder.QuoteCommandLineArgument(externalCompilerPaths.CompilerRuntimeConfigPath)
                    + " --depsfile " + SharedRoslynCompilerWorkerAssemblyBuilder.QuoteCommandLineArgument(externalCompilerPaths.CompilerDepsFilePath)
                    + " " + SharedRoslynCompilerWorkerAssemblyBuilder.QuoteCommandLineArgument(workerPaths.AssemblyPath),
                WorkingDirectory = workerPaths.DirectoryPath,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = false,
                CreateNoWindow = true
            };

            SharedRoslynCompilerWorkerAssemblyBuilder.ConfigureWorkerDotnetRuntimeEnvironment(startInfo);
            return startInfo;
        }

        private static SharedRoslynCompilerWorkerAssemblyBuilder.WorkerAssemblyBuildResult CompileWorkerAssembly(
            ExternalCompilerPaths externalCompilerPaths,
            string workerSourcePath,
            string workerAssemblyPath,
            string workerCompileResponseFilePath)
        {
            if (s_compileWorkerAssemblyForTests != null)
            {
                return SharedRoslynCompilerWorkerAssemblyBuilder.WorkerAssemblyBuildResult.Started(
                    s_compileWorkerAssemblyForTests(
                        externalCompilerPaths,
                        workerSourcePath,
                        workerAssemblyPath,
                        workerCompileResponseFilePath));
            }

            return SharedRoslynCompilerWorkerAssemblyBuilder.CompileWorkerAssembly(
                externalCompilerPaths,
                workerSourcePath,
                workerAssemblyPath,
                workerCompileResponseFilePath);
        }

        private static bool HasLiveWorkerProcess()
        {
            return _sharedCompilerWorkerProcess != null && !_sharedCompilerWorkerProcess.HasExited;
        }

        private static bool HasErrors(IReadOnlyCollection<CompilerMessage> messages)
        {
            foreach (CompilerMessage message in messages)
            {
                if (message.type == CompilerMessageType.Error)
                {
                    return true;
                }
            }

            return false;
        }

        private static string FindFirstErrorMessage(IReadOnlyCollection<CompilerMessage> messages)
        {
            foreach (CompilerMessage message in messages)
            {
                if (message.type == CompilerMessageType.Error)
                {
                    return message.message;
                }
            }

            return string.Empty;
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

        internal static Action<string> SwapWorkerDirectoryDeleterForTests(Action<string> deleter)
        {
            Debug.Assert(deleter != null, "deleter must not be null");

            Action<string> previous = s_deleteWorkerDirectory;
            s_deleteWorkerDirectory = deleter;
            return previous;
        }

        internal static Action<Process, string> SwapCompileRequestSenderForTests(Action<Process, string> sender)
        {
            Debug.Assert(sender != null, "sender must not be null");

            Action<Process, string> previous = s_sendCompileRequest;
            s_sendCompileRequest = sender;
            return previous;
        }

        internal static Func<ExternalCompilerPaths, string, string, string, CompilerMessage[]>
            SwapWorkerAssemblyCompilerForTests(
                Func<ExternalCompilerPaths, string, string, string, CompilerMessage[]> compiler)
        {
            Func<ExternalCompilerPaths, string, string, string, CompilerMessage[]> previous =
                s_compileWorkerAssemblyForTests;
            s_compileWorkerAssemblyForTests = compiler;
            return previous;
        }

        private static void Shutdown()
        {
            lock (SharedCompilerWorkerLock)
            {
                ShutdownWorkerProcessLocked();
                CleanupWorkerDirectoryLocked();
            }
        }

        private static void ShutdownWorkerProcessLocked()
        {
            Process workerProcess = _sharedCompilerWorkerProcess;
            _sharedCompilerWorkerProcess = null;
            if (workerProcess == null)
            {
                return;
            }

            try
            {
                if (!workerProcess.HasExited)
                {
                    workerProcess.StandardInput.WriteLine(
                        SharedRoslynCompilerWorkerProtocol.SharedCompilerWorkerQuitCommand);
                    workerProcess.StandardInput.Flush();
                    workerProcess.WaitForExit(500);
                }

                if (!workerProcess.HasExited)
                {
                    workerProcess.Kill();
                    workerProcess.WaitForExit(500);
                }
            }
            catch (IOException ex)
            {
                LogWorkerShutdownFailure(ex);
            }
            catch (ObjectDisposedException ex)
            {
                LogWorkerShutdownFailure(ex);
            }
            catch (InvalidOperationException ex)
            {
                LogWorkerShutdownFailure(ex);
            }
            finally
            {
                workerProcess.Dispose();
            }
        }

        private static void CleanupWorkerDirectoryLocked()
        {
            string workerDirectoryPath = _workerDirectoryPath ?? GetWorkerDirectoryPath();
            if (!Directory.Exists(workerDirectoryPath))
            {
                _workerDirectoryPath = null;
                return;
            }

            TryDeleteWorkerDirectory(workerDirectoryPath);
            _workerDirectoryPath = null;
        }

        private static void TryDeleteWorkerDirectory(string workerDirectoryPath)
        {
            try
            {
                s_deleteWorkerDirectory(workerDirectoryPath);
            }
            catch (IOException ex)
            {
                LogWorkerDirectoryCleanupFailure(workerDirectoryPath, ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                LogWorkerDirectoryCleanupFailure(workerDirectoryPath, ex);
            }
        }

        private static void LogWorkerDirectoryCleanupFailure(string workerDirectoryPath, Exception ex)
        {
            VibeLogger.LogWarning(
                "dynamic_code_shared_worker_cleanup_failed",
                "execute-dynamic-code shared Roslyn worker directory cleanup failed during shutdown",
                new
                {
                    worker_directory_path = workerDirectoryPath,
                    exception_type = ex.GetType().FullName,
                    exception_message = ex.Message
                },
                humanNote: "Shared Roslyn worker cleanup could not remove its temporary directory during shutdown.",
                aiTodo: "Investigate file locks or permission issues if temporary worker directories continue to accumulate.");
            Debug.LogWarning($"[{UnityCliLoopConstants.PROJECT_NAME}] Failed to delete shared Roslyn worker directory '{workerDirectoryPath}': {ex.Message}");
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

        private static void LogWorkerShutdownFailure(Exception ex)
        {
            VibeLogger.LogWarning(
                "dynamic_code_shared_worker_shutdown_failed",
                "execute-dynamic-code shared Roslyn worker shutdown observed a communication failure",
                new
                {
                    exception_type = ex.GetType().FullName,
                    exception_message = ex.Message
                },
                humanNote: "Shared Roslyn worker shutdown saw a broken communication channel while cleaning up a crashed worker.",
                aiTodo: "Investigate repeated worker shutdown communication failures if shared compilation stops recovering cleanly.");
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
