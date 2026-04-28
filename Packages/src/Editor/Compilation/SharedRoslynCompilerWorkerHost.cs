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
        private const string RoslynWorkerProgramTemplateRelativePath =
            "Editor/Compilation/Templates/SharedRoslynCompilerWorkerProgram.cs.template";
        private const string SharedCompilerWorkerResultPrefixToken = "{{SHARED_COMPILER_WORKER_RESULT_PREFIX}}";
        private const string SharedCompilerWorkerEndMarkerToken = "{{SHARED_COMPILER_WORKER_END_MARKER}}";
        private const string SharedCompilerWorkerQuitCommandToken = "{{SHARED_COMPILER_WORKER_QUIT_COMMAND}}";
        private const string CompileRequestPathPrefixToken = "{{COMPILE_REQUEST_PATH_PREFIX}}";
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
            string templatePath = GetWorkerProgramTemplatePath();
            string templateSource = File.ReadAllText(templatePath, Encoding.UTF8);
            return templateSource
                .Replace(SharedCompilerWorkerResultPrefixToken, SharedCompilerWorkerResultPrefix)
                .Replace(SharedCompilerWorkerEndMarkerToken, SharedCompilerWorkerEndMarker)
                .Replace(SharedCompilerWorkerQuitCommandToken, SharedCompilerWorkerQuitCommand)
                .Replace(CompileRequestPathPrefixToken, CompileRequestPathPrefix);
        }

        internal static string GetWorkerProgramTemplatePathForTests()
        {
            return GetWorkerProgramTemplatePath();
        }

        internal static string GetSharedCompilerWorkerResultPrefixForTests()
        {
            return SharedCompilerWorkerResultPrefix;
        }

        internal static string GetSharedCompilerWorkerEndMarkerForTests()
        {
            return SharedCompilerWorkerEndMarker;
        }

        internal static string GetSharedCompilerWorkerQuitCommandForTests()
        {
            return SharedCompilerWorkerQuitCommand;
        }

        private static string GetWorkerProgramTemplatePath()
        {
            return Path.Combine(McpConstants.PackageResolvedPath, RoslynWorkerProgramTemplateRelativePath);
        }
    }
}
