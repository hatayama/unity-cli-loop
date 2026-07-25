using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor.Compilation;
using Debug = UnityEngine.Debug;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Owns the shared Roslyn compiler worker process, temporary directory, and synchronized lifecycle.
    /// Compile conversations are serialized with an async gate; process state uses a short sync lock
    /// so shutdown can kill the worker without waiting for an in-flight read.
    /// </summary>
    internal sealed class SharedRoslynCompilerWorkerSession
    {
        private readonly SharedRoslynCompilerWorkerSessionCoordination _coordination = new();
        private Func<ProcessStartInfo, Process> _startProcess = ProcessStartHelper.TryStart;
        private Action<Process, string> _sendCompileRequest = SendCompileRequestCore;
        private Func<ExternalCompilerPaths, string, string, string, CompilerMessage[]>
            _compileWorkerAssemblyForTests;
        private Process _workerProcess;
        private string _workerDirectoryPath;
        private int _responseTimeoutMilliseconds =
            SharedRoslynCompilerWorkerLineReader.DefaultResponseTimeoutMilliseconds;
        // Why a generation instead of a sticky bool: full Shutdown (reset/reload/quit) must
        // invalidate in-flight retry loops, while a later compile after reset must still start a worker.
        private int _lifecycleGeneration;

        /// <summary>
        /// Serializes worker request/response conversations without holding the state lock across awaits.
        /// </summary>
        internal Task<T> RunSerializedCompileAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken ct)
        {
            return _coordination.RunSerializedCompileAsync(operation, ct);
        }

        /// <summary>
        /// Runs a short critical section over process/directory state.
        /// </summary>
        internal T ExecuteWithStateLock<T>(Func<T> operation)
        {
            return _coordination.ExecuteWithStateLock(operation);
        }

        internal void ExecuteWithStateLock(Action operation)
        {
            _coordination.ExecuteWithStateLock(operation);
        }

        /// <summary>
        /// Legacy sync entry used by EditMode tests that only touch process start/dispose.
        /// </summary>
        internal T ExecuteLocked<T>(Func<T> operation)
        {
            return ExecuteWithStateLock(operation);
        }

        internal int ResponseTimeoutMilliseconds
        {
            get
            {
                return _coordination.ExecuteWithStateLock(() => _responseTimeoutMilliseconds);
            }
        }

        internal bool HasLiveProcessLocked()
        {
            AssertStateLockHeld();
            return _workerProcess != null && !_workerProcess.HasExited;
        }

        internal int GetLifecycleGenerationLocked()
        {
            AssertStateLockHeld();
            return _lifecycleGeneration;
        }

        internal bool IsLifecycleGenerationCurrentLocked(int expectedLifecycleGeneration)
        {
            AssertStateLockHeld();
            return _lifecycleGeneration == expectedLifecycleGeneration;
        }

        internal bool StartProcessLocked(ProcessStartInfo startInfo)
        {
            AssertStateLockHeld();
            // EnsureWorkerReady reaches this method only after the same lock observed no live process,
            // so replacement releases the stale handle without retrying graceful shutdown.
            Process previousProcess = _workerProcess;
            _workerProcess = null;
            previousProcess?.Dispose();

            _workerProcess = _startProcess(startInfo);
            return _workerProcess != null;
        }

        internal void SendCompileRequestLocked(string requestFilePath)
        {
            AssertStateLockHeld();
            _sendCompileRequest(_workerProcess, requestFilePath);
        }

        internal StreamReader GetOutputReaderLocked()
        {
            AssertStateLockHeld();
            return _workerProcess.StandardOutput;
        }

        internal void RecordWorkerDirectoryLocked(string workerDirectoryPath)
        {
            AssertStateLockHeld();
            _workerDirectoryPath = workerDirectoryPath;
        }

        internal SharedRoslynCompilerWorkerAssemblyBuilder.WorkerAssemblyBuildResult CompileWorkerAssembly(
            ExternalCompilerPaths externalCompilerPaths,
            string workerSourcePath,
            string workerAssemblyPath,
            string workerCompileResponseFilePath)
        {
            if (_compileWorkerAssemblyForTests != null)
            {
                return SharedRoslynCompilerWorkerAssemblyBuilder.WorkerAssemblyBuildResult.Started(
                    _compileWorkerAssemblyForTests(
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

        /// <summary>
        /// Builds the worker assembly without blocking the caller thread on WaitForExit.
        /// Test hooks stay synchronous so EditMode fixtures do not need thread-pool coordination.
        /// </summary>
        internal Task<SharedRoslynCompilerWorkerAssemblyBuilder.WorkerAssemblyBuildResult>
            CompileWorkerAssemblyAsync(
                ExternalCompilerPaths externalCompilerPaths,
                string workerSourcePath,
                string workerAssemblyPath,
                string workerCompileResponseFilePath)
        {
            if (_compileWorkerAssemblyForTests != null)
            {
                return Task.FromResult(CompileWorkerAssembly(
                    externalCompilerPaths,
                    workerSourcePath,
                    workerAssemblyPath,
                    workerCompileResponseFilePath));
            }

            // Why not take the state lock here: build stays outside the process lock so shutdown
            // can still kill a live worker while this Task.Run is in flight.
            return SharedRoslynCompilerWorkerAssemblyBuilder.CompileWorkerAssemblyOffMainThreadAsync(
                externalCompilerPaths,
                workerSourcePath,
                workerAssemblyPath,
                workerCompileResponseFilePath);
        }

        internal void ShutdownProcessLocked()
        {
            AssertStateLockHeld();
            Process workerProcess = _workerProcess;
            _workerProcess = null;
            if (workerProcess == null)
            {
                return;
            }

            ExecuteProcessShutdown(
                hasExited: () => workerProcess.HasExited,
                requestGracefulShutdown: () =>
                {
                    workerProcess.StandardInput.WriteLine(
                        SharedRoslynCompilerWorkerProtocol.SharedCompilerWorkerQuitCommand);
                    workerProcess.StandardInput.Flush();
                    workerProcess.WaitForExit(500);
                },
                forceKill: () =>
                {
                    workerProcess.Kill();
                    workerProcess.WaitForExit(500);
                },
                dispose: workerProcess.Dispose,
                logFailure: LogWorkerShutdownFailure);
        }

        internal static void ExecuteProcessShutdown(
            Func<bool> hasExited,
            Action requestGracefulShutdown,
            Action forceKill,
            Action dispose,
            Action<Exception> logFailure)
        {
            Debug.Assert(hasExited != null, "hasExited must not be null");
            Debug.Assert(requestGracefulShutdown != null, "requestGracefulShutdown must not be null");
            Debug.Assert(forceKill != null, "forceKill must not be null");
            Debug.Assert(dispose != null, "dispose must not be null");
            Debug.Assert(logFailure != null, "logFailure must not be null");

            try
            {
                // A failed graceful request must not prevent the forced termination phase.
                TryRequestGracefulShutdown(hasExited, requestGracefulShutdown, logFailure);
                TryForceKill(hasExited, forceKill, logFailure);
            }
            finally
            {
                dispose();
            }
        }

        private static void TryRequestGracefulShutdown(
            Func<bool> hasExited,
            Action requestGracefulShutdown,
            Action<Exception> logFailure)
        {
            try
            {
                if (!hasExited())
                {
                    requestGracefulShutdown();
                }
            }
            catch (IOException ex)
            {
                logFailure(ex);
            }
            catch (ObjectDisposedException ex)
            {
                logFailure(ex);
            }
            catch (InvalidOperationException ex)
            {
                logFailure(ex);
            }
            catch (Win32Exception ex)
            {
                logFailure(ex);
            }
        }

        private static void TryForceKill(
            Func<bool> hasExited,
            Action forceKill,
            Action<Exception> logFailure)
        {
            bool shouldForceKill;
            try
            {
                shouldForceKill = !hasExited();
            }
            catch (Win32Exception ex)
            {
                logFailure(ex);
                // An unavailable exit code leaves process state unknown, so forced termination is safer.
                shouldForceKill = true;
            }
            catch (InvalidOperationException ex)
            {
                logFailure(ex);
                return;
            }

            if (!shouldForceKill)
            {
                return;
            }

            try
            {
                forceKill();
            }
            catch (Win32Exception ex)
            {
                logFailure(ex);
            }
            catch (InvalidOperationException ex)
            {
                logFailure(ex);
            }
        }

        /// <summary>
        /// Shuts down the worker without waiting for the compile gate.
        /// In-flight readers fail fast when the process pipes close.
        /// </summary>
        internal void Shutdown(string fallbackWorkerDirectoryPath)
        {
            _coordination.RunShutdownWithoutCompileGate(() =>
            {
                // Why advance here (not in ShutdownProcessLocked): retry cleanup kills the process
                // so the same compile conversation can start a replacement worker. Server reset /
                // reload / quit must invalidate that restart path for in-flight retries only.
                _lifecycleGeneration++;
                ShutdownProcessLocked();
                CleanupWorkerDirectoryLocked(fallbackWorkerDirectoryPath);
            });
        }

        internal Func<ProcessStartInfo, Process> SwapProcessStarterForTests(
            Func<ProcessStartInfo, Process> starter)
        {
            Debug.Assert(starter != null, "starter must not be null");

            Func<ProcessStartInfo, Process> previous = _startProcess;
            _startProcess = starter;
            return previous;
        }

        internal Func<ExternalCompilerPaths, string, string, string, CompilerMessage[]>
            SwapWorkerAssemblyCompilerForTests(
                Func<ExternalCompilerPaths, string, string, string, CompilerMessage[]> compiler)
        {
            Func<ExternalCompilerPaths, string, string, string, CompilerMessage[]> previous =
                _compileWorkerAssemblyForTests;
            _compileWorkerAssemblyForTests = compiler;
            return previous;
        }

        private static void SendCompileRequestCore(Process workerProcess, string requestFilePath)
        {
            string requestCommand = SharedRoslynCompilerWorkerProtocol.CreateCompileRequestCommand(requestFilePath);
            workerProcess.StandardInput.WriteLine(requestCommand);
            workerProcess.StandardInput.Flush();
        }

        private void CleanupWorkerDirectoryLocked(string fallbackWorkerDirectoryPath)
        {
            AssertStateLockHeld();
            string workerDirectoryPath = _workerDirectoryPath ?? fallbackWorkerDirectoryPath;
            if (!Directory.Exists(workerDirectoryPath))
            {
                _workerDirectoryPath = null;
                return;
            }

            TryDeleteWorkerDirectory(workerDirectoryPath);
            _workerDirectoryPath = null;
        }

        private void TryDeleteWorkerDirectory(string workerDirectoryPath)
        {
            try
            {
                Directory.Delete(workerDirectoryPath, true);
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

        private void AssertStateLockHeld()
        {
            _coordination.AssertStateLockHeld();
        }
    }
}
