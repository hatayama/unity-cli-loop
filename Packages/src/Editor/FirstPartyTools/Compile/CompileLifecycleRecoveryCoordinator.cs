using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Decides and triggers recovery actions when CompileLifecycleWatchdog observes a stalled or
    /// faulted compile request. Assembly Definition validation, message building, and abort actions
    /// are injected so these recovery decisions can be pinned with tests without running Unity compilation.
    /// Why one Console snapshot per recovery: asmdef validation and the indeterminate summary both read
    /// the Console, and scanning it twice on the main thread doubles recovery work for nothing.
    /// </summary>
    internal sealed class CompileLifecycleRecoveryCoordinator
    {
        private readonly Func<bool> _isEditorCompiling;
        private readonly Func<bool> _isRequestCompleted;
        private readonly Func<bool> _hasCompilationStarted;
        private readonly Func<TaskCompletionSource<CompileResult>> _getCurrentCompileTask;
        private readonly Func<UnityCliLoopConsoleLogEntry[], AssemblyDefinitionConsoleErrorResult> _findAssemblyDefinitionErrors;
        private readonly Func<UnityCliLoopConsoleLogEntry[]> _getConsoleErrorEntries;
        private readonly Func<int> _getConsoleErrorCountAtCompileStart;
        private readonly Func<ValidationResult> _validateNoDuplicateAsmdefNames;
        private readonly Func<bool> _getIsForceCompile;
        private readonly Func<CompilerMessage[]> _getCompileMessages;
        private readonly Func<int> _getAssemblyFinishedCount;
        private readonly Func<double> _getMonotonicSeconds;
        private readonly Action<int> _onCompileStartPending;
        private readonly Func<Dictionary<string, object>, Dictionary<string, object>> _buildStateContext;
        private readonly Action<CompileResult> _abortWithResult;
        private readonly Action<string> _abort;

        internal CompileLifecycleRecoveryCoordinator(
            Func<bool> isEditorCompiling,
            Func<bool> isRequestCompleted,
            Func<bool> hasCompilationStarted,
            Func<TaskCompletionSource<CompileResult>> getCurrentCompileTask,
            Func<UnityCliLoopConsoleLogEntry[], AssemblyDefinitionConsoleErrorResult> findAssemblyDefinitionErrors,
            Func<UnityCliLoopConsoleLogEntry[]> getConsoleErrorEntries,
            Func<int> getConsoleErrorCountAtCompileStart,
            Func<ValidationResult> validateNoDuplicateAsmdefNames,
            Func<bool> getIsForceCompile,
            Func<CompilerMessage[]> getCompileMessages,
            Func<int> getAssemblyFinishedCount,
            Func<double> getMonotonicSeconds,
            Action<int> onCompileStartPending,
            Func<Dictionary<string, object>, Dictionary<string, object>> buildStateContext,
            Action<CompileResult> abortWithResult,
            Action<string> abort)
        {
            _isEditorCompiling = Required(isEditorCompiling, nameof(isEditorCompiling));
            _isRequestCompleted = Required(isRequestCompleted, nameof(isRequestCompleted));
            _hasCompilationStarted = Required(hasCompilationStarted, nameof(hasCompilationStarted));
            _getCurrentCompileTask = Required(getCurrentCompileTask, nameof(getCurrentCompileTask));
            _findAssemblyDefinitionErrors = Required(findAssemblyDefinitionErrors, nameof(findAssemblyDefinitionErrors));
            _getConsoleErrorEntries = Required(getConsoleErrorEntries, nameof(getConsoleErrorEntries));
            _getConsoleErrorCountAtCompileStart =
                Required(getConsoleErrorCountAtCompileStart, nameof(getConsoleErrorCountAtCompileStart));
            _validateNoDuplicateAsmdefNames =
                Required(validateNoDuplicateAsmdefNames, nameof(validateNoDuplicateAsmdefNames));
            _getIsForceCompile = Required(getIsForceCompile, nameof(getIsForceCompile));
            _getCompileMessages = Required(getCompileMessages, nameof(getCompileMessages));
            _getAssemblyFinishedCount = Required(getAssemblyFinishedCount, nameof(getAssemblyFinishedCount));
            _getMonotonicSeconds = Required(getMonotonicSeconds, nameof(getMonotonicSeconds));
            _onCompileStartPending = Required(onCompileStartPending, nameof(onCompileStartPending));
            _buildStateContext = Required(buildStateContext, nameof(buildStateContext));
            _abortWithResult = Required(abortWithResult, nameof(abortWithResult));
            _abort = Required(abort, nameof(abort));
        }

        /// <summary>
        /// Returns the injected dependency after asserting and enforcing that it is not null.
        /// Why one guard for every injected delegate: each inline `?? throw` is a branch of the
        /// constructor, and sixteen of them put it over the repository's cyclomatic complexity limit.
        /// </summary>
        private static T Required<T>(T dependency, string parameterName) where T : class
        {
            Debug.Assert(dependency != null, $"{parameterName} must not be null");
            return dependency ?? throw new ArgumentNullException(parameterName);
        }

        /// <summary>
        /// Starts watching one compile request and wires fault recovery for it.
        /// </summary>
        internal void StartWatchdog(TaskCompletionSource<CompileResult> compileTask, CancellationToken ct)
        {
            Debug.Assert(compileTask != null, "compileTask must not be null");

            Task watchdogTask = WatchCompileLifecycleAsync(ct);
            _ = watchdogTask.ContinueWith(
                faultedTask => HandleCompileLifecycleWatchdogFault(compileTask, faultedTask),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }

        private Task WatchCompileLifecycleAsync(CancellationToken ct)
        {
            CompileLifecycleWatchdog watchdog = new CompileLifecycleWatchdog(
                _isEditorCompiling,
                _isRequestCompleted,
                WaitForCompileWatchdogPollAsync,
                _hasCompilationStarted,
                _ => { },
                HandleCompileStartTimeout,
                _onCompileStartPending,
                HandleCompileStartPendingTimeout,
                HandleCompileStoppedWithoutFinishEvent,
                _abort,
                _getAssemblyFinishedCount,
                _getMonotonicSeconds,
                HandleAssemblyProgressStalled);
            return watchdog.WatchAsync(ct);
        }

        /// <summary>
        /// Recovers from Unity accepting the compile request (isCompiling stayed true) without ever raising
        /// compilationStarted. The compile is still queued inside the Editor, so the result is indeterminate
        /// and carries the Editor state that explains why Unity is withholding it.
        /// </summary>
        internal void HandleCompileStartPendingTimeout(int pendingMs)
        {
            CompileStartPendingDiagnostics diagnostics = CompileStartPendingDiagnostics.Capture(pendingMs);
            CompileResult result = CompileResultFactory.CreateNotStartedResult(diagnostics);
            VibeLogger.LogWarning(
                "compile_start_pending_timeout",
                result.Message,
                _buildStateContext(diagnostics.ToLogContext()));
            _abortWithResult(result);
        }

        /// <summary>
        /// Records a warning when assembly callbacks stall while Unity still reports compiling.
        /// Why not abort: assembly progress can pause for a valid long compile, and aborting
        /// would create false-positive compile failures.
        /// </summary>
        internal void HandleAssemblyProgressStalled(int stalledMs)
        {
            CompilerMessage[] compileMessages = _getCompileMessages();
            VibeLogger.LogWarning(
                "compile_assembly_progress_stalled",
                "Assembly compilation progress stalled while Unity still reports compiling.",
                new
                {
                    stalled_ms = stalledMs,
                    assembly_finished_count = _getAssemblyFinishedCount(),
                    message_count = compileMessages.Length,
                    editor_compiling = EditorApplication.isCompiling,
                    editor_updating = EditorApplication.isUpdating
                });
        }

        private static Task WaitForCompileWatchdogPollAsync()
        {
            return TimerDelay.Wait(UnityCliLoopConstants.COMPILE_START_POLL_INTERVAL_MS);
        }

        private void HandleCompileLifecycleWatchdogFault(
            TaskCompletionSource<CompileResult> compileTask,
            Task faultedTask)
        {
            Debug.Assert(compileTask != null, "compileTask must not be null");
            Debug.Assert(faultedTask != null, "faultedTask must not be null");
            Debug.Assert(faultedTask.IsFaulted, "faultedTask must be faulted");

            if (!IsCurrentCompileRequest(_getCurrentCompileTask(), compileTask))
            {
                return;
            }

            Exception exception = faultedTask.Exception;
            Debug.Assert(exception != null, "faultedTask exception must not be null");
            if (exception != null)
            {
                Debug.LogException(exception);
            }

            // The fault continuation runs on the thread pool; the dispatcher hands the abort to the main
            // thread and wakes the Editor with SignalTick, even in sessions where delayCall stops flushing.
            MainThreadSwitcher.AddContinuation(() => AbortCompileAfterWatchdogFault(compileTask));
        }

        /// <summary>
        /// Aborts a compile request after its watchdog faulted, unless a newer request has replaced it.
        /// </summary>
        internal void AbortCompileAfterWatchdogFault(TaskCompletionSource<CompileResult> compileTask)
        {
            Debug.Assert(compileTask != null, "compileTask must not be null");

            if (!IsCurrentCompileRequest(_getCurrentCompileTask(), compileTask))
            {
                return;
            }

            _abort("Compilation watchdog failed unexpectedly.");
        }

        internal static bool IsCurrentCompileRequest(
            TaskCompletionSource<CompileResult> currentCompileTask,
            TaskCompletionSource<CompileResult> compileTask)
        {
            Debug.Assert(compileTask != null, "compileTask must not be null");
            return currentCompileTask != null && ReferenceEquals(currentCompileTask, compileTask);
        }

        /// <summary>
        /// Recovers from Unity never starting compilation before the watchdog's start timeout.
        /// </summary>
        internal void HandleCompileStartTimeout(int waitedMs)
        {
            AssemblyDefinitionConsoleErrorResult assemblyDefinitionErrors =
                _findAssemblyDefinitionErrors(_getConsoleErrorEntries());
            if (assemblyDefinitionErrors.HasErrors)
            {
                VibeLogger.LogWarning(
                    "compile_start_timeout_assembly_definition_error",
                    assemblyDefinitionErrors.Message,
                    _buildStateContext(new Dictionary<string, object>
                    {
                        ["waited_ms"] = waitedMs
                    }));
                _abortWithResult(CompileResultFactory.CreateAssemblyDefinitionFailureResult(assemblyDefinitionErrors));
                return;
            }

            ValidationResult asmdefValidation = _validateNoDuplicateAsmdefNames();
            if (!asmdefValidation.IsValid)
            {
                VibeLogger.LogWarning(
                    "compile_start_timeout_duplicate_asmdef",
                    asmdefValidation.ErrorMessage,
                    _buildStateContext(new Dictionary<string, object>
                    {
                        ["waited_ms"] = waitedMs
                    }));
                _abort(asmdefValidation.ErrorMessage);
                return;
            }

            VibeLogger.LogWarning(
                "compile_start_timeout",
                "Compilation did not start before the start timeout.",
                _buildStateContext(new Dictionary<string, object>
                {
                    ["waited_ms"] = waitedMs
                }));
            _abort(
                "Compilation did not start. Possible causes: editor update/reload locks, Auto Refresh disabled, or no script changes."
            );
        }

        /// <summary>
        /// Recovers from Unity stopping compilation without firing the compilationFinished callback.
        /// </summary>
        internal void HandleCompileStoppedWithoutFinishEvent(int stoppedMs)
        {
            string message =
                "Unity stopped compiling before " + UnityCliLoopConstants.DISPLAY_NAME + " received the compilationFinished callback. " +
                "The compile result is indeterminate; use get-logs to inspect the compiler output.";
            // Why append the Console errors: the indeterminate result otherwise costs a second
            // get-logs round trip to see the asmdef or compiler error that aborted the compile.
            // Why only entries after the compile-start boundary: older Console errors belong to
            // earlier sessions and would be misread as this compile's cause.
            UnityCliLoopConsoleLogEntry[] consoleErrorEntries = _getConsoleErrorEntries();
            string consoleErrorSummary = CompileIndeterminateErrorSummaryBuilder.Build(
                CompileIndeterminateErrorSummaryBuilder.TakeEntriesAfter(
                    consoleErrorEntries,
                    _getConsoleErrorCountAtCompileStart()));
            if (consoleErrorSummary != null)
            {
                message = message + "\n" + consoleErrorSummary;
            }

            AssemblyDefinitionConsoleErrorResult assemblyDefinitionErrors =
                _findAssemblyDefinitionErrors(consoleErrorEntries);
            CompilerMessage[] compileMessages = _getCompileMessages();
            bool isForceCompile = _getIsForceCompile();
            CompileResult result = CompileResultFactory.CreateStoppedWithoutFinishResult(
                assemblyDefinitionErrors,
                compileMessages,
                isForceCompile,
                message);
            VibeLogger.LogWarning(
                "compile_finish_callback_missing",
                result.Message ?? message,
                new
                {
                    force_recompile = isForceCompile,
                    stopped_ms = stoppedMs,
                    message_count = compileMessages.Length,
                    assembly_definition_error_count = assemblyDefinitionErrors.Errors.Length,
                    editor_compiling = EditorApplication.isCompiling,
                    editor_updating = EditorApplication.isUpdating,
                    editor_playing = EditorApplication.isPlaying,
                    editor_paused = EditorApplication.isPaused
                });
            _abortWithResult(result);
        }
    }
}
