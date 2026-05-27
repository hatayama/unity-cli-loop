using UnityEditor;
using UnityEditor.Compilation;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Threading.Tasks;
using System.Threading;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// A class that asynchronously executes Unity's compilation process and monitors the results.
    /// It handles starting compilation, monitoring its progress, and retrieving the results.
    /// </summary>
    public class CompileController : IDisposable
    {
        private bool _isCompiling = false;
        private List<CompilerMessage> _compileMessages = new();
        private TaskCompletionSource<CompileResult> _currentCompileTask;
        private bool _isForceCompile = false;

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
                        new { force_recompile = forceRecompile });
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

            _isCompiling = true;
            _compileMessages.Clear();
            TaskCompletionSource<CompileResult> compileTask = new();
            _currentCompileTask = compileTask;
            _isForceCompile = forceRecompile;
            bool eventsRegistered = false;
            bool compileTaskTransferred = false;

            try
            {
                // Execute asset refresh.
                VibeLogger.LogInfo(
                    "compile_asset_refresh_start",
                    "Calling AssetDatabase.Refresh before compile.",
                    new { force_recompile = forceRecompile });
                AssetDatabase.Refresh();
                VibeLogger.LogInfo(
                    "compile_asset_refresh_complete",
                    "AssetDatabase.Refresh returned before compile.",
                    new { force_recompile = forceRecompile });

                AssemblyDefinitionConsoleErrorValidationService assemblyDefinitionValidationService = new();
                AssemblyDefinitionConsoleErrorResult assemblyDefinitionErrors =
                    assemblyDefinitionValidationService.FindCurrentErrors();
                if (assemblyDefinitionErrors.HasErrors)
                {
                    CompileResult result = CreateAssemblyDefinitionFailureResult(assemblyDefinitionErrors);
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
                VibeLogger.LogInfo(
                    "compile_event_handlers_registered",
                    "Registered Unity compilation callbacks.",
                    new { force_recompile = forceRecompile });

                string startMessage = forceRecompile ? "Forced recompile started after asset refresh..." : "Compilation started after asset refresh...";
                OnCompileStarted?.Invoke(startMessage);

                VibeLogger.LogInfo(
                    "compile_request_script_compilation",
                    "Requesting Unity script compilation.",
                    new { force_recompile = forceRecompile });
                if (forceRecompile)
                {
                    CompilationPipeline.RequestScriptCompilation(RequestScriptCompilationOptions.CleanBuildCache);
                }
                else
                {
                    CompilationPipeline.RequestScriptCompilation();
                }
                VibeLogger.LogInfo(
                    "compile_request_script_compilation_returned",
                    "Unity script compilation request returned.",
                    new { force_recompile = forceRecompile });

                _ = WatchCompileStartAsync(ct);
                VibeLogger.LogInfo(
                    "compile_controller_waiting_for_finish",
                    "Waiting for Unity compilationFinished callback.",
                    new { force_recompile = forceRecompile });
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
                    compileTask.TrySetCanceled();
                }
            }
        }

        private async Task WatchCompileStartAsync(CancellationToken ct)
        {
            int waitedMs = 0;

            while (waitedMs < UnityCliLoopConstants.COMPILE_START_TIMEOUT_MS)
            {
                if (ct.IsCancellationRequested)
                {
                    AbortCompile("Compilation request was cancelled before it started.");
                    return;
                }

                if (EditorApplication.isCompiling)
                {
                    VibeLogger.LogInfo(
                        "compile_editor_compiling_observed",
                        "EditorApplication.isCompiling became true.",
                        new { waited_ms = waitedMs });
                    return;
                }

                if (_currentCompileTask == null || _currentCompileTask.Task.IsCompleted)
                {
                    return;
                }

                // Do not pass CancellationToken here: this method is fire-and-forget and must not throw.
                // Cancellation is handled by explicit checks above to avoid leaving _currentCompileTask pending.
                await TimerDelay.Wait(UnityCliLoopConstants.COMPILE_START_POLL_INTERVAL_MS);
                waitedMs += UnityCliLoopConstants.COMPILE_START_POLL_INTERVAL_MS;
            }

            AssemblyDefinitionConsoleErrorValidationService assemblyDefinitionValidationService = new();
            AssemblyDefinitionConsoleErrorResult assemblyDefinitionErrors =
                assemblyDefinitionValidationService.FindCurrentErrors();
            if (assemblyDefinitionErrors.HasErrors)
            {
                VibeLogger.LogWarning(
                    "compile_start_timeout_assembly_definition_error",
                    assemblyDefinitionErrors.Message,
                    new { waited_ms = waitedMs });
                AbortCompileWithResult(CreateAssemblyDefinitionFailureResult(assemblyDefinitionErrors));
                return;
            }

            AssemblyDefinitionDuplicationValidationService asmdefValidationService = new();
            ValidationResult asmdefValidation = asmdefValidationService.ValidateNoDuplicateAsmdefNames();
            if (!asmdefValidation.IsValid)
            {
                VibeLogger.LogWarning(
                    "compile_start_timeout_duplicate_asmdef",
                    asmdefValidation.ErrorMessage,
                    new { waited_ms = waitedMs });
                AbortCompile(asmdefValidation.ErrorMessage);
                return;
            }

            VibeLogger.LogWarning(
                "compile_start_timeout",
                "Compilation did not start before the start timeout.",
                new { waited_ms = waitedMs });
            AbortCompile(
                "Compilation did not start. Possible causes: editor update/reload locks, Auto Refresh disabled, or no script changes."
            );
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
                new { force_recompile = _isForceCompile });

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
                new { force_recompile = _isForceCompile });

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

            // Completion subscribers are outside this controller, so state cleanup cannot depend on them returning.
            try
            {
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
                }

                task?.TrySetResult(result);
            }
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
            CompileResult result = CreateCompileResult();
            VibeLogger.LogInfo(
                "compile_finished_event",
                "Unity compilationFinished callback received.",
                new
                {
                    force_recompile = _isForceCompile,
                    success = result.Success,
                    error_count = result.ErrorCount,
                    warning_count = result.WarningCount,
                    message_count = _compileMessages.Count
                });

            CompleteCompileRequest(result, unregisterEvents: true);
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

            VibeLogger.LogInfo(
                "compile_assembly_finished",
                "Unity assemblyCompilationFinished callback received.",
                new
                {
                    assembly_name = assemblyName,
                    message_count = messages.Length,
                    total_message_count = _compileMessages.Count
                });
            OnAssemblyCompiled?.Invoke(assemblyName, messages);
        }

        /// <summary>
        /// Creates the compilation result.
        /// </summary>
        /// <returns>The compilation result.</returns>
        private CompileResult CreateCompileResult()
        {
            int errorCount = _compileMessages.Count(m => m.type == CompilerMessageType.Error);
            int warningCount = _compileMessages.Count(m => m.type == CompilerMessageType.Warning);

            // For force compile, don't include messages in response
            // User should use get-logs tool after domain reload completes
            if (_isForceCompile)
            {
                return new CompileResult(
                    success: null, // Success status is indeterminate during force compile
                    errorCount: errorCount,
                    warningCount: warningCount,
                    completedAt: DateTime.Now,
                    messages: new CompilerMessage[0],
                    errors: new CompilerMessage[0],
                    warnings: new CompilerMessage[0],
                    isIndeterminate: true,
                    message: "Force compilation executed. Use get-logs tool to retrieve compilation messages."
                );
            }

            CompilerMessage[] errors = _compileMessages.Where(m => m.type == CompilerMessageType.Error).ToArray();
            CompilerMessage[] warnings = _compileMessages.Where(m => m.type == CompilerMessageType.Warning).ToArray();

            return new CompileResult(
                success: errorCount == 0,
                errorCount: errorCount,
                warningCount: warningCount,
                completedAt: DateTime.Now,
                messages: _compileMessages.ToArray(),
                errors: errors,
                warnings: warnings
            );
        }

        /// <summary>
        /// Creates a failed compile result from Assembly Definition and Assembly Reference Console errors.
        /// </summary>
        private static CompileResult CreateAssemblyDefinitionFailureResult(
            AssemblyDefinitionConsoleErrorResult assemblyDefinitionErrors)
        {
            CompilerMessage[] errors = CreateAssemblyDefinitionCompilerMessages(assemblyDefinitionErrors.Errors);
            return new CompileResult(
                success: false,
                errorCount: errors.Length,
                warningCount: 0,
                completedAt: DateTime.Now,
                messages: errors,
                errors: errors,
                warnings: Array.Empty<CompilerMessage>(),
                message: assemblyDefinitionErrors.Message
            );
        }

        /// <summary>
        /// Converts Assembly Definition and Assembly Reference Console errors into compiler messages.
        /// </summary>
        private static CompilerMessage[] CreateAssemblyDefinitionCompilerMessages(
            AssemblyDefinitionConsoleError[] assemblyDefinitionErrors)
        {
            CompilerMessage[] messages = new CompilerMessage[assemblyDefinitionErrors.Length];
            for (int i = 0; i < assemblyDefinitionErrors.Length; i++)
            {
                AssemblyDefinitionConsoleError error = assemblyDefinitionErrors[i];
                messages[i] = new CompilerMessage
                {
                    type = CompilerMessageType.Error,
                    message = error.Message,
                    file = error.File,
                    line = error.Line
                };
            }

            return messages;
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

    /// <summary>
    /// A class that represents the result of a compilation.
    /// Includes information on errors, warnings, and the completion time.
    /// </summary>
    public class CompileResult
    {
        /// <summary>
        /// Whether the compilation was successful. Null indicates indeterminate status.
        /// </summary>
        public bool? Success { get; }
        
        /// <summary>
        /// The number of errors.
        /// </summary>
        public int ErrorCount { get; }
        
        /// <summary>
        /// The number of warnings.
        /// </summary>
        public int WarningCount { get; }
        
        /// <summary>
        /// The time of compilation completion.
        /// </summary>
        public DateTime CompletedAt { get; }
        
        /// <summary>
        /// All compiler messages.
        /// </summary>
        public CompilerMessage[] Messages { get; }
        
        /// <summary>
        /// Error messages only.
        /// </summary>
        public CompilerMessage[] Errors { get; }
        
        /// <summary>
        /// Warning messages only.
        /// </summary>
        public CompilerMessage[] Warnings { get; }

        /// <summary>
        /// Whether the compilation result is indeterminate (cannot be determined).
        /// </summary>
        public bool IsIndeterminate { get; }

        /// <summary>
        /// Optional message for additional information
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// Alias for error messages (for backward compatibility).
        /// </summary>
        public CompilerMessage[] error => Errors;
        
        /// <summary>
        /// Alias for warning messages (for backward compatibility).
        /// </summary>
        public CompilerMessage[] warning => Warnings;

        /// <summary>
        /// Initializes the compilation result.
        /// </summary>
        /// <param name="success">The compilation success flag. Null indicates indeterminate status.</param>
        /// <param name="errorCount">The number of errors.</param>
        /// <param name="warningCount">The number of warnings.</param>
        /// <param name="completedAt">The completion time.</param>
        /// <param name="messages">All messages.</param>
        /// <param name="errors">The error messages.</param>
        /// <param name="warnings">The warning messages.</param>
        /// <param name="isIndeterminate">Whether the result is indeterminate.</param>
        public CompileResult(
            bool? success,
            int errorCount,
            int warningCount,
            DateTime completedAt,
            CompilerMessage[] messages,
            CompilerMessage[] errors,
            CompilerMessage[] warnings,
            bool isIndeterminate = false,
            string message = null
        )
        {
            Success = success;
            ErrorCount = errorCount;
            WarningCount = warningCount;
            CompletedAt = completedAt;
            Messages = messages;
            Errors = errors;
            Warnings = warnings;
            IsIndeterminate = isIndeterminate;
            Message = message;
        }
    }
}
