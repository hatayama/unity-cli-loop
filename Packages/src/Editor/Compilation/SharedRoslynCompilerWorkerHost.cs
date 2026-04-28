using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Compilation;
using Debug = UnityEngine.Debug;

namespace io.github.hatayama.uLoopMCP
{
    internal static class SharedRoslynCompilerWorkerHost
    {
        private const int SharedCompilerWorkerMaxAttempts = 2;
        private const string SharedCompilerWorkerResultPrefix = "__ULOOP_RESULT__";
        private const string SharedCompilerWorkerEndMarker = "__ULOOP_END__";
        private const string SharedCompilerWorkerQuitCommand = "__QUIT__";
        internal const string CompileRequestPathPrefix = "path-base64:";
        private const string RoslynWorkerSourceFileName = "RoslynCompilerWorker.cs";
        private const string RoslynWorkerAssemblyFileName = "RoslynCompilerWorker.dll";
        private const string RoslynWorkerCompileResponseFileName = "RoslynCompilerWorker.rsp";
        private const int SharedCompilerWorkerResponseTimeoutMilliseconds = 30000;
        private const int WorkerAssemblyBuildTimeoutMilliseconds = 30000;
        internal const string DotnetMultilevelLookupEnvironmentVariableName = "DOTNET_MULTILEVEL_LOOKUP";
        internal const string DotnetMultilevelLookupDisabledValue = "0";

        private static readonly object SharedCompilerWorkerLock = new();
        private static Action<string> s_deleteWorkerDirectory = path => Directory.Delete(path, true);
        private static Action<Process, string> s_sendCompileRequest = SendCompileRequestCore;
        private static Func<ExternalCompilerPaths, string, string, string, CompilerMessage[]>
            s_compileWorkerAssemblyForTests;
        private static Process _sharedCompilerWorkerProcess;
        private static string _workerDirectoryPath;

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

        private sealed class WorkerAssemblyBuildResult
        {
            public bool StartedSuccessfully { get; }

            public CompilerMessage[] Messages { get; }

            public string FailureReason { get; }

            public object FailureContext { get; }

            private WorkerAssemblyBuildResult(
                bool startedSuccessfully,
                CompilerMessage[] messages,
                string failureReason,
                object failureContext)
            {
                StartedSuccessfully = startedSuccessfully;
                Messages = messages;
                FailureReason = failureReason;
                FailureContext = failureContext;
            }

            public static WorkerAssemblyBuildResult Started(CompilerMessage[] messages)
            {
                return new WorkerAssemblyBuildResult(true, messages, null, null);
            }

            public static WorkerAssemblyBuildResult StartFailure(string failureReason, object failureContext)
            {
                return new WorkerAssemblyBuildResult(false, null, failureReason, failureContext);
            }
        }

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

        [InitializeOnLoadMethod]
        private static void RegisterLifecycle()
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

            WorkerAssemblyBuildResult buildResult = CompileWorkerAssembly(
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

            DeleteWorkerAssemblyIfPresent(workerPaths.AssemblyPath);
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
            string requestCommand = CreateCompileRequestCommand(requestFilePath);
            workerProcess.StandardInput.WriteLine(requestCommand);
            workerProcess.StandardInput.Flush();
        }

        internal static string CreateCompileRequestCommandForTests(string requestFilePath)
        {
            return CreateCompileRequestCommand(requestFilePath);
        }

        internal static string CreateProgramSourceForTests()
        {
            return CreateProgramSource();
        }

        private static string CreateCompileRequestCommand(string requestFilePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(requestFilePath), "requestFilePath must not be empty");

            string fullRequestFilePath = Path.GetFullPath(requestFilePath);
            byte[] requestPathBytes = Encoding.UTF8.GetBytes(fullRequestFilePath);
            return CompileRequestPathPrefix + Convert.ToBase64String(requestPathBytes);
        }

