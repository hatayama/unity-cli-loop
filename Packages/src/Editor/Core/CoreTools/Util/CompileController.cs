using UnityEditor;
using UnityEditor.Compilation;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Threading.Tasks;
using System.Threading;

namespace io.github.hatayama.uLoopMCP
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
                    return await _currentCompileTask.Task;
                }
                throw new InvalidOperationException("Compilation is in progress, but the task could not be found.");
            }

            CompilationStateValidationService validationService = new();
            ValidationResult validation = validationService.ValidateCompilationState();
            if (!validation.IsValid)
            {
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
            _currentCompileTask = new TaskCompletionSource<CompileResult>();
            _isForceCompile = forceRecompile;

            // Execute asset refresh.
            AssetDatabase.Refresh();

            // Register events.
            CompilationPipeline.compilationFinished += HandleCompileFinished;
            CompilationPipeline.assemblyCompilationFinished += HandleAssemblyFinished;

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

            _ = WatchCompileStartAsync(ct);
            return await _currentCompileTask.Task;
        }

        private async Task WatchCompileStartAsync(CancellationToken ct)
        {
            int waitedMs = 0;

            while (waitedMs < McpConstants.COMPILE_START_TIMEOUT_MS)
            {
                if (ct.IsCancellationRequested)
                {
                    AbortCompile("Compilation request was cancelled before it started.");
                    return;
                }

                if (EditorApplication.isCompiling)
                {
                    return;
                }

                if (_currentCompileTask == null || _currentCompileTask.Task.IsCompleted)
                {
                    return;
                }

                // Do not pass CancellationToken here: this method is fire-and-forget and must not throw.
                // Cancellation is handled by explicit checks above to avoid leaving _currentCompileTask pending.
                await TimerDelay.Wait(McpConstants.COMPILE_START_POLL_INTERVAL_MS);
                waitedMs += McpConstants.COMPILE_START_POLL_INTERVAL_MS;
            }

            AssemblyDefinitionDuplicationValidationService asmdefValidationService = new();
            ValidationResult asmdefValidation = asmdefValidationService.ValidateNoDuplicateAsmdefNames();
            if (!asmdefValidation.IsValid)
            {
                AbortCompile(asmdefValidation.ErrorMessage);
                return;
            }

            AbortCompile(
                "Compilation did not start. Possible causes: editor update/reload locks, Auto Refresh disabled, or no script changes."
            );
        }

        private void AbortCompile(string reason)
        {
            if (_currentCompileTask == null || _currentCompileTask.Task.IsCompleted)
            {
                return;
            }

            // Unregister events.
            CompilationPipeline.compilationFinished -= HandleCompileFinished;
            CompilationPipeline.assemblyCompilationFinished -= HandleAssemblyFinished;

            _isCompiling = false;

            CompileResult result = new CompileResult(
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

            OnCompileCompleted?.Invoke(result);

            TaskCompletionSource<CompileResult> task = _currentCompileTask;
            _currentCompileTask = null;
            task.SetResult(result);
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
            // Unregister events.
            CompilationPipeline.compilationFinished -= HandleCompileFinished;
            CompilationPipeline.assemblyCompilationFinished -= HandleAssemblyFinished;

            _isCompiling = false;

            CompileResult result = CreateCompileResult();
            OnCompileCompleted?.Invoke(result);

            // Set the result on the TaskCompletionSource.
            TaskCompletionSource<CompileResult> task = _currentCompileTask;
            _currentCompileTask = null;
            task?.SetResult(result);

            // Reset force compile flag for future compilations
            _isForceCompile = false;
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
