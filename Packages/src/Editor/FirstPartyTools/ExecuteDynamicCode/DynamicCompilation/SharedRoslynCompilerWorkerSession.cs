using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEditor.Compilation;
using Debug = UnityEngine.Debug;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Owns the shared Roslyn compiler worker process, temporary directory, and synchronized lifecycle.
    /// </summary>
    internal sealed class SharedRoslynCompilerWorkerSession
    {
        private readonly object _syncRoot = new();
        private Action<string> _deleteWorkerDirectory = path => Directory.Delete(path, true);
        private Action<Process, string> _sendCompileRequest = SendCompileRequestCore;
        private Func<ExternalCompilerPaths, string, string, string, CompilerMessage[]>
            _compileWorkerAssemblyForTests;
        private Process _workerProcess;
        private string _workerDirectoryPath;

        internal T ExecuteLocked<T>(Func<T> operation)
        {
            Debug.Assert(operation != null, "operation must not be null");

            lock (_syncRoot)
            {
                return operation();
            }
        }

        internal bool HasLiveProcessLocked()
        {
            AssertLockHeld();
            return _workerProcess != null && !_workerProcess.HasExited;
        }

        internal bool StartProcessLocked(ProcessStartInfo startInfo)
        {
            AssertLockHeld();
            _workerProcess = ProcessStartHelper.TryStart(startInfo);
            return _workerProcess != null;
        }

        internal void SendCompileRequestLocked(string requestFilePath)
        {
            AssertLockHeld();
            _sendCompileRequest(_workerProcess, requestFilePath);
        }

        internal StreamReader GetOutputReaderLocked()
        {
            AssertLockHeld();
            return _workerProcess.StandardOutput;
        }

        internal void RecordWorkerDirectoryLocked(string workerDirectoryPath)
        {
            AssertLockHeld();
            _workerDirectoryPath = workerDirectoryPath;
        }

        internal SharedRoslynCompilerWorkerAssemblyBuilder.WorkerAssemblyBuildResult CompileWorkerAssemblyLocked(
            ExternalCompilerPaths externalCompilerPaths,
            string workerSourcePath,
            string workerAssemblyPath,
            string workerCompileResponseFilePath)
        {
            AssertLockHeld();
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

        internal void ShutdownProcessLocked()
        {
            AssertLockHeld();
            Process workerProcess = _workerProcess;
            _workerProcess = null;
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

        internal void Shutdown(string fallbackWorkerDirectoryPath)
        {
            lock (_syncRoot)
            {
                ShutdownProcessLocked();
                CleanupWorkerDirectoryLocked(fallbackWorkerDirectoryPath);
            }
        }

        internal Action<string> SwapWorkerDirectoryDeleterForTests(Action<string> deleter)
        {
            Debug.Assert(deleter != null, "deleter must not be null");

            Action<string> previous = _deleteWorkerDirectory;
            _deleteWorkerDirectory = deleter;
            return previous;
        }

        internal Action<Process, string> SwapCompileRequestSenderForTests(Action<Process, string> sender)
        {
            Debug.Assert(sender != null, "sender must not be null");

            Action<Process, string> previous = _sendCompileRequest;
            _sendCompileRequest = sender;
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
            AssertLockHeld();
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
                _deleteWorkerDirectory(workerDirectoryPath);
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

        private void AssertLockHeld()
        {
            Debug.Assert(Monitor.IsEntered(_syncRoot), "Shared worker session lock must be held");
        }
    }
}
