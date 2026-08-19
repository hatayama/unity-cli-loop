using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEditor.Compilation;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Provides worker process paths, source sync, assembly build, and startup for the shared Roslyn compiler worker.
    /// </summary>
    internal static class SharedRoslynCompilerWorkerHostProcess
    {
        private const string RoslynWorkerSourceFileName = "RoslynCompilerWorker.cs";
        private const string RoslynWorkerAssemblyFileName = "RoslynCompilerWorker.dll";
        private const string RoslynWorkerCompileResponseFileName = "RoslynCompilerWorker.rsp";

        /// <summary>
        /// Provides Worker Paths behavior for Unity CLI Loop.
        /// </summary>
        internal sealed class WorkerPaths
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

        internal static string GetWorkerDirectoryPath()
        {
            return Path.Combine(
                Path.GetTempPath(),
                "UnityCliLoopCompilation",
                $"RoslynWorker-{Process.GetCurrentProcess().Id}");
        }

        internal static WorkerPaths CreateWorkerPaths(SharedRoslynCompilerWorkerSession session)
        {
            string workerDirectoryPath = GetWorkerDirectoryPath();
            Directory.CreateDirectory(workerDirectoryPath);
            session.ExecuteWithStateLock(
                () => session.RecordWorkerDirectoryLocked(workerDirectoryPath));
            return new WorkerPaths(
                workerDirectoryPath,
                Path.Combine(workerDirectoryPath, RoslynWorkerSourceFileName),
                Path.Combine(workerDirectoryPath, RoslynWorkerAssemblyFileName),
                Path.Combine(workerDirectoryPath, RoslynWorkerCompileResponseFileName));
        }

        internal static void SynchronizeWorkerSource(WorkerPaths workerPaths)
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

        internal static ProcessStartInfo CreateWorkerStartInfo(
            ExternalCompilerPaths externalCompilerPaths,
            WorkerPaths workerPaths)
        {
            ProcessStartInfo startInfo = new()
            {
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
                // Pairs with Console.OutputEncoding = Encoding.UTF8 in the worker template so
                // diagnostic text survives non-UTF-8 default codepages on Windows. Stderr is
                // not redirected, so StandardErrorEncoding must stay unset — Process.Start
                // rejects it when RedirectStandardError is false.
                StandardOutputEncoding = Encoding.UTF8,
                CreateNoWindow = true
            };

            SharedRoslynCompilerWorkerAssemblyBuilder.ConfigureWorkerDotnetRuntimeEnvironment(startInfo);
            return startInfo;
        }

        internal static async Task<WorkerStartupResult> EnsureWorkerReadyAsync(
            SharedRoslynCompilerWorkerSession session,
            ExternalCompilerPaths externalCompilerPaths,
            int lifecycleGenerationAtStart)
        {
            WorkerStartupResult earlyResult = session.ExecuteWithStateLock(() =>
            {
                if (!session.IsLifecycleGenerationCurrentLocked(lifecycleGenerationAtStart))
                {
                    return WorkerStartupResult.ClosedLifecycleFailure();
                }

                if (session.HasLiveProcessLocked())
                {
                    return WorkerStartupResult.Ready();
                }

                return null;
            });
            if (earlyResult != null)
            {
                return earlyResult;
            }

            WorkerPaths workerPaths = CreateWorkerPaths(session);
            SynchronizeWorkerSource(workerPaths);

            WorkerStartupResult workerAssemblyResult = await EnsureWorkerAssemblyBuiltAsync(
                session,
                externalCompilerPaths,
                workerPaths).ConfigureAwait(false);
            if (!workerAssemblyResult.IsReady)
            {
                return workerAssemblyResult;
            }

            return StartWorkerProcess(
                session,
                externalCompilerPaths,
                workerPaths,
                lifecycleGenerationAtStart);
        }

        internal static async Task<WorkerStartupResult> EnsureWorkerAssemblyBuiltAsync(
            SharedRoslynCompilerWorkerSession session,
            ExternalCompilerPaths externalCompilerPaths,
            WorkerPaths workerPaths)
        {
            if (File.Exists(workerPaths.AssemblyPath))
            {
                return WorkerStartupResult.Ready();
            }

            // Why outside state lock: worker DLL compile can take seconds; shutdown must still kill
            // an already-running shared worker without waiting on this build.
            // Why Task.Run (via CompileWorkerAssemblyAsync): WaitForExit(timeout) is synchronous and
            // this path can still run on the Unity main thread before the first await when the
            // compile gate is acquired without yielding.
            SharedRoslynCompilerWorkerAssemblyBuilder.WorkerAssemblyBuildResult buildResult =
                await session.CompileWorkerAssemblyAsync(
                    externalCompilerPaths,
                    workerPaths.SourcePath,
                    workerPaths.AssemblyPath,
                    workerPaths.CompileResponseFilePath).ConfigureAwait(false);
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

        internal static WorkerStartupResult StartWorkerProcess(
            SharedRoslynCompilerWorkerSession session,
            ExternalCompilerPaths externalCompilerPaths,
            WorkerPaths workerPaths,
            int lifecycleGenerationAtStart)
        {
            ProcessStartInfo startInfo = CreateWorkerStartInfo(externalCompilerPaths, workerPaths);
            return session.ExecuteWithStateLock(() =>
            {
                if (!session.IsLifecycleGenerationCurrentLocked(lifecycleGenerationAtStart))
                {
                    return WorkerStartupResult.ClosedLifecycleFailure();
                }

                bool started = session.StartProcessLocked(startInfo);
                if (!started)
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
            });
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
    }
}
