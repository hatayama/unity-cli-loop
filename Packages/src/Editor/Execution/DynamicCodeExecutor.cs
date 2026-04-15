using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Bridges compilation and execution so the tool layer can stay focused on request and response shaping.
    /// </summary>
    internal sealed class DynamicCodeExecutor : IDynamicCodeExecutor
    {
        private readonly IDynamicCompilationService _compiler;
        private readonly ICompiledCommandInvoker _invoker;
        private readonly DynamicCodeSourcePreparationService _sourcePreparationService;
        private readonly ExecutionStatistics _statistics;
        private readonly object _statsLock = new();

        public DynamicCodeExecutor(
            IDynamicCompilationService compiler,
            ICompiledCommandInvoker invoker)
            : this(compiler, invoker, DynamicCodeServices.SourcePreparationService)
        {
        }

        internal DynamicCodeExecutor(
            IDynamicCompilationService compiler,
            ICompiledCommandInvoker invoker,
            DynamicCodeSourcePreparationService sourcePreparationService)
        {
            _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
            _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
            _sourcePreparationService = sourcePreparationService ?? throw new ArgumentNullException(nameof(sourcePreparationService));
            _statistics = new ExecutionStatistics();
        }

        public async Task<ExecutionResult> ExecuteCodeAsync(
            string code,
            string className = DynamicCodeConstants.DEFAULT_CLASS_NAME,
            object[] parameters = null,
            CancellationToken cancellationToken = default,
            bool compileOnly = false)
        {
            string correlationId = McpConstants.GenerateCorrelationId();
            Stopwatch totalStopwatch = Stopwatch.StartNew();
            Stopwatch sourcePreparationStopwatch = Stopwatch.StartNew();

            try
            {
                PreparedDynamicCode preparedCode = _sourcePreparationService.Prepare(
                    code,
                    DynamicCodeConstants.DEFAULT_NAMESPACE,
                    className);
                sourcePreparationStopwatch.Stop();
                Stopwatch compileTotalStopwatch = Stopwatch.StartNew();
                CompilationResult compilationResult = await CompileCodeAsync(code, className, cancellationToken);
                compileTotalStopwatch.Stop();

                ExecutionResult compilationFailureResult = TryCreateCompilationFailureResult(
                    compilationResult,
                    totalStopwatch);
                if (compilationFailureResult != null)
                {
                    AppendExecutorStageTimings(
                        compilationFailureResult.Timings,
                        sourcePreparationStopwatch.Elapsed.TotalMilliseconds,
                        compileTotalStopwatch.Elapsed.TotalMilliseconds);
                    return compilationFailureResult;
                }

                if (compileOnly)
                {
                    ExecutionResult compileOnlyResult = CreateCompileOnlySuccessResult(compilationResult, totalStopwatch.Elapsed);
                    AppendExecutorStageTimings(
                        compileOnlyResult.Timings,
                        sourcePreparationStopwatch.Elapsed.TotalMilliseconds,
                        compileTotalStopwatch.Elapsed.TotalMilliseconds);
                    return compileOnlyResult;
                }

                ExecutionContext context = new ExecutionContext
                {
                    CompiledAssembly = compilationResult.CompiledAssembly,
                    Parameters = BuildExecutionParameters(parameters, preparedCode.HoistedLiteralBindings),
                    CancellationToken = cancellationToken
                };

                Stopwatch executionStopwatch = Stopwatch.StartNew();
                ExecutionResult executionResult = await _invoker.ExecuteAsync(context);
                executionStopwatch.Stop();

                executionResult.ExecutionTime = totalStopwatch.Elapsed;
                executionResult.AutoInjectedNamespaces = compilationResult.AutoInjectedNamespaces;
                executionResult.Timings = MergeTimings(
                    compilationResult.Timings,
                    executionResult.Timings,
                    $"[Perf] Execution: {executionStopwatch.Elapsed.TotalMilliseconds:F1}ms");
                AppendExecutorStageTimings(
                    executionResult.Timings,
                    sourcePreparationStopwatch.Elapsed.TotalMilliseconds,
                    compileTotalStopwatch.Elapsed.TotalMilliseconds);
                AppendCompilationAdvisories(executionResult.Logs, compilationResult.AdvisoryLogs);

                UpdateStatistics(executionResult, totalStopwatch.Elapsed);
                return executionResult;
            }
            catch (OperationCanceledException)
            {
                ExecutionResult cancelledResult = CreateCancelledResult(totalStopwatch.Elapsed);
                UpdateStatistics(cancelledResult, totalStopwatch.Elapsed);
                return cancelledResult;
            }
            catch (Exception ex)
            {
                ExecutionResult failureResult = CreateUnexpectedErrorResult(ex, totalStopwatch.Elapsed);
                UpdateStatistics(failureResult, totalStopwatch.Elapsed);
                LogUnexpectedExecutionException(ex, correlationId, totalStopwatch.ElapsedMilliseconds);
                return failureResult;
            }
        }

        public ExecutionStatistics GetStatistics()
        {
            lock (_statsLock)
            {
                return new ExecutionStatistics
                {
                    TotalExecutions = _statistics.TotalExecutions,
                    SuccessfulExecutions = _statistics.SuccessfulExecutions,
                    FailedExecutions = _statistics.FailedExecutions,
                    AverageExecutionTime = _statistics.AverageExecutionTime,
                    SecurityViolations = _statistics.SecurityViolations,
                    CompilationErrors = _statistics.CompilationErrors
                };
            }
        }

        public void Dispose()
        {
            if (_compiler is IDisposable disposableCompiler)
            {
                disposableCompiler.Dispose();
            }
        }

        private async Task<CompilationResult> CompileCodeAsync(
            string code,
            string className,
            CancellationToken ct)
        {
            CompilationRequest request = new CompilationRequest
            {
                Code = code,
                ClassName = className,
                Namespace = DynamicCodeConstants.DEFAULT_NAMESPACE
            };

            CompilationResult result = await _compiler.CompileAsync(request, ct);
            if (!result.Success)
            {
                lock (_statsLock)
                {
                    _statistics.CompilationErrors++;
                }
            }

            return result;
        }

        private ExecutionResult TryCreateCompilationFailureResult(
            CompilationResult compilationResult,
            Stopwatch totalStopwatch)
        {
            if (!compilationResult.Success)
            {
                if (compilationResult.HasSecurityViolations)
                {
                    return CreateSecurityFailureResult(
                        "Security violations detected",
                        totalStopwatch.Elapsed,
                        compilationResult.SecurityViolations,
                        compilationResult.Timings);
                }

                return CreateCompilationFailureResult(
                    "Compilation error occurred",
                    totalStopwatch.Elapsed,
                    compilationResult);
            }

            if (compilationResult.HasSecurityViolations)
            {
                return CreateSecurityFailureResult(
                    "Security violations detected (Dangerous API call)",
                    totalStopwatch.Elapsed,
                    compilationResult.SecurityViolations,
                    compilationResult.Timings);
            }

            return null;
        }

        private static ExecutionResult CreateCompileOnlySuccessResult(
            CompilationResult compilationResult,
            TimeSpan executionTime)
        {
            return new ExecutionResult
            {
                Success = true,
                Result = null,
                ExecutionTime = executionTime,
                Logs = MergeCompilationLogs(
                    new List<string> { "Code compiled successfully (no execution)" },
                    compilationResult.AdvisoryLogs),
                AutoInjectedNamespaces = compilationResult.AutoInjectedNamespaces,
                Timings = CloneTimings(compilationResult.Timings)
            };
        }

        private static ExecutionResult CreateCancelledResult(TimeSpan executionTime)
        {
            return new ExecutionResult
            {
                Success = false,
                ErrorMessage = McpConstants.ERROR_MESSAGE_EXECUTION_CANCELLED,
                Logs = new List<string> { "Execution cancelled" },
                ExecutionTime = executionTime,
                Timings = new List<string>()
            };
        }

        private static ExecutionResult CreateSecurityFailureResult(
            string message,
            TimeSpan executionTime,
            List<SecurityViolation> violations,
            List<string> timings)
        {
            List<string> violationMessages = BuildViolationMessages(violations);
            if (violationMessages.Count > 0)
            {
                message = $"{message} {string.Join(" ", violationMessages)}";
            }

            return new ExecutionResult
            {
                Success = false,
                ErrorMessage = message,
                Logs = violationMessages,
                ExecutionTime = executionTime,
                Timings = timings != null ? new List<string>(timings) : new List<string>()
            };
        }

        private static ExecutionResult CreateCompilationFailureResult(
            string message,
            TimeSpan executionTime,
            CompilationResult compilationResult)
        {
            return new ExecutionResult
            {
                Success = false,
                ErrorMessage = message,
                Logs = MergeCompilationLogs(new List<string>(), compilationResult.AdvisoryLogs),
                ExecutionTime = executionTime,
                CompilationErrors = compilationResult.Errors,
                UpdatedCode = compilationResult.UpdatedCode,
                AmbiguousTypeCandidates = compilationResult.AmbiguousTypeCandidates,
                AutoInjectedNamespaces = compilationResult.AutoInjectedNamespaces,
                Timings = CloneTimings(compilationResult.Timings)
            };
        }

        private static ExecutionResult CreateUnexpectedErrorResult(
            Exception ex,
            TimeSpan executionTime)
        {
            return new ExecutionResult
            {
                Success = false,
                ErrorMessage = "An unexpected error occurred during execution",
                Exception = ex,
                Logs = new List<string> { ex.ToString() },
                ExecutionTime = executionTime,
                Timings = new List<string>()
            };
        }

        private static List<string> BuildViolationMessages(List<SecurityViolation> violations)
        {
            List<string> violationMessages = new List<string>();
            if (violations == null)
            {
                return violationMessages;
            }

            foreach (SecurityViolation violation in violations)
            {
                string violationMessage = !string.IsNullOrEmpty(violation.Message)
                    ? violation.Message
                    : violation.Description;

                if (!string.IsNullOrEmpty(violation.ApiName))
                {
                    violationMessages.Add($"{violation.Type}: {violationMessage} (API: {violation.ApiName})");
                    continue;
                }

                violationMessages.Add($"{violation.Type}: {violationMessage}");
            }

            return violationMessages;
        }

        private static List<string> MergeCompilationLogs(
            List<string> logs,
            List<string> advisoryLogs)
        {
            List<string> mergedLogs = logs ?? new List<string>();
            AppendCompilationAdvisories(mergedLogs, advisoryLogs);
            return mergedLogs;
        }

        private static void AppendCompilationAdvisories(
            List<string> destination,
            List<string> advisoryLogs)
        {
            if (destination == null || advisoryLogs == null || advisoryLogs.Count == 0)
            {
                return;
            }

            destination.AddRange(advisoryLogs);
        }

        private void UpdateStatistics(ExecutionResult result, TimeSpan executionTime)
        {
            lock (_statsLock)
            {
                _statistics.TotalExecutions++;
                if (result.Success)
                {
                    _statistics.SuccessfulExecutions++;
                }
                else
                {
                    _statistics.FailedExecutions++;
                }

                double totalMilliseconds =
                    _statistics.AverageExecutionTime.TotalMilliseconds * (_statistics.TotalExecutions - 1);
                totalMilliseconds += executionTime.TotalMilliseconds;
                _statistics.AverageExecutionTime =
                    TimeSpan.FromMilliseconds(totalMilliseconds / _statistics.TotalExecutions);
            }
        }

        private static Dictionary<string, object> BuildExecutionParameters(
            object[] parameters,
            IReadOnlyCollection<HoistedLiteralBinding> hoistedLiteralBindings)
        {
            Dictionary<string, object> executionParameters = new Dictionary<string, object>();

            if (hoistedLiteralBindings != null)
            {
                foreach (HoistedLiteralBinding binding in hoistedLiteralBindings)
                {
                    executionParameters[binding.ParameterName] = binding.Value;
                }
            }

            if (parameters != null)
            {
                for (int i = 0; i < parameters.Length; i++)
                {
                    executionParameters[$"param{i}"] = parameters[i];
                }
            }

            return executionParameters;
        }

        private static List<string> MergeTimings(
            List<string> compilationTimings,
            List<string> executionTimings,
            string executionEntry)
        {
            List<string> mergedTimings = new List<string>();
            if (compilationTimings != null)
            {
                mergedTimings.AddRange(compilationTimings);
            }

            if (executionTimings != null)
            {
                mergedTimings.AddRange(executionTimings);
            }

            mergedTimings.Add(executionEntry);
            return mergedTimings;
        }

        private static void AppendExecutorStageTimings(
            List<string> timings,
            double sourcePreparationMilliseconds,
            double compileTotalMilliseconds)
        {
            if (timings == null)
            {
                return;
            }

            timings.Add($"[Perf] SourcePrepare: {sourcePreparationMilliseconds:F1}ms");
            timings.Add($"[Perf] CompileTotal: {compileTotalMilliseconds:F1}ms");
        }

        private static List<string> CloneTimings(List<string> timings)
        {
            return timings != null ? new List<string>(timings) : new List<string>();
        }

        private static void LogUnexpectedExecutionException(
            Exception ex,
            string correlationId,
            long executionTimeMilliseconds)
        {
            VibeLogger.LogError(
                "execute_code_exception",
                "Unexpected error during code execution",
                new
                {
                    exception_type = ex.GetType().Name,
                    execution_time_ms = executionTimeMilliseconds
                },
                correlationId,
                "Dynamic Code Execution Error",
                "Investigating Unexpected Exception");
        }
    }
}
