using UnityEditor;
using UnityEditor.Compilation;
using System.Collections.Generic;
using System;
using System.Threading.Tasks;
using System.Threading;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// A class that asynchronously executes Unity's compilation process and monitors the results.
    /// It handles starting compilation, monitoring its progress, and retrieving the results.
    /// </summary>
    public class CompileController : IDisposable
    {
        private readonly ICompileResultSessionRepository _compileResultSessionRepository;
        private readonly IPendingCompileSessionRepository _pendingCompileSessionRepository;
        private bool _isCompiling = false;
        private List<CompilerMessage> _compileMessages = new();
        private TaskCompletionSource<CompileResult> _currentCompileTask;
        private bool _isForceCompile = false;
        private bool _reloadExternalSceneChanges = true;
        private CompileResultRecordingContext _resultRecordingContext = CompileResultRecordingContext.Disabled();
        private DateTime _compileStartedAtUtc = DateTime.MinValue;

        public CompileController(
            ICompileResultSessionRepository compileResultSessionRepository,
            IPendingCompileSessionRepository pendingCompileSessionRepository)
        {
            UnityEngine.Debug.Assert(compileResultSessionRepository != null, "compileResultSessionRepository must not be null");
            UnityEngine.Debug.Assert(pendingCompileSessionRepository != null, "pendingCompileSessionRepository must not be null");

            _compileResultSessionRepository = compileResultSessionRepository ??
                throw new ArgumentNullException(nameof(compileResultSessionRepository));
            _pendingCompileSessionRepository = pendingCompileSessionRepository ??
                throw new ArgumentNullException(nameof(pendingCompileSessionRepository));
        }

        /// <summary>
        /// Event that occurs when compilation is complete.
        /// </summary>
        public event Action<CompileResult> OnCompileCompleted;
        
        /// <summary>
        /// Event that occurs when compilation starts.
        /// </summary>
        public event Action<string> OnCompileStarted;
        
        /// <summary>
        /// Event that occurs when assembly compilation is complete.
        /// </summary>
        public event Action<string, CompilerMessage[]> OnAssemblyCompiled;

        /// <summary>
        /// Gets whether a compilation is currently in progress.
        /// </summary>
        public bool IsCompiling => _isCompiling;
        
        /// <summary>
        /// Gets the current list of compiler messages.
        /// </summary>
        public IReadOnlyList<CompilerMessage> CompileMessages => _compileMessages.AsReadOnly();

        /// <summary>
        /// Sets delayed compile result storage for the active CLI request.
        /// </summary>
        internal void SetResultRecordingContext(CompileResultRecordingContext resultRecordingContext)
        {
            _resultRecordingContext = resultRecordingContext;
        }

        /// <summary>
        /// Sets how compile handles open Scene files changed outside Unity before asset refresh.
        /// </summary>
        internal void SetExternalSceneChangePolicy(bool reloadExternalSceneChanges)
        {
            _reloadExternalSceneChanges = reloadExternalSceneChanges;
        }

        /// <summary>
        /// Executes compilation asynchronously.
        /// </summary>
        /// <param name="forceRecompile">Whether to force a recompile.</param>
        /// <returns>The compilation result.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the task is not found during compilation.</exception>
        public async Task<CompileResult> TryCompileAsync(bool forceRecompile = false)
        {
            return await TryCompileAsync(forceRecompile, CancellationToken.None);
        }

        public async Task<CompileResult> TryCompileAsync(bool forceRecompile, CancellationToken ct)
        {
            if (_isCompiling)
            {
                // If compilation is already in progress, wait for the current task.
                if (_currentCompileTask != null)
                {
                    VibeLogger.LogInfo(
                        "compile_controller_wait_existing_task",
                        "Compilation is already in progress; waiting for the existing task.",
                        BuildCompileControllerStateContext(new Dictionary<string, object>
                        {
                            ["requested_force_recompile"] = forceRecompile
                        }));
                    return await _currentCompileTask.Task;
                }
                throw new InvalidOperationException("Compilation is in progress, but the task could not be found.");
            }

            CompilationStateValidationService validationService = new();
            ValidationResult validation = validationService.ValidateCompilationState();
            if (!validation.IsValid)
            {
                VibeLogger.LogWarning(
                    "compile_controller_validation_failed",
                    validation.ErrorMessage,
                    new { force_recompile = forceRecompile });
                return new CompileResult(
                    success: false,
                    errorCount: 0,
                    warningCount: 0,
                    completedAt: DateTime.Now,
                    messages: new CompilerMessage[0],
                    errors: new CompilerMessage[0],
                    warnings: new CompilerMessage[0],
                    isIndeterminate: true,
                    message: validation.ErrorMessage
                );
            }

            (bool CanProceed, string Message, string[] ScenePaths) sceneChangeResult =
                ExternalSceneChangeTracker.ResolveForCompile(_reloadExternalSceneChanges);
            if (!sceneChangeResult.CanProceed)
            {
                VibeLogger.LogWarning(
                    "compile_external_scene_change_resolution_failed",
                    sceneChangeResult.Message,
                    new
                    {
                        reload_external_scene_changes = _reloadExternalSceneChanges,
                        scene_paths = sceneChangeResult.ScenePaths
                    });
                return CompileResultFactory.CreateExternalSceneChangeFailureResult(sceneChangeResult);
            }

            _isCompiling = true;
            _compileMessages.Clear();
            _compileStartedAtUtc = DateTime.UtcNow;
            TaskCompletionSource<CompileResult> compileTask = new();
            _currentCompileTask = compileTask;
            _isForceCompile = forceRecompile;
            bool eventsRegistered = false;
            bool compileTaskTransferred = false;

            try
            {
                // Execute asset refresh.
                AssetDatabase.Refresh();

                AssemblyDefinitionConsoleErrorValidationService assemblyDefinitionValidationService = new();
                AssemblyDefinitionConsoleErrorResult assemblyDefinitionErrors =
                    assemblyDefinitionValidationService.FindCurrentErrors();
                if (assemblyDefinitionErrors.HasErrors)
                {
                    CompileResult result =
                        CompileResultFactory.CreateAssemblyDefinitionFailureResult(assemblyDefinitionErrors);
                    VibeLogger.LogWarning(
                        "compile_asset_refresh_assembly_definition_error",
                        assemblyDefinitionErrors.Message,
                        new
                        {
                            force_recompile = forceRecompile,
                            error_count = assemblyDefinitionErrors.Errors.Length
                        });
                    CompleteCompileWithoutRequest(result);
                    return result;
                }

                // Register events.
                CompilationPipeline.compilationFinished += HandleCompileFinished;
                CompilationPipeline.assemblyCompilationFinished += HandleAssemblyFinished;
                eventsRegistered = true;

                string startMessage = forceRecompile ? "Forced recompile started after asset refresh..." : "Compilation started after asset refresh...";
                OnCompileStarted?.Invoke(startMessage);

                if (forceRecompile)
                {
                    CompilationPipeline.RequestScriptCompilation(RequestScriptCompilationOptions.CleanBuildCache);
                }
                else
                {
                    CompilationPipeline.RequestScriptCompilation();
                }

                StartCompileLifecycleWatchdog(compileTask, ct);
                compileTaskTransferred = true;
                return await compileTask.Task;
            }
            finally
            {
                if (!compileTaskTransferred &&
                    ReferenceEquals(_currentCompileTask, compileTask) &&
                    !compileTask.Task.IsCompleted)
                {
                    if (eventsRegistered)
                    {
                        UnregisterCompilationEvents();
                    }

                    _currentCompileTask = null;
                    _isCompiling = false;
                    _isForceCompile = false;
                    _compileStartedAtUtc = DateTime.MinValue;
                    compileTask.TrySetCanceled();
                }
            }
        }

        private void StartCompileLifecycleWatchdog(TaskCompletionSource<CompileResult> compileTask, CancellationToken ct)
        {
            UnityEngine.Debug.Assert(compileTask != null, "compileTask must not be null");

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
                () => EditorApplication.isCompiling,
                IsCompileRequestCompleted,
                WaitForCompileWatchdogPollAsync,
                _ => { },
                HandleCompileStartTimeout,
                HandleCompileStoppedWithoutFinishEvent,
                AbortCompile);
            return watchdog.WatchAsync(ct);
        }

        private bool IsCompileRequestCompleted()
        {
            return _currentCompileTask == null || _currentCompileTask.Task.IsCompleted;
        }

        private static Task WaitForCompileWatchdogPollAsync()
        {
            return TimerDelay.Wait(UnityCliLoopConstants.COMPILE_START_POLL_INTERVAL_MS);
        }

        private void HandleCompileLifecycleWatchdogFault(
            TaskCompletionSource<CompileResult> compileTask,
            Task faultedTask)
        {
            UnityEngine.Debug.Assert(compileTask != null, "compileTask must not be null");
            UnityEngine.Debug.Assert(faultedTask != null, "faultedTask must not be null");
            UnityEngine.Debug.Assert(faultedTask.IsFaulted, "faultedTask must be faulted");

            if (!IsCurrentCompileRequest(_currentCompileTask, compileTask))
            {
                return;
            }

            Exception exception = faultedTask.Exception;
            UnityEngine.Debug.Assert(exception != null, "faultedTask exception must not be null");
            if (exception != null)
            {
                UnityEngine.Debug.LogException(exception);
            }

            EditorApplication.delayCall += () => AbortCompileAfterWatchdogFault(compileTask);
        }

        private void AbortCompileAfterWatchdogFault(TaskCompletionSource<CompileResult> compileTask)
        {
            UnityEngine.Debug.Assert(compileTask != null, "compileTask must not be null");

            if (!IsCurrentCompileRequest(_currentCompileTask, compileTask))
            {
                return;
            }

            AbortCompile("Compilation watchdog failed unexpectedly.");
        }

        internal static bool IsCurrentCompileRequest(
            TaskCompletionSource<CompileResult> currentCompileTask,
            TaskCompletionSource<CompileResult> compileTask)
        {
            UnityEngine.Debug.Assert(compileTask != null, "compileTask must not be null");
            return currentCompileTask != null && ReferenceEquals(currentCompileTask, compileTask);
        }

        private Dictionary<string, object> BuildCompileControllerStateContext(
            Dictionary<string, object> extraContext)
        {
            Dictionary<string, object> context = new()
            {
                ["force_recompile"] = _isForceCompile,
                ["controller_compiling"] = _isCompiling,
                ["current_task_present"] = _currentCompileTask != null,
                ["current_task_completed"] = _currentCompileTask != null && _currentCompileTask.Task.IsCompleted,
                ["message_count"] = _compileMessages != null ? _compileMessages.Count : 0,
                ["editor_compiling"] = EditorApplication.isCompiling,
                ["editor_updating"] = EditorApplication.isUpdating,
                ["editor_playing"] = EditorApplication.isPlaying,
                ["editor_paused"] = EditorApplication.isPaused,
                ["reload_external_scene_changes"] = _reloadExternalSceneChanges
            };
            if (extraContext == null)
            {
                return context;
            }

            foreach (KeyValuePair<string, object> entry in extraContext)
            {
                context[entry.Key] = entry.Value;
            }

            return context;
        }

        private void HandleCompileStartTimeout(int waitedMs)
        {
            AssemblyDefinitionConsoleErrorValidationService assemblyDefinitionValidationService = new();
            AssemblyDefinitionConsoleErrorResult assemblyDefinitionErrors =
                assemblyDefinitionValidationService.FindCurrentErrors();
            if (assemblyDefinitionErrors.HasErrors)
            {
                VibeLogger.LogWarning(
                    "compile_start_timeout_assembly_definition_error",
                    assemblyDefinitionErrors.Message,
                    BuildCompileControllerStateContext(new Dictionary<string, object>
                    {
                        ["waited_ms"] = waitedMs
                    }));
                AbortCompileWithResult(
                    CompileResultFactory.CreateAssemblyDefinitionFailureResult(assemblyDefinitionErrors));
                return;
            }

            AssemblyDefinitionDuplicationValidationService asmdefValidationService = new();
            ValidationResult asmdefValidation = asmdefValidationService.ValidateNoDuplicateAsmdefNames();
            if (!asmdefValidation.IsValid)
            {
                VibeLogger.LogWarning(
                    "compile_start_timeout_duplicate_asmdef",
                    asmdefValidation.ErrorMessage,
                    BuildCompileControllerStateContext(new Dictionary<string, object>
                    {
                        ["waited_ms"] = waitedMs
                    }));
                AbortCompile(asmdefValidation.ErrorMessage);
                return;
            }

            VibeLogger.LogWarning(
                "compile_start_timeout",
                "Compilation did not start before the start timeout.",
                BuildCompileControllerStateContext(new Dictionary<string, object>
                {
                    ["waited_ms"] = waitedMs
                }));
            AbortCompile(
                "Compilation did not start. Possible causes: editor update/reload locks, Auto Refresh disabled, or no script changes."
            );
        }

        private void HandleCompileStoppedWithoutFinishEvent(int stoppedMs)
        {
            string message =
                "Unity stopped compiling before Unity CLI Loop received the compilationFinished callback. " +
                "The compile result is indeterminate; use get-logs to inspect the compiler output.";
            AssemblyDefinitionConsoleErrorValidationService assemblyDefinitionValidationService = new();
            AssemblyDefinitionConsoleErrorResult assemblyDefinitionErrors =
                assemblyDefinitionValidationService.FindCurrentErrors();
            CompileResult result = CompileResultFactory.CreateStoppedWithoutFinishResult(
                assemblyDefinitionErrors,
                _compileMessages.ToArray(),
                _isForceCompile,
                message);
            VibeLogger.LogWarning(
                "compile_finish_callback_missing",
                result.Message ?? message,
                new
                {
                    force_recompile = _isForceCompile,
                    stopped_ms = stoppedMs,
                    message_count = _compileMessages.Count,
                    assembly_definition_error_count = assemblyDefinitionErrors.Errors.Length,
                    editor_compiling = EditorApplication.isCompiling,
                    editor_updating = EditorApplication.isUpdating,
                    editor_playing = EditorApplication.isPlaying,
                    editor_paused = EditorApplication.isPaused
                });
            AbortCompileWithResult(result);
        }

        /// <summary>
        /// Completes an active compile request with a prepared failure result before Unity reports compilationFinished.
        /// </summary>
        private void AbortCompileWithResult(CompileResult result)
        {
            if (_currentCompileTask == null || _currentCompileTask.Task.IsCompleted)
            {
                return;
            }

            VibeLogger.LogWarning(
                "compile_aborted",
                result.Message,
                BuildCompileControllerStateContext(new Dictionary<string, object>
                {
                    ["success"] = result.Success,
                    ["error_count"] = result.ErrorCount,
                    ["warning_count"] = result.WarningCount,
                    ["is_indeterminate"] = result.IsIndeterminate
                }));

            CompleteCompileRequest(result, unregisterEvents: true);
        }

        /// <summary>
        /// Completes a compile request that stopped before RequestScriptCompilation was called.
        /// </summary>
        private void CompleteCompileWithoutRequest(CompileResult result)
        {
            CompleteCompileRequest(result, unregisterEvents: false);
        }

        private void AbortCompile(string reason)
        {
            if (_currentCompileTask == null || _currentCompileTask.Task.IsCompleted)
            {
                return;
            }

            VibeLogger.LogWarning(
                "compile_aborted",
                reason,
                BuildCompileControllerStateContext(new Dictionary<string, object>
                {
                    ["reason"] = reason
                }));

            CompileResult result = new(
                success: false,
                errorCount: 0,
                warningCount: 0,
                completedAt: DateTime.Now,
                messages: new CompilerMessage[0],
                errors: new CompilerMessage[0],
                warnings: new CompilerMessage[0],
                isIndeterminate: true,
                message: reason
            );

            CompleteCompileRequest(result, unregisterEvents: true);
        }

        /// <summary>
        /// Completes the active compile task while guaranteeing controller state cleanup.
        /// </summary>
        private void CompleteCompileRequest(CompileResult result, bool unregisterEvents)
        {
            UnityEngine.Debug.Assert(result != null, "result must not be null");

            TaskCompletionSource<CompileResult> task = _currentCompileTask;
            Dictionary<string, object> completionContext = BuildCompileControllerStateContext(
                new Dictionary<string, object>
                {
                    ["unregister_events"] = unregisterEvents,
                    ["success"] = result.Success,
                    ["error_count"] = result.ErrorCount,
                    ["warning_count"] = result.WarningCount,
                    ["is_indeterminate"] = result.IsIndeterminate
                });
            // Completion subscribers are outside this controller, so state cleanup cannot depend on them returning.
            try
            {
                RecordCompileResultIfNeeded(result, completionContext);

                if (unregisterEvents)
                {
                    UnregisterCompilationEvents();
                }

                _isCompiling = false;
                _isForceCompile = false;
                OnCompileCompleted?.Invoke(result);
            }
            finally
            {
                if (ReferenceEquals(_currentCompileTask, task))
                {
                    _currentCompileTask = null;
                    _isCompiling = false;
                    _isForceCompile = false;
                    _resultRecordingContext = CompileResultRecordingContext.Disabled();
                    _compileStartedAtUtc = DateTime.MinValue;
                }

                task?.TrySetResult(result);
            }
        }

        private void RecordCompileResultIfNeeded(
            CompileResult result,
            Dictionary<string, object> completionContext)
        {
            UnityEngine.Debug.Assert(result != null, "result must not be null");
            UnityEngine.Debug.Assert(completionContext != null, "completionContext must not be null");

            if (!_resultRecordingContext.Enabled)
            {
                return;
            }

            CompileResponse response =
                CompileResponseFactory.CreateCompileResult(result, _resultRecordingContext.ForceRecompile);
            CompileSessionResultStore.StoreCompileResult(
                _compileResultSessionRepository,
                _pendingCompileSessionRepository,
                _resultRecordingContext.RequestId,
                _resultRecordingContext.ForceRecompile,
                response,
                _resultRecordingContext.RequestId);
            completionContext["result_recorded_in_session_state"] = true;
            completionContext["request_id"] = _resultRecordingContext.RequestId;
        }

        /// <summary>
        /// Removes Unity compilation callbacks for the current compile request.
        /// </summary>
        private void UnregisterCompilationEvents()
        {
            CompilationPipeline.compilationFinished -= HandleCompileFinished;
            CompilationPipeline.assemblyCompilationFinished -= HandleAssemblyFinished;
        }

        /// <summary>
        /// Clears the compiler messages.
        /// </summary>
        public void ClearMessages()
        {
            _compileMessages.Clear();
        }

        /// <summary>
        /// Handler for when compilation is complete.
        /// </summary>
        /// <param name="context">The compilation context.</param>
        private void HandleCompileFinished(object context)
        {
            CompileResult result = CompileResultFactory.CreateCompileResult(_compileMessages.ToArray(), _isForceCompile);
            LogCompileFinishCallbackReceived(result);
            CompleteCompileRequest(result, unregisterEvents: true);
        }

        private void LogCompileFinishCallbackReceived(CompileResult result)
        {
            UnityEngine.Debug.Assert(result != null, "result must not be null");

            string requestId = _resultRecordingContext.Enabled
                ? _resultRecordingContext.RequestId
                : "";
            VibeLogger.LogInfo(
                "compile_finish_callback_received",
                "Unity compilationFinished callback was received.",
                new
                {
                    request_id = requestId,
                    success = result.Success,
                    error_count = result.ErrorCount,
                    warning_count = result.WarningCount,
                    is_indeterminate = result.IsIndeterminate,
                    elapsed_ms = CompileElapsedMilliseconds()
                },
                requestId);
        }

        private long CompileElapsedMilliseconds()
        {
            if (_compileStartedAtUtc == DateTime.MinValue)
            {
                return 0;
            }

            return (long)(DateTime.UtcNow - _compileStartedAtUtc).TotalMilliseconds;
        }

        /// <summary>
        /// Handler for when assembly compilation is complete.
        /// </summary>
        /// <param name="asmPath">The assembly path.</param>
        /// <param name="messages">The compiler messages.</param>
        private void HandleAssemblyFinished(string asmPath, CompilerMessage[] messages)
        {
            string assemblyName = System.IO.Path.GetFileName(asmPath);

            foreach (CompilerMessage message in messages)
            {
                _compileMessages.Add(message);
            }

            OnAssemblyCompiled?.Invoke(assemblyName, messages);
        }

        /// <summary>
        /// Cleans up resources.
        /// </summary>
        public void Cleanup()
        {
            // Unregister events just in case.
            CompilationPipeline.compilationFinished -= HandleCompileFinished;
            CompilationPipeline.assemblyCompilationFinished -= HandleAssemblyFinished;

            // If there is an incomplete task, cancel it.
            if (_currentCompileTask != null && !_currentCompileTask.Task.IsCompleted)
            {
                _currentCompileTask.SetCanceled();
                _currentCompileTask = null;
            }
            _isCompiling = false;
            _isForceCompile = false;
            _reloadExternalSceneChanges = true;
            _resultRecordingContext = CompileResultRecordingContext.Disabled();
            _compileStartedAtUtc = DateTime.MinValue;
        }

        /// <summary>
        /// Releases resources.
        /// </summary>
        public void Dispose()
        {
            Cleanup();
            _compileMessages?.Clear();
            _compileMessages = null;

            // Clear all events.
            OnCompileCompleted = null;
            OnCompileStarted = null;
            OnAssemblyCompiled = null;
        }
    }
}
