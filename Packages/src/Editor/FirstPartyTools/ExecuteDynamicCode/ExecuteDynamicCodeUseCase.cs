using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Coordinates the execute-dynamic-code workflow while keeping the tool itself thin.
    /// Processing sequence: 1. Resolve parameters, 2. Execute via runtime, 3. Retry missing-return cases, 4. Shape response
    /// </summary>
    internal sealed class ExecuteDynamicCodeUseCase : IExecuteDynamicCodeUseCase
    {
        private readonly IDynamicCodeExecutionRuntime _runtime;
        private readonly DynamicCodeExecutionResponseFactory _responseFactory;

        public ExecuteDynamicCodeUseCase(IDynamicCodeExecutionRuntime runtime)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _responseFactory = new DynamicCodeExecutionResponseFactory();
        }

        public async Task<ExecuteDynamicCodeResponse> ExecuteAsync(
            ExecuteDynamicCodeSchema parameters,
            CancellationToken cancellationToken)
        {
            using DynamicCodeDomainReloadWaitSignal domainReloadWaitSignal =
                DynamicCodeDomainReloadWaitSignal.Start(parameters);

            try
            {
                object[] parametersArray = ConvertParameters(parameters.Parameters);
                string originalCode = parameters.Code ?? string.Empty;

                LogExecutionStart(parameters, UnityCliLoopConstants.GenerateCorrelationId());

                DynamicCodeExecutionRequest request = CreateExecutionRequest(
                    originalCode,
                    parametersArray,
                    parameters.CompileOnly,
                    parameters.YieldToForegroundRequests);
                await WarmForegroundExecutionPathIfNeededAsync(parameters, cancellationToken)
                    .ConfigureAwait(false);
                ExecutionResult executionResult = await ExecuteRequestAsync(request, cancellationToken).ConfigureAwait(false);

                ExecutionResult finalResult = await RetryMissingReturnIfNeeded(
                    executionResult,
                    originalCode,
                    parametersArray,
                    parameters.CompileOnly,
                    parameters.YieldToForegroundRequests,
                    cancellationToken).ConfigureAwait(false);

                if (ShouldMarkExecutionPathWarm(parameters, finalResult))
                {
                    DynamicCodeForegroundWarmupState.MarkCompletedBySuccessfulExecution();
                }

                if (DynamicCodeExecutionResponseFactory.IsCancelledResult(finalResult))
                {
                    ExecuteDynamicCodeResponse cancelledResponse =
                        DynamicCodeExecutionResponseFactory.CreateCancelledResponse();
                    cancelledResponse.Logs = finalResult.Logs ?? cancelledResponse.Logs;
                    cancelledResponse.Timings = finalResult.Timings != null
                        ? new List<string>(finalResult.Timings)
                        : cancelledResponse.Timings;
                    cancelledResponse.EmitTimingsInJsonResponse = parameters.IncludeTimings;
                    return cancelledResponse;
                }

                ExecuteDynamicCodeResponse response = _responseFactory.ConvertExecutionResultToResponse(finalResult);
                response.EmitTimingsInJsonResponse = parameters.IncludeTimings;
                // Why: domain-reload timeouts can complete while Unity's synchronization context is stalled.
                bool domainReloadWaitRequired =
                    await domainReloadWaitSignal.ShouldWaitAsync(cancellationToken).ConfigureAwait(false);
                response.DomainReloadWaitRequired = domainReloadWaitRequired;
                return response;
            }
            catch (OperationCanceledException)
            {
                ExecuteDynamicCodeResponse response = DynamicCodeExecutionResponseFactory.CreateCancelledResponse();
                response.EmitTimingsInJsonResponse = parameters?.IncludeTimings ?? false;
                return response;
            }
        }

        private static void LogExecutionStart(
            ExecuteDynamicCodeSchema parameters,
            string correlationId)
        {
            VibeLogger.LogInfo(
                "execute_dynamic_code_start",
                "Dynamic code execution started (return optional)",
                new
                {
                    correlationId,
                    codeLength = parameters.Code?.Length ?? 0,
                    compileOnly = parameters.CompileOnly,
                    parametersCount = parameters.Parameters?.Count ?? 0
                },
                correlationId,
                "Dynamic code execution request received (return is optional)",
                "Monitor execution flow and performance");
        }

        private static object[] ConvertParameters(Dictionary<string, object> parameters)
        {
            if (parameters == null || parameters.Count == 0)
            {
                return null;
            }

            return parameters.Values.ToArray();
        }

        private static DynamicCodeExecutionRequest CreateExecutionRequest(
            string code,
            object[] parameters,
            bool compileOnly,
            bool yieldToForegroundRequests = false)
        {
            return new DynamicCodeExecutionRequest
            {
                Code = code,
                ClassName = "DynamicCommand",
                Parameters = parameters,
                CompileOnly = compileOnly,
                YieldToForegroundRequests = yieldToForegroundRequests
            };
        }

        private async Task<ExecutionResult> RetryMissingReturnIfNeeded(
            ExecutionResult executionResult,
            string originalCode,
            object[] parameters,
            bool compileOnly,
            bool yieldToForegroundRequests,
            CancellationToken cancellationToken)
        {
            if (executionResult.Success)
            {
                return executionResult;
            }

            bool looksLikeMissingReturn = LooksLikeMissingReturn(executionResult);
            if (!looksLikeMissingReturn || !CanRetryMissingReturn(originalCode))
            {
                return executionResult;
            }

            string codeWithReturn = AppendReturnIfMissing(originalCode);
            DynamicCodeExecutionRequest retryRequest = CreateExecutionRequest(
                codeWithReturn,
                parameters,
                compileOnly,
                yieldToForegroundRequests);
            ExecutionResult retryResult = await ExecuteRequestAsync(retryRequest, cancellationToken)
                .ConfigureAwait(false);
            if (retryResult.Success)
            {
                return retryResult;
            }

            if (retryResult.Logs?.Any() == true)
            {
                retryResult.Logs = MergeLogs(executionResult.Logs, retryResult.Logs);
            }
            else
            {
                retryResult.Logs = CloneLogs(executionResult.Logs);
            }

            return retryResult;
        }

        private static List<string> MergeLogs(List<string> originalLogs, List<string> retryLogs)
        {
            List<string> mergedLogs = CloneLogs(originalLogs);
            if (retryLogs == null || retryLogs.Count == 0)
            {
                return mergedLogs;
            }

            if (mergedLogs == null)
            {
                return new List<string>(retryLogs);
            }

            mergedLogs.AddRange(retryLogs);
            return mergedLogs;
        }

        private static List<string> CloneLogs(List<string> logs)
        {
            return logs == null ? null : new List<string>(logs);
        }

        private static bool LooksLikeMissingReturn(ExecutionResult executionResult)
        {
            if (executionResult.CompilationErrors?.Any() == true)
            {
                return executionResult.CompilationErrors.Any(error =>
                    error.ErrorCode == "CS0161" || error.ErrorCode == "CS0127");
            }

            if (executionResult.Logs?.Any() == true)
            {
                return executionResult.Logs.Any(log =>
                    log.Contains("CS0161") ||
                    log.Contains("CS0127") ||
                    log.Contains("must return a value"));
            }

            return false;
        }

        private static bool CanRetryMissingReturn(string originalCode)
        {
            SourceShapeResult shape = SourceShaper.Analyze(originalCode ?? string.Empty);
            return shape.HasTopLevelStatements
                   && !shape.HasNamespaceDeclaration
                   && !shape.HasTypeDeclaration;
        }

        private async Task<ExecutionResult> ExecuteRequestAsync(
            DynamicCodeExecutionRequest request,
            CancellationToken cancellationToken)
        {
            if (!request.YieldToForegroundRequests)
            {
                return await _runtime.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
            }

            (bool entered, ExecutionResult result) = await _runtime.TryExecuteIfIdleAsync(
                request,
                cancellationToken).ConfigureAwait(false);
            if (entered)
            {
                return result;
            }

            return new ExecutionResult
            {
                Success = false,
                ErrorMessage = UnityCliLoopConstants.ERROR_MESSAGE_EXECUTION_IN_PROGRESS
            };
        }

        private async Task WarmForegroundExecutionPathIfNeededAsync(
            ExecuteDynamicCodeSchema parameters,
            CancellationToken cancellationToken)
        {
            if (!ShouldWarmForegroundExecutionPath(parameters))
            {
                return;
            }

            if (!DynamicCodeForegroundWarmupState.TryBegin())
            {
                return;
            }

            bool completed = false;
            try
            {
                completed = await ExecuteForegroundWarmupSequenceAsync(cancellationToken).ConfigureAwait(false);
                if (completed)
                {
                    DynamicCodeForegroundWarmupState.MarkCompleted();
                }
            }
            finally
            {
                if (!completed)
                {
                    DynamicCodeForegroundWarmupState.ResetAfterIncompleteAttempt();
                }
            }
        }

        private async Task<bool> ExecuteForegroundWarmupSequenceAsync(CancellationToken ct)
        {
            return await DynamicCodeForegroundWarmupRunner.RunForegroundSequenceAsync(
                _runtime,
                yieldToForegroundRequests: false,
                ct).ConfigureAwait(false);
        }

        private static bool ShouldWarmForegroundExecutionPath(ExecuteDynamicCodeSchema parameters)
        {
            if (parameters == null)
            {
                return false;
            }

            // Why: this fallback only exists to protect the first real foreground execution that
            // users see after startup or reload.
            // Why not run it for compile-only or yield-to-foreground requests: compile validation
            // does not need the runtime hot path, and yield-based requests are background work
            // that must stay cancellable.
            return !parameters.CompileOnly && !parameters.YieldToForegroundRequests;
        }

        private static bool ShouldMarkExecutionPathWarm(
            ExecuteDynamicCodeSchema parameters,
            ExecutionResult executionResult)
        {
            return parameters != null
                && !parameters.CompileOnly
                && executionResult != null
                && executionResult.Success;
        }

        private static string AppendReturnIfMissing(string originalCode)
        {
            string code = originalCode ?? string.Empty;
            string trimmed = code.TrimEnd();
            bool endsWithSemicolon = trimmed.EndsWith(";");
            string builder = endsWithSemicolon ? code : code + ";";
            return builder + "\nreturn null;";
        }
    }
}