        private static WorkerAttemptResult ReadWorkerResponse(
            string requestFilePath,
            CancellationToken ct)
        {
            string responseHeader = ReadProtocolLine(
                _sharedCompilerWorkerProcess.StandardOutput,
                ct);
            if (string.IsNullOrEmpty(responseHeader))
            {
                return WorkerAttemptResult.RetryableFailure(
                    "worker_empty_header",
                    new { request_file_path = requestFilePath });
            }

            if (!TryParseResponseHeader(responseHeader, out int exitCode))
            {
                return WorkerAttemptResult.RetryableFailure(
                    GetResponseHeaderFailureReason(responseHeader),
                    new { header = responseHeader });
            }

            List<string> outputLines = ReadDiagnosticLines(ct);
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

        private static bool TryParseResponseHeader(string responseHeader, out int exitCode)
        {
            exitCode = 0;

            if (!responseHeader.StartsWith(SharedCompilerWorkerResultPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            string statusText = responseHeader.Substring(SharedCompilerWorkerResultPrefix.Length).Trim();
            return int.TryParse(statusText, out exitCode);
        }

        private static string GetResponseHeaderFailureReason(string responseHeader)
        {
            if (!responseHeader.StartsWith(SharedCompilerWorkerResultPrefix, StringComparison.Ordinal))
            {
                return "worker_invalid_header";
            }

            return "worker_invalid_exit_code";
        }

        private static List<string> ReadDiagnosticLines(CancellationToken ct)
        {
            List<string> outputLines = new List<string>();
            while (true)
            {
                string outputLine = ReadProtocolLine(_sharedCompilerWorkerProcess.StandardOutput, ct);
                if (outputLine == null)
                {
                    return null;
                }

                if (outputLine == SharedCompilerWorkerEndMarker)
                {
                    return outputLines;
                }

                outputLines.Add(outputLine);
            }
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
                "uLoopMCPCompilation",
                $"RoslynWorker-{Process.GetCurrentProcess().Id}");
        }

        private static void SynchronizeWorkerSource(WorkerPaths workerPaths)
        {
            string workerSource = CreateProgramSource();
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
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = externalCompilerPaths.DotnetHostPath,
                Arguments = "exec"
                    + " --runtimeconfig " + QuoteCommandLineArgument(externalCompilerPaths.CompilerRuntimeConfigPath)
                    + " --depsfile " + QuoteCommandLineArgument(externalCompilerPaths.CompilerDepsFilePath)
                    + " " + QuoteCommandLineArgument(workerPaths.AssemblyPath),
                WorkingDirectory = workerPaths.DirectoryPath,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = false,
                CreateNoWindow = true
            };

            ConfigureWorkerDotnetRuntimeEnvironment(startInfo);
            return startInfo;
        }

        internal static void ConfigureWorkerDotnetRuntimeEnvironment(ProcessStartInfo startInfo)
        {
            Debug.Assert(startInfo != null, "startInfo must not be null");

            // Why: global probing can select a system .NET 6 runtime while Unity 6000.4 worker
            // references come from the bundled .NET 8 runtime, which breaks assembly binding.
            startInfo.EnvironmentVariables[DotnetMultilevelLookupEnvironmentVariableName] =
                DotnetMultilevelLookupDisabledValue;
        }

        private static WorkerAssemblyBuildResult CompileWorkerAssembly(
            ExternalCompilerPaths externalCompilerPaths,
            string workerSourcePath,
            string workerAssemblyPath,
            string workerCompileResponseFilePath)
        {
            if (s_compileWorkerAssemblyForTests != null)
            {
                return WorkerAssemblyBuildResult.Started(
                    s_compileWorkerAssemblyForTests(
                        externalCompilerPaths,
                        workerSourcePath,
                        workerAssemblyPath,
                        workerCompileResponseFilePath));
            }

            WriteWorkerCompilerResponseFile(
                workerCompileResponseFilePath,
                workerSourcePath,
                workerAssemblyPath,
                BuildWorkerReferenceSet(externalCompilerPaths));

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = externalCompilerPaths.DotnetHostPath,
                Arguments = $"{QuoteCommandLineArgument(externalCompilerPaths.CompilerDllPath)} @{QuoteCommandLineArgument(workerCompileResponseFilePath)}",
                WorkingDirectory = Path.GetDirectoryName(workerSourcePath),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            ConfigureWorkerDotnetRuntimeEnvironment(startInfo);

            using Process process = ProcessStartHelper.TryStart(startInfo);
            if (process == null)
            {
                return WorkerAssemblyBuildResult.StartFailure(
                    "worker_compiler_start_failed",
                    new
                    {
                        dotnet_host_path = externalCompilerPaths.DotnetHostPath,
                        compiler_dll_path = externalCompilerPaths.CompilerDllPath
                    });
            }

            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(WorkerAssemblyBuildTimeoutMilliseconds))
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(500);
                }

