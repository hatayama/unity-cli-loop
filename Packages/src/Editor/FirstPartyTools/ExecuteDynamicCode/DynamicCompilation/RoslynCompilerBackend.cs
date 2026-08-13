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
            string workerRequestFilePath = Path.ChangeExtension(sourcePath, ".worker");
            IReadOnlyCollection<string> defineSymbols = compilerOptions.DefineSymbols;
            bool allowUnsafeCode = compilerOptions.AllowUnsafeCode;
            bool emitDebugCode = compilerOptions.EmitDebugCode;

            try
            {
                WriteWorkerRequestFile(
                    workerRequestFilePath,
                    sourcePath,
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
                    sourcePath,
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
                if (oneShotResult.ShouldFallback)
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
            string sourcePath,
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
            string responseFilePath = Path.ChangeExtension(sourcePath, ".rsp");
            WriteCompilerResponseFile(
                responseFilePath,
                sourcePath,
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
                    using Process process = ProcessStartHelper.TryStart(startInfo);
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

        internal static void WriteCompilerResponseFile(
            string responseFilePath,
            string sourcePath,
            string dllPath,
            IReadOnlyCollection<string> references,
            IReadOnlyCollection<string> defineSymbols,
            bool allowUnsafeCode,
            bool emitDebugCode)
        {
            List<string> lines = new()
            {
                "-nologo",
                // Diagnostics must be machine-readable by AI agents: English text, UTF-8 bytes.
                "-preferreduilang:en-US",
                "-utf8output",
                "-nostdlib+",
                "-target:library",
                // Why -optimize- for emitDebugCode: hot-reload shims need locals in the PDB for
                // pause-point capture; execute-dynamic-code keeps -optimize+.
                emitDebugCode ? "-optimize-" : "-optimize+",
                // Why portable: one-shot csc fallback must emit a PDB so Assembly.Load can map
                // runtime exceptions back to user-snippet.cs lines (same as the shared worker).
                "-debug:portable",
                allowUnsafeCode ? "-unsafe+" : "-unsafe-",
                QuoteResponseFileArgument("-out:", dllPath)
            };

            // Why pathmap: csc resolves relative #line document paths against the source file's
            // directory and writes work-directory absolute URLs into the PDB; map the compile
            // directory back to the project root so PDB documents point at the real project files
            // (the shared worker's ParseText path leaves the literal as-is and needs no mapping).
            // Directory.GetCurrentDirectory() is the Unity project root inside the Editor.
            string sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(sourcePath));
            lines.Add(QuoteResponseFileArgument("-pathmap:", sourceDirectory + "=" + Directory.GetCurrentDirectory()));

            string defineOption = BuildDefineOption(defineSymbols);
            if (!string.IsNullOrEmpty(defineOption))
            {
                lines.Add(defineOption);
            }

            foreach (string reference in references)
            {
                lines.Add(QuoteResponseFileArgument("-r:", reference));
            }

            lines.Add(QuoteResponseFilePath(sourcePath));
            File.WriteAllLines(responseFilePath, lines);
        }

        internal static void WriteWorkerRequestFile(
            string requestFilePath,
            string sourcePath,
            string dllPath,
            IReadOnlyCollection<string> references,
            IReadOnlyCollection<string> defineSymbols,
            bool allowUnsafeCode,
            bool emitDebugCode)
        {
            List<string> lines = new() { Path.GetFullPath(sourcePath), Path.GetFullPath(dllPath) };
            lines.Add(allowUnsafeCode ? "unsafe:1" : "unsafe:0");
            lines.Add(emitDebugCode ? "debugCode:1" : "debugCode:0");

            string serializedDefines = SerializeDefineSymbols(defineSymbols);
            if (!string.IsNullOrEmpty(serializedDefines))
            {
                lines.Add($"define:{serializedDefines}");
            }

            foreach (string reference in references)
            {
                lines.Add($"ref:{Path.GetFullPath(reference)}");
            }

            File.WriteAllLines(Path.GetFullPath(requestFilePath), lines);
        }

        private static string BuildDefineOption(IReadOnlyCollection<string> defineSymbols)
        {
            string serializedDefines = SerializeDefineSymbols(defineSymbols);
            if (string.IsNullOrEmpty(serializedDefines))
            {
                return null;
            }

            return $"-define:{serializedDefines}";
        }

        private static string SerializeDefineSymbols(IReadOnlyCollection<string> defineSymbols)
        {
            if (defineSymbols == null || defineSymbols.Count == 0)
            {
                return string.Empty;
            }

            List<string> filteredDefines = new(defineSymbols.Count);
            foreach (string defineSymbol in defineSymbols)
            {
                if (!string.IsNullOrWhiteSpace(defineSymbol))
                {
                    filteredDefines.Add(defineSymbol);
                }
            }

            return filteredDefines.Count == 0 ? string.Empty : string.Join(";", filteredDefines);
        }

        private static string QuoteResponseFileArgument(string prefix, string value)
        {
            return $"{prefix}{QuoteResponseFilePath(value)}";
        }

        private static string QuoteResponseFilePath(string path)
        {
            return $"\"{path}\"";
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
