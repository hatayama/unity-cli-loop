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
        private const string VibeLogSharedWorkerStarted = "dynamic_code_shared_worker_started";

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

        // One per Editor process, outside the worker directory: a reload that drops a warm-up midway
        // leaves only this, which the next warm-up overwrites, and the worker-directory cleanup must
        // not delete a warm-up's files under it.
        internal static string GetWarmUpDirectoryPath()
        {
            return Path.Combine(
                Path.GetTempPath(),
                "UnityCliLoopCompilation",
                $"SharedWorkerWarmUp-{Process.GetCurrentProcess().Id}");
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

        internal static void SynchronizeWorkerSource(WorkerPaths workerPaths, string workerSource)
        {
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

            Stopwatch watch = Stopwatch.StartNew();
            WorkerPaths workerPaths = CreateWorkerPaths(session);
            // Why one string for both: the source written to disk and the source in the cache key
            // must be the same text.
            string workerSource = SharedRoslynCompilerWorkerProtocol.CreateProgramSource();
            SynchronizeWorkerSource(workerPaths, workerSource);
            long sourceSyncMs = watch.ElapsedMilliseconds;

            watch.Restart();
            WorkerAssemblyEnsureResult workerAssemblyResult = await EnsureWorkerAssemblyBuiltAsync(
                session,
                externalCompilerPaths,
                workerPaths,
                workerSource).ConfigureAwait(false);
            long assemblyEnsureMs = watch.ElapsedMilliseconds;
            if (!workerAssemblyResult.Result.IsReady)
            {
                LogWorkerStarted(
                    sourceSyncMs,
                    assemblyEnsureMs,
                    0,
                    workerAssemblyResult,
                    workerAssemblyResult.Result);
                return workerAssemblyResult.Result;
            }

            watch.Restart();
            WorkerStartupResult startResult = StartWorkerProcess(
                session,
                externalCompilerPaths,
                workerPaths,
                lifecycleGenerationAtStart);
            // Only the spawn: the worker's start-up until it answers lands in the first compile.
            LogWorkerStarted(
                sourceSyncMs,
                assemblyEnsureMs,
                watch.ElapsedMilliseconds,
                workerAssemblyResult,
                startResult);
            return startResult;
        }

        // Written only when no live process could serve the request, which after a domain reload
        // is the first compile, so the cold cost of the shared worker shows apart from compiles.
        private static void LogWorkerStarted(
            long sourceSyncMs,
            long assemblyEnsureMs,
            long processStartMs,
            WorkerAssemblyEnsureResult assemblyResult,
            WorkerStartupResult result)
        {
            VibeLogger.LogInfo(
                VibeLogSharedWorkerStarted,
                "Shared Roslyn compiler worker started.",
                new
                {
                    sourceSyncMs,
                    assemblyEnsureMs,
                    processStartMs,
                    ready = result.IsReady,
                    workerAssemblySource = assemblyResult.AssemblySource,
                    cachePublish = assemblyResult.Publish.ToVibeValue(),
                    cachePublishError = assemblyResult.Publish.Error
                });
        }

        internal static async Task<WorkerAssemblyEnsureResult> EnsureWorkerAssemblyBuiltAsync(
            SharedRoslynCompilerWorkerSession session,
            ExternalCompilerPaths externalCompilerPaths,
            WorkerPaths workerPaths,
            string workerSource)
        {
            // Why never publish an existing assembly: nothing checked where it came from, and a csc
            // run that timed out can leave a partly written assembly behind.
            if (File.Exists(workerPaths.AssemblyPath))
            {
                return WorkerAssemblyEnsureResult.FromExistingAssembly();
            }

            string cachedAssemblyPath = SharedRoslynCompilerWorkerAssemblyCache.ResolveCachedAssemblyPath(
                session.ResolveWorkerAssemblyCacheRoot(),
                SharedRoslynCompilerWorkerAssemblyCache.ComputeCacheKey(workerSource, externalCompilerPaths));
            // Why a synchronous copy even on the main thread: the assembly is a few megabytes and
            // copying takes milliseconds, against hundreds for a csc run.
            if (SharedRoslynCompilerWorkerAssemblyCache.TryCopyCachedAssembly(
                    cachedAssemblyPath,
                    workerPaths.AssemblyPath))
            {
                return WorkerAssemblyEnsureResult.FromCache();
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
                return WorkerAssemblyEnsureResult.Failed(WorkerStartupResult.Failure(
                    buildResult.FailureReason,
                    buildResult.FailureContext));
            }

            if (HasErrors(buildResult.Messages))
            {
                SharedRoslynCompilerWorkerAssemblyBuilder.DeleteWorkerAssemblyIfPresent(workerPaths.AssemblyPath);
                return WorkerAssemblyEnsureResult.Failed(WorkerStartupResult.Failure(
                    "worker_build_failed",
                    new
                    {
                        first_error = FindFirstErrorMessage(buildResult.Messages),
                        worker_source_path = workerPaths.SourcePath
                    }));
            }

            // Only an assembly this run's csc finished without errors goes to the cache.
            CachePublishOutcome publish = SharedRoslynCompilerWorkerAssemblyCache.PublishBuiltAssembly(
                workerPaths.AssemblyPath,
                cachedAssemblyPath);
            return WorkerAssemblyEnsureResult.Built(publish);
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