                Task.WaitAll(stdoutTask, stderrTask);
                return WorkerAssemblyBuildResult.StartFailure(
                    "worker_compiler_timeout",
                    new
                    {
                        timeout_ms = WorkerAssemblyBuildTimeoutMilliseconds,
                        dotnet_host_path = externalCompilerPaths.DotnetHostPath,
                        compiler_dll_path = externalCompilerPaths.CompilerDllPath
                    });
            }

            Task.WaitAll(stdoutTask, stderrTask);
            CompilerMessage[] compilerMessages = ExternalCompilerMessageParser.Parse(
                stdoutTask.GetAwaiter().GetResult(),
                stderrTask.GetAwaiter().GetResult(),
                process.ExitCode);
            return WorkerAssemblyBuildResult.Started(compilerMessages);
        }

        private static List<string> BuildWorkerReferenceSet(ExternalCompilerPaths externalCompilerPaths)
        {
            string sharedRuntimeDirectoryPath = externalCompilerPaths.NetCoreRuntimeSharedDirectoryPath;
            List<string> references = new List<string>
            {
                Path.Combine(sharedRuntimeDirectoryPath, "System.Private.CoreLib.dll"),
                Path.Combine(sharedRuntimeDirectoryPath, "System.Runtime.dll"),
                Path.Combine(sharedRuntimeDirectoryPath, "System.Console.dll"),
                Path.Combine(sharedRuntimeDirectoryPath, "System.Collections.dll"),
                Path.Combine(sharedRuntimeDirectoryPath, "System.IO.dll"),
                Path.Combine(sharedRuntimeDirectoryPath, "System.Threading.dll"),
                Path.Combine(sharedRuntimeDirectoryPath, "System.Threading.Tasks.dll"),
                Path.Combine(sharedRuntimeDirectoryPath, "System.Text.Encoding.Extensions.dll"),
                Path.Combine(sharedRuntimeDirectoryPath, "System.Runtime.Extensions.dll"),
                Path.Combine(sharedRuntimeDirectoryPath, "netstandard.dll"),
                externalCompilerPaths.CodeAnalysisDllPath,
                externalCompilerPaths.CodeAnalysisCSharpDllPath
            };

            AddIfExists(references, Path.Combine(sharedRuntimeDirectoryPath, "System.Collections.Immutable.dll"));
            AddIfExists(references, Path.Combine(sharedRuntimeDirectoryPath, "System.Reflection.Metadata.dll"));
            AddIfExists(references, Path.Combine(sharedRuntimeDirectoryPath, "System.Runtime.CompilerServices.Unsafe.dll"));
            AddIfExists(references, Path.Combine(sharedRuntimeDirectoryPath, "System.Memory.dll"));
            AddIfExists(references, Path.Combine(sharedRuntimeDirectoryPath, "System.Buffers.dll"));
            AddIfExists(references, Path.Combine(sharedRuntimeDirectoryPath, "System.Threading.Tasks.Extensions.dll"));

            return references;
        }

        private static void WriteWorkerCompilerResponseFile(
            string responseFilePath,
            string sourcePath,
            string dllPath,
            IReadOnlyCollection<string> references)
        {
            List<string> lines = new List<string>
            {
                "-nologo",
                "-nostdlib+",
                "-target:exe",
                "-optimize+",
                "-debug-",
                QuoteResponseFileArgument("-out:", dllPath)
            };

            foreach (string reference in references)
            {
                lines.Add(QuoteResponseFileArgument("-r:", reference));
            }

            lines.Add(QuoteResponseFilePath(sourcePath));
            File.WriteAllLines(responseFilePath, lines);
        }

        private static string ReadProtocolLine(StreamReader reader, CancellationToken ct)
        {
            Debug.Assert(reader != null, "reader must not be null");

            Task<string> readTask = Task.Run(() => reader.ReadLine());
            Task timeoutTask = Task.Delay(SharedCompilerWorkerResponseTimeoutMilliseconds, ct);
            Task completedTask = Task.WhenAny(readTask, timeoutTask).GetAwaiter().GetResult();
            if (!ReferenceEquals(completedTask, readTask))
            {
                ct.ThrowIfCancellationRequested();
                return null;
            }

            return readTask.GetAwaiter().GetResult();
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

        private static void DeleteWorkerAssemblyIfPresent(string assemblyPath)
        {
            if (File.Exists(assemblyPath))
            {
                File.Delete(assemblyPath);
            }
        }

        private static void ShutdownForReload()
        {
            Shutdown();
        }

        private static void ShutdownForQuit()
        {
            Shutdown();
        }

        internal static void ShutdownForServerReset()
        {
            Shutdown();
        }

        internal static void ShutdownForTests()
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
                    workerProcess.StandardInput.WriteLine(SharedCompilerWorkerQuitCommand);
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
            Debug.LogWarning($"[{McpConstants.PROJECT_NAME}] Failed to delete shared Roslyn worker directory '{workerDirectoryPath}': {ex.Message}");
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

        private static string QuoteResponseFileArgument(string prefix, string value)
        {
            return $"{prefix}{QuoteResponseFilePath(value)}";
        }

        private static string QuoteResponseFilePath(string path)
        {
            return $"\"{path}\"";
        }

        private static string QuoteCommandLineArgument(string value)
        {
            return $"\"{value}\"";
        }

        private static void AddIfExists(
            List<string> destination,
            string referencePath)
        {
            if (string.IsNullOrEmpty(referencePath) || !File.Exists(referencePath))
            {
                return;
            }

            destination.Add(referencePath);
        }

        private static string CreateProgramSource()
        {
            return "using System;\n"
                + "using System.Collections.Generic;\n"
                + "using System.IO;\n"
                + "using System.Text;\n"
                + "using Microsoft.CodeAnalysis;\n"
                + "using Microsoft.CodeAnalysis.CSharp;\n"
                + "using Microsoft.CodeAnalysis.Emit;\n"
                + "using Microsoft.CodeAnalysis.Text;\n"
                + "\n"
                + "public static class Program\n"
                + "{\n"
                + "    private const string ResultPrefix = \"" + SharedCompilerWorkerResultPrefix + "\";\n"
                + "    private const string EndMarker = \"" + SharedCompilerWorkerEndMarker + "\";\n"
                + "    private const string QuitCommand = \"" + SharedCompilerWorkerQuitCommand + "\";\n"
                + "    private const string RequestPathPrefix = \"" + CompileRequestPathPrefix + "\";\n"
                + "    private const string UnsafePrefix = \"unsafe:\";\n"
                + "    private const string DefinePrefix = \"define:\";\n"
                + "    private const string ReferencePrefix = \"ref:\";\n"
                + "    private static readonly object SyncRoot = new object();\n"
                + "    private static readonly Dictionary<string, CachedReference> Cache = new Dictionary<string, CachedReference>(StringComparer.OrdinalIgnoreCase);\n"
                + "\n"
                + "    public static int Main()\n"
                + "    {\n"
                + "        string requestPath;\n"
                + "        while ((requestPath = Console.ReadLine()) != null)\n"
                + "        {\n"
                + "            if (requestPath == QuitCommand)\n"
                + "            {\n"
                + "                return 0;\n"
                + "            }\n"
                + "\n"
                + "            CompileResponse response = Compile(DecodeRequestPath(requestPath));\n"
                + "            Console.WriteLine(ResultPrefix + \" \" + response.ExitCode);\n"
                + "            foreach (string diagnosticLine in response.DiagnosticLines)\n"
                + "            {\n"
                + "                Console.WriteLine(diagnosticLine);\n"
                + "            }\n"
                + "            Console.WriteLine(EndMarker);\n"
                + "            Console.Out.Flush();\n"
                + "        }\n"
                + "\n"
                + "        return 0;\n"
                + "    }\n"
                + "\n"
                + "    private static string DecodeRequestPath(string requestPath)\n"
                + "    {\n"
                + "        int encodedPathIndex = FindRequestPathPrefixIndex(requestPath);\n"
                + "        if (encodedPathIndex >= 0)\n"
                + "        {\n"
                + "            string encodedPath = requestPath.Substring(encodedPathIndex + RequestPathPrefix.Length);\n"
                + "            if (!IsBase64Payload(encodedPath))\n"
                + "            {\n"
                + "                return RecoverRawRequestPath(requestPath);\n"
                + "            }\n"
                + "\n"
                + "            byte[] requestPathBytes = Convert.FromBase64String(encodedPath);\n"
                + "            return Encoding.UTF8.GetString(requestPathBytes);\n"
                + "        }\n"
                + "\n"
                + "        return RecoverRawRequestPath(requestPath);\n"
                + "    }\n"
                + "\n"
                + "    private static bool IsBase64Payload(string value)\n"
                + "    {\n"
                + "        if (string.IsNullOrEmpty(value) || value.Length % 4 != 0 || !HasValidBase64Padding(value))\n"
                + "        {\n"
                + "            return false;\n"
                + "        }\n"
                + "\n"
                + "        for (int i = 0; i < value.Length; i++)\n"
                + "        {\n"
                + "            char character = value[i];\n"
                + "            bool isBase64Character = character >= 'A' && character <= 'Z'\n"
                + "                || character >= 'a' && character <= 'z'\n"
                + "                || character >= '0' && character <= '9'\n"
                + "                || character == '+'\n"
                + "                || character == '/'\n"
                + "                || character == '=';\n"
                + "            if (!isBase64Character)\n"
                + "            {\n"
                + "                return false;\n"
                + "            }\n"
                + "        }\n"
                + "\n"
                + "        return true;\n"
                + "    }\n"
                + "\n"
                + "    private static bool HasValidBase64Padding(string value)\n"
                + "    {\n"
                + "        int firstPaddingIndex = value.IndexOf('=');\n"
                + "        if (firstPaddingIndex < 0)\n"
                + "        {\n"
                + "            return true;\n"
                + "        }\n"
                + "\n"
                + "        int paddingLength = value.Length - firstPaddingIndex;\n"
                + "        if (paddingLength > 2)\n"
                + "        {\n"
                + "            return false;\n"
                + "        }\n"
                + "\n"
                + "        for (int i = firstPaddingIndex; i < value.Length; i++)\n"
                + "        {\n"
                + "            if (value[i] != '=')\n"
                + "            {\n"
                + "                return false;\n"
                + "            }\n"
                + "        }\n"
                + "\n"
                + "        return true;\n"
                + "    }\n"
                + "\n"
                + "    private static int FindRequestPathPrefixIndex(string requestPath)\n"
                + "    {\n"
                + "        int encodedPathIndex = requestPath.IndexOf(RequestPathPrefix, StringComparison.Ordinal);\n"
                + "        if (encodedPathIndex <= 0)\n"
                + "        {\n"
                + "            return encodedPathIndex;\n"
                + "        }\n"
                + "\n"
                + "        return HasDirectorySeparatorBeforePrefix(requestPath, encodedPathIndex) ? -1 : encodedPathIndex;\n"
                + "    }\n"
                + "\n"
                + "    private static bool HasDirectorySeparatorBeforePrefix(string requestPath, int prefixIndex)\n"
                + "    {\n"
                + "        for (int i = 0; i < prefixIndex; i++)\n"
                + "        {\n"
                + "            if (requestPath[i] == '\\\\' || requestPath[i] == '/')\n"
                + "            {\n"
                + "                return true;\n"
                + "            }\n"
                + "        }\n"
                + "\n"
                + "        return false;\n"
                + "    }\n"
                + "\n"
                + "    private static string RecoverRawRequestPath(string requestPath)\n"
                + "    {\n"
                + "        string trimmedPath = requestPath.TrimStart('\\uFEFF', '\\uFFFD');\n"
                + "        int drivePathIndex = FindWindowsDrivePathIndex(trimmedPath);\n"
                + "        return drivePathIndex > 0 ? trimmedPath.Substring(drivePathIndex) : trimmedPath;\n"
                + "    }\n"
                + "\n"
                + "    private static int FindWindowsDrivePathIndex(string value)\n"
                + "    {\n"
                + "        for (int i = 0; i <= value.Length - 3; i++)\n"
                + "        {\n"
                + "            bool isDriveLetter = (value[i] >= 'A' && value[i] <= 'Z') || (value[i] >= 'a' && value[i] <= 'z');\n"
                + "            if (isDriveLetter && value[i + 1] == ':' && (value[i + 2] == '\\\\' || value[i + 2] == '/'))\n"
                + "            {\n"
                + "                return i;\n"
                + "            }\n"
                + "        }\n"
                + "\n"
                + "        return -1;\n"
                + "    }\n"
                + "\n"
                + "    private static CompileResponse Compile(string requestPath)\n"
                + "    {\n"
                + "        string[] requestLines = File.ReadAllLines(requestPath);\n"
                + "        string sourcePath = requestLines[0];\n"
                + "        string dllPath = requestLines[1];\n"
                + "        bool allowUnsafe = ParseAllowUnsafe(requestLines);\n"
                + "        string[] defineSymbols = ParseDefineSymbols(requestLines);\n"
                + "        string source = File.ReadAllText(sourcePath, Encoding.UTF8);\n"
                + "        SourceText sourceText = SourceText.From(source, Encoding.UTF8);\n"
                + "        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(sourceText, CreateParseOptions(defineSymbols), sourcePath);\n"
                + "        List<MetadataReference> references = BuildReferences(requestLines);\n"
                + "        CSharpCompilation compilation = CSharpCompilation.Create(Path.GetFileNameWithoutExtension(dllPath), new[] { syntaxTree }, references, CreateCompilationOptions(allowUnsafe));\n"
                + "        using (FileStream peStream = new FileStream(dllPath, FileMode.Create, FileAccess.Write, FileShare.Read))\n"
                + "        {\n"
                + "            EmitResult emitResult = compilation.Emit(peStream);\n"
                + "            int exitCode = 0;\n"
                + "            List<string> diagnosticLines = new List<string>();\n"
                + "            foreach (Diagnostic diagnostic in emitResult.Diagnostics)\n"
                + "            {\n"
                + "                if (diagnostic.Severity != DiagnosticSeverity.Error && diagnostic.Severity != DiagnosticSeverity.Warning)\n"
                + "                {\n"
                + "                    continue;\n"
                + "                }\n"
                + "\n"
                + "                if (diagnostic.Severity == DiagnosticSeverity.Error)\n"
                + "                {\n"
                + "                    exitCode = 1;\n"
                + "                }\n"
                + "\n"
                + "                FileLinePositionSpan lineSpan = diagnostic.Location != Location.None ? diagnostic.Location.GetMappedLineSpan() : default(FileLinePositionSpan);\n"
                + "                int line = lineSpan.StartLinePosition.Line + 1;\n"
                + "                int column = lineSpan.StartLinePosition.Character + 1;\n"
                + "                string file = string.IsNullOrEmpty(lineSpan.Path) ? sourcePath : lineSpan.Path;\n"
                + "                string severity = diagnostic.Severity == DiagnosticSeverity.Warning ? \"warning\" : \"error\";\n"
                + "                diagnosticLines.Add(file + \"(\" + line + \",\" + column + \"): \" + severity + \" \" + diagnostic.Id + \": \" + diagnostic.GetMessage());\n"
                + "            }\n"
                + "\n"
                + "            return new CompileResponse(exitCode, diagnosticLines);\n"
                + "        }\n"
                + "    }\n"
                + "\n"
                + "    private static CSharpParseOptions CreateParseOptions(string[] defineSymbols)\n"
                + "    {\n"
                + "        return defineSymbols == null || defineSymbols.Length == 0\n"
                + "            ? CSharpParseOptions.Default\n"
                + "            : CSharpParseOptions.Default.WithPreprocessorSymbols(defineSymbols);\n"
                + "    }\n"
                + "\n"
                + "    private static CSharpCompilationOptions CreateCompilationOptions(bool allowUnsafe)\n"
                + "    {\n"
                + "        return new CSharpCompilationOptions(\n"
                + "            OutputKind.DynamicallyLinkedLibrary,\n"
                + "            optimizationLevel: OptimizationLevel.Release,\n"
                + "            allowUnsafe: allowUnsafe);\n"
                + "    }\n"
                + "\n"
                + "    private static bool ParseAllowUnsafe(string[] requestLines)\n"
                + "    {\n"
                + "        for (int i = 2; i < requestLines.Length; i++)\n"
                + "        {\n"
                + "            string line = requestLines[i];\n"
                + "            if (!line.StartsWith(UnsafePrefix, StringComparison.Ordinal))\n"
                + "            {\n"
                + "                continue;\n"
                + "            }\n"
                + "\n"
                + "            return string.Equals(line.Substring(UnsafePrefix.Length), \"1\", StringComparison.Ordinal);\n"
                + "        }\n"
                + "\n"
                + "        return false;\n"
                + "    }\n"
                + "\n"
                + "    private static string[] ParseDefineSymbols(string[] requestLines)\n"
                + "    {\n"
                + "        for (int i = 2; i < requestLines.Length; i++)\n"
                + "        {\n"
                + "            string line = requestLines[i];\n"
                + "            if (!line.StartsWith(DefinePrefix, StringComparison.Ordinal))\n"
                + "            {\n"
                + "                continue;\n"
                + "            }\n"
                + "\n"
                + "            string serializedSymbols = line.Substring(DefinePrefix.Length);\n"
                + "            if (string.IsNullOrWhiteSpace(serializedSymbols))\n"
                + "            {\n"
                + "                return new string[0];\n"
                + "            }\n"
                + "\n"
                + "            return serializedSymbols.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);\n"
                + "        }\n"
                + "\n"
                + "        return new string[0];\n"
                + "    }\n"
                + "\n"
                + "    private static List<MetadataReference> BuildReferences(string[] requestLines)\n"
                + "    {\n"
                + "        List<MetadataReference> references = new List<MetadataReference>(Math.Max(0, requestLines.Length - 2));\n"
                + "        lock (SyncRoot)\n"
                + "        {\n"
                + "            for (int i = 2; i < requestLines.Length; i++)\n"
                + "            {\n"
                + "                string referencePath = requestLines[i];\n"
                + "                if (referencePath.StartsWith(UnsafePrefix, StringComparison.Ordinal)\n"
                + "                    || referencePath.StartsWith(DefinePrefix, StringComparison.Ordinal))\n"
                + "                {\n"
                + "                    continue;\n"
                + "                }\n"
                + "\n"
                + "                if (referencePath.StartsWith(ReferencePrefix, StringComparison.Ordinal))\n"
                + "                {\n"
                + "                    referencePath = referencePath.Substring(ReferencePrefix.Length);\n"
                + "                }\n"
                + "\n"
                + "                if (string.IsNullOrWhiteSpace(referencePath) || !File.Exists(referencePath))\n"
                + "                {\n"
                + "                    continue;\n"
                + "                }\n"
                + "\n"
                + "                FileInfo info = new FileInfo(referencePath);\n"
                + "                long writeTicks = info.LastWriteTimeUtc.Ticks;\n"
                + "                long fileLength = info.Length;\n"
                + "                CachedReference cachedReference;\n"
                + "                if (Cache.TryGetValue(referencePath, out cachedReference)\n"
                + "                    && cachedReference.WriteTicks == writeTicks\n"
                + "                    && cachedReference.FileLength == fileLength)\n"
                + "                {\n"
                + "                    references.Add(cachedReference.Reference);\n"
                + "                    continue;\n"
                + "                }\n"
                + "\n"
                + "                PortableExecutableReference createdReference = MetadataReference.CreateFromFile(referencePath);\n"
                + "                Cache[referencePath] = new CachedReference(createdReference, writeTicks, fileLength);\n"
                + "                references.Add(createdReference);\n"
                + "            }\n"
                + "        }\n"
                + "\n"
                + "        return references;\n"
                + "    }\n"
                + "\n"
                + "    private sealed class CachedReference\n"
                + "    {\n"
                + "        public CachedReference(PortableExecutableReference reference, long writeTicks, long fileLength)\n"
                + "        {\n"
                + "            Reference = reference;\n"
                + "            WriteTicks = writeTicks;\n"
                + "            FileLength = fileLength;\n"
                + "        }\n"
                + "\n"
                + "        public PortableExecutableReference Reference { get; }\n"
                + "        public long WriteTicks { get; }\n"
                + "        public long FileLength { get; }\n"
                + "    }\n"
                + "\n"
                + "    private sealed class CompileResponse\n"
                + "    {\n"
                + "        public CompileResponse(int exitCode, List<string> diagnosticLines)\n"
                + "        {\n"
                + "            ExitCode = exitCode;\n"
                + "            DiagnosticLines = diagnosticLines;\n"
                + "        }\n"
                + "\n"
                + "        public int ExitCode { get; }\n"
                + "        public List<string> DiagnosticLines { get; }\n"
                + "    }\n"
                + "}\n";
        }
    }
}
