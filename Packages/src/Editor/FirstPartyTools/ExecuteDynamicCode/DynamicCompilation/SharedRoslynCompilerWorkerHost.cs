using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
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
        private const string RoslynWorkerSourceFileName = "RoslynCompilerWorker.cs";
        private const string RoslynWorkerAssemblyFileName = "RoslynCompilerWorker.dll";
        private const string RoslynWorkerCompileResponseFileName = "RoslynCompilerWorker.rsp";

        private static readonly SharedRoslynCompilerWorkerSession ServiceValue = new();

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
            return ServiceValue.ExecuteLocked(
                () => TryCompileWithRetries(
                    requestFilePath,
                    externalCompilerPaths,
                    ct,
                    markBuildStarted,
                    markBuildFinished,
                    incrementBuildCount));
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

                ServiceValue.ShutdownProcessLocked();

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
            if (ServiceValue.HasLiveProcessLocked())
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

            SharedRoslynCompilerWorkerAssemblyBuilder.WorkerAssemblyBuildResult buildResult =
                ServiceValue.CompileWorkerAssemblyLocked(
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
            if (!ServiceValue.StartProcessLocked(startInfo))
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
            if (!ServiceValue.HasLiveProcessLocked())
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
                ServiceValue.ShutdownProcessLocked();
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
            ServiceValue.SendCompileRequestLocked(absoluteRequestFilePath);
        }

        private static WorkerAttemptResult ReadWorkerResponse(
            string requestFilePath,
            CancellationToken ct)
        {
            StreamReader reader = ServiceValue.GetOutputReaderLocked();
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
            ServiceValue.RecordWorkerDirectoryLocked(workerDirectoryPath);
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

        internal static Action<Process, string> SwapCompileRequestSenderForTests(Action<Process, string> sender)
        {
            return ServiceValue.SwapCompileRequestSenderForTests(sender);
        }

        internal static Func<ExternalCompilerPaths, string, string, string, CompilerMessage[]>
            SwapWorkerAssemblyCompilerForTests(
                Func<ExternalCompilerPaths, string, string, string, CompilerMessage[]> compiler)
        {
            return ServiceValue.SwapWorkerAssemblyCompilerForTests(compiler);
        }

        private static void Shutdown()
        {
            ServiceValue.Shutdown(GetWorkerDirectoryPath());
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
