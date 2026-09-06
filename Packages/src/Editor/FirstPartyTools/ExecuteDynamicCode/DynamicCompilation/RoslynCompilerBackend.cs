using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor.Compilation;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Provides Roslyn Compiler Backend behavior for Unity CLI Loop.
    /// </summary>
    internal static class RoslynCompilerBackend
    {
        private static Func<ProcessStartInfo, Process> oneShotProcessStarter = ProcessStartHelper.TryStart;
        /// <summary>
        /// Carries the result data produced by One Shot Compile behavior.
        /// </summary>
        private sealed class OneShotCompileResult
        {
            public DynamicCompilationBackendResult BackendResult { get; }

            public bool ShouldFallback { get; }

            private OneShotCompileResult(DynamicCompilationBackendResult backendResult, bool shouldFallback)
            {
                BackendResult = backendResult;
                ShouldFallback = shouldFallback;
            }

            public static OneShotCompileResult Successful(CompilerMessage[] compilerMessages)
            {
                return new OneShotCompileResult(
                    new DynamicCompilationBackendResult(
                        compilerMessages,
                        DynamicCompilationBackendKind.OneShotRoslyn),
                    false);
            }

            public static OneShotCompileResult Fallback()
            {
                return new OneShotCompileResult(null, true);
            }
        }

        public static async Task<DynamicCompilationBackendResult> CompileAsync(
            string sourcePath,
            string dllPath,
            List<string> references,
            ExternalCompilerPaths externalCompilerPaths,
            RoslynCompilerOptions compilerOptions,
            CancellationToken ct,
            Action markBuildStarted,
            Action markBuildFinished,
            Action incrementBuildCount)
        {
            return await CompileSourcesAsync(
                new[] { sourcePath },
                dllPath,
                references,
                externalCompilerPaths,
                compilerOptions,
                ct,
                markBuildStarted,
                markBuildFinished,
                incrementBuildCount,
                allowAssemblyBuilderFallback: true).ConfigureAwait(false);
        }

        public static async Task<DynamicCompilationBackendResult> CompileMultipleSourcesAsync(
            IReadOnlyList<string> sourcePaths,
            string dllPath,
            List<string> references,
            ExternalCompilerPaths externalCompilerPaths,
            RoslynCompilerOptions compilerOptions,
            CancellationToken ct,
            Action markBuildStarted,
            Action markBuildFinished,
            Action incrementBuildCount)
        {
            return await CompileSourcesAsync(
                sourcePaths,
                dllPath,
                references,
                externalCompilerPaths,
                compilerOptions,
                ct,
                markBuildStarted,
                markBuildFinished,
                incrementBuildCount,
                allowAssemblyBuilderFallback: false).ConfigureAwait(false);
        }

        private static async Task<DynamicCompilationBackendResult> CompileSourcesAsync(
            IReadOnlyList<string> sourcePaths,
            string dllPath,
            List<string> references,
            ExternalCompilerPaths externalCompilerPaths,
            RoslynCompilerOptions compilerOptions,
            CancellationToken ct,
            Action markBuildStarted,
            Action markBuildFinished,
            Action incrementBuildCount,
            bool allowAssemblyBuilderFallback)
        {
            RoslynCompilerRequestFileWriter.ValidateSourcePaths(sourcePaths);
            string sourcePath = sourcePaths[0];
            string workerRequestFilePath = RoslynCompilerRequestFileWriter.CreateRequestFilePath(sourcePath, ".worker", sourcePaths.Count > 1);
            IReadOnlyCollection<string> defineSymbols = compilerOptions.DefineSymbols;
            bool allowUnsafeCode = compilerOptions.AllowUnsafeCode;
            bool emitDebugCode = compilerOptions.EmitDebugCode;

            try
            {
                RoslynCompilerRequestFileWriter.WriteMultipleSourcesWorkerRequestFile(
                    workerRequestFilePath,
                    sourcePaths,
                    dllPath,
                    references,
                    defineSymbols,
                    allowUnsafeCode,
                    emitDebugCode);

                SharedWorkerCompileOutcome workerOutcome = await SharedRoslynCompilerWorkerHost.TryCompileAsync(
                    workerRequestFilePath,
                    externalCompilerPaths,
                    ct,
                    markBuildStarted,
                    markBuildFinished,
                    incrementBuildCount).ConfigureAwait(false);
                UnityEngine.Debug.Assert(workerOutcome != null, "Shared worker compile outcome must not be null");
                if (workerOutcome.Succeeded)
                {
                    return new DynamicCompilationBackendResult(
                        workerOutcome.Messages,
                        DynamicCompilationBackendKind.SharedRoslynWorker);
                }

                if (!workerOutcome.IsLifecycleClosed)
                {
                    DynamicCompilationHealthMonitor.ReportSharedWorkerFallback(
                        "worker_unavailable",
                        new
                        {
                            platform = UnityEngine.Application.platform.ToString(),
                            dotnet_host_path = externalCompilerPaths.DotnetHostPath,
                            compiler_dll_path = externalCompilerPaths.CompilerDllPath,
                            layout_kind = externalCompilerPaths.LayoutKind.ToString()
                        });
                }

                ct.ThrowIfCancellationRequested();
                OneShotCompileResult oneShotResult = await CompileWithOneShotAsync(
                    sourcePaths,
                    dllPath,
                    references,
                    defineSymbols,
                    allowUnsafeCode,
                    emitDebugCode,
                    externalCompilerPaths,
                    ct,
                    markBuildStarted,
                    markBuildFinished,
                    incrementBuildCount).ConfigureAwait(false);
                if (oneShotResult.ShouldFallback && allowAssemblyBuilderFallback)
                {
                    return await AssemblyBuilderFallbackCompilerBackend.CompileAsync(
                        sourcePath,
                        dllPath,
                        references,
                        ct,
                        markBuildStarted,
                        markBuildFinished,
                        incrementBuildCount).ConfigureAwait(false);
                }

                if (oneShotResult.ShouldFallback)
                {
                    return new DynamicCompilationBackendResult(
                        new[]
                        {
                            new CompilerMessage
                            {
                                type = CompilerMessageType.Error,
                                message = "No supported Roslyn compiler backend is available for multiple sources."
                            }
                        },
                        DynamicCompilationBackendKind.Unknown);
                }

                return oneShotResult.BackendResult;
            }
            finally
            {
                if (File.Exists(workerRequestFilePath))
                {
                    File.Delete(workerRequestFilePath);
                }
            }
        }

        private static async Task<OneShotCompileResult> CompileWithOneShotAsync(
            IReadOnlyList<string> sourcePaths,
            string dllPath,
            List<string> references,
            IReadOnlyCollection<string> defineSymbols,
            bool allowUnsafeCode,
            bool emitDebugCode,
            ExternalCompilerPaths externalCompilerPaths,
            CancellationToken ct,
            Action markBuildStarted,
            Action markBuildFinished,
            Action incrementBuildCount)
        {
            string sourcePath = sourcePaths[0];
            string responseFilePath = RoslynCompilerRequestFileWriter.CreateRequestFilePath(sourcePath, ".rsp", sourcePaths.Count > 1);
            RoslynCompilerRequestFileWriter.WriteMultipleSourcesCompilerResponseFile(
                responseFilePath,
                sourcePaths,
                dllPath,
                references,
                defineSymbols,
                allowUnsafeCode,
                emitDebugCode);

            try
            {
                incrementBuildCount();

                ProcessStartInfo startInfo = new()
                {
                    FileName = externalCompilerPaths.DotnetHostPath,
                    Arguments = $"{QuoteCommandLineArgument(externalCompilerPaths.CompilerDllPath)} @{QuoteCommandLineArgument(responseFilePath)}",
                    WorkingDirectory = Directory.GetCurrentDirectory(),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                markBuildStarted();

                try
                {
                    using Process process = oneShotProcessStarter(startInfo);
                    if (process == null)
                    {
                        DynamicCompilationHealthMonitor.ReportOneShotCompilerStartFailure(new
                        {
                            dotnet_host_path = externalCompilerPaths.DotnetHostPath,
                            compiler_dll_path = externalCompilerPaths.CompilerDllPath,
                            layout_kind = externalCompilerPaths.LayoutKind.ToString()
                        });

                        return OneShotCompileResult.Fallback();
                    }

                    OneShotProcessCompletionResult completionResult = await WaitForOneShotCompilerAsync(process, ct)
                        .ConfigureAwait(false);

                    CompilerMessage[] compilerMessages = ExternalCompilerMessageParser.Parse(
                        completionResult.StandardOutput,
                        completionResult.StandardError,
                        completionResult.ExitCode);
                    if (ShouldRetryWithAssemblyBuilder(process.ExitCode, compilerMessages))
                    {
                        ReportInfrastructureFallback(externalCompilerPaths, process.ExitCode);
                        return OneShotCompileResult.Fallback();
                    }

                    return OneShotCompileResult.Successful(compilerMessages);
                }
                finally
                {
                    markBuildFinished();
                }
            }
            finally
            {
                if (File.Exists(responseFilePath))
                {
                    File.Delete(responseFilePath);
                }
            }
        }

        // Infrastructure-level failures (non-zero exit without file-specific diagnostics)
        // indicate the compiler itself broke, not the user's code.
        private static bool ShouldRetryWithAssemblyBuilder(
            int exitCode,
            IReadOnlyCollection<CompilerMessage> compilerMessages)
        {
            if (exitCode == 0)
            {
                return false;
            }

            foreach (CompilerMessage compilerMessage in compilerMessages)
            {
                if (compilerMessage.type == CompilerMessageType.Error &&
                    !string.IsNullOrWhiteSpace(compilerMessage.file))
                {
                    return false;
                }
            }

            return true;
        }

        private static string QuoteCommandLineArgument(string value)
        {
            return $"\"{value}\"";
        }

        internal static Func<ProcessStartInfo, Process> SwapOneShotProcessStarterForTests(
            Func<ProcessStartInfo, Process> replacement)
        {
            Func<ProcessStartInfo, Process> previous = oneShotProcessStarter;
            oneShotProcessStarter = replacement ?? throw new ArgumentNullException(nameof(replacement));
            return previous;
        }

        /// <summary>
        /// Carries the result data produced by One Shot Process Completion behavior.
        /// </summary>
        internal sealed class OneShotProcessCompletionResult
        {
            public string StandardOutput { get; }

            public string StandardError { get; }

            public int ExitCode { get; }

            public OneShotProcessCompletionResult(
                string standardOutput,
                string standardError,
                int exitCode)
            {
                StandardOutput = standardOutput;
                StandardError = standardError;
                ExitCode = exitCode;
            }
        }

        private static async Task<OneShotProcessCompletionResult> WaitForOneShotCompilerAsync(
            Process process,
            CancellationToken ct)
        {
            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();
            Task waitForExitTask = Task.Run(() => process.WaitForExit());
            return await AwaitOneShotProcessCompletionAsync(
                stdoutTask,
                stderrTask,
                waitForExitTask,
                () => process.ExitCode,
                () => RequestCancellation(process),
                ct).ConfigureAwait(false);
        }

        private static void RequestCancellation(Process process)
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
        }

        internal static async Task<OneShotProcessCompletionResult> AwaitOneShotProcessCompletionAsync(
            Task<string> stdoutTask,
            Task<string> stderrTask,
            Task waitForExitTask,
            Func<int> getExitCode,
            Action requestCancellation,
            CancellationToken ct)
        {
            Task completionTask = Task.WhenAll(stdoutTask, stderrTask, waitForExitTask);
            Task cancellationTask = Task.Delay(Timeout.Infinite, ct);

            Task finishedTask = await Task.WhenAny(completionTask, cancellationTask).ConfigureAwait(false);
            if (!ReferenceEquals(finishedTask, completionTask))
            {
                requestCancellation();
                ObserveTaskFault(completionTask);
                ct.ThrowIfCancellationRequested();
            }

            await completionTask.ConfigureAwait(false);
            return new OneShotProcessCompletionResult(
                stdoutTask.Result,
                stderrTask.Result,
                getExitCode());
        }

        internal static void ReportInfrastructureFallback(
            ExternalCompilerPaths externalCompilerPaths,
            int exitCode)
        {
            DynamicCompilationHealthMonitor.ReportOneShotCompilerStartFailure(new
            {
                reason = "infrastructure_failure",
                exit_code = exitCode,
                dotnet_host_path = externalCompilerPaths.DotnetHostPath,
                compiler_dll_path = externalCompilerPaths.CompilerDllPath,
                layout_kind = externalCompilerPaths.LayoutKind.ToString()
            });
        }

        private static void ObserveTaskFault(Task task)
        {
            _ = task.ContinueWith(
                static observedTask => _ = observedTask.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }
}
