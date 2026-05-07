using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Coordinates the execute-dynamic-code workflow while keeping the tool itself thin.
    /// Processing sequence: 1. Resolve security level and parameters, 2. Execute via runtime, 3. Retry missing-return cases, 4. Shape response
    /// </summary>
    internal sealed class ExecuteDynamicCodeUseCase : IExecuteDynamicCodeUseCase
    {
        private const string ForegroundWarmupCode =
            "using UnityEngine; LogType previous = Debug.unityLogger.filterLogType; Debug.unityLogger.filterLogType = LogType.Warning; try { Debug.Log(\"Unity CLI Loop dynamic code prewarm\"); return \"Unity CLI Loop dynamic code prewarm\"; } finally { Debug.unityLogger.filterLogType = previous; }";
        private readonly IDynamicCodeExecutionRuntime _runtime;
        private readonly DynamicCodeFriendlyErrorConverter _friendlyErrorConverter;

        public ExecuteDynamicCodeUseCase(IDynamicCodeExecutionRuntime runtime)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _friendlyErrorConverter = new DynamicCodeFriendlyErrorConverter();
        }

        public async Task<ExecuteDynamicCodeResponse> ExecuteAsync(
            ExecuteDynamicCodeSchema parameters,
            CancellationToken cancellationToken)
        {
            string correlationId = UnityCliLoopConstants.GenerateCorrelationId();
            DynamicCodeSecurityLevel editorLevel = DynamicCodeSecurityLevel.Restricted;

            try
            {
                editorLevel = FirstPartyDynamicCodeSettings.GetDynamicCodeSecurityLevel();
                object[] parametersArray = ConvertParameters(parameters.Parameters);
                string originalCode = parameters.Code ?? string.Empty;
                bool shouldWarmForegroundExecutionPath = ShouldWarmForegroundExecutionPath(parameters);

                LogExecutionStart(parameters, editorLevel, correlationId);

                DynamicCodeExecutionRequest request = CreateExecutionRequest(
                    originalCode,
                    parametersArray,
                    parameters.CompileOnly,
                    editorLevel,
                    parameters.YieldToForegroundRequests);
                await WarmForegroundExecutionPathIfNeededAsync(parameters, editorLevel, cancellationToken);
                ExecutionResult executionResult = await ExecuteRequestAsync(request, cancellationToken);

                ExecutionResult finalResult = await RetryMissingReturnIfNeeded(
                    executionResult,
                    originalCode,
                    parametersArray,
                    parameters.CompileOnly,
                    editorLevel,
                    parameters.YieldToForegroundRequests,
                    cancellationToken);

                if (shouldWarmForegroundExecutionPath && finalResult.Success)
                {
                    DynamicCodeForegroundWarmupState.MarkCompletedByForegroundExecution();
                }

                if (IsCancelledResult(finalResult))
                {
                    ExecuteDynamicCodeResponse cancelledResponse = CreateCancelledResponse(editorLevel);
                    cancelledResponse.Logs = finalResult.Logs ?? cancelledResponse.Logs;
                    cancelledResponse.Timings = finalResult.Timings != null
                        ? new List<string>(finalResult.Timings)
                        : cancelledResponse.Timings;
                    return cancelledResponse;
                }

                ExecuteDynamicCodeResponse response = ConvertExecutionResultToResponse(
                    finalResult,
                    originalCode);
                response.SecurityLevel = editorLevel.ToString();
                return response;
            }
            catch (OperationCanceledException)
            {
                return CreateCancelledResponse(editorLevel);
            }
            catch (Exception ex)
            {
                LogExecutionException(ex, correlationId);
                return CreateExceptionResponse(ex, editorLevel);
            }
        }

        private static void LogExecutionStart(
            ExecuteDynamicCodeSchema parameters,
            DynamicCodeSecurityLevel securityLevel,
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
                    parametersCount = parameters.Parameters?.Count ?? 0,
                    securityLevel = securityLevel.ToString()
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
            DynamicCodeSecurityLevel securityLevel,
            bool yieldToForegroundRequests = false)
        {
            return new DynamicCodeExecutionRequest
            {
                Code = code,
                ClassName = "DynamicCommand",
                Parameters = parameters,
                CompileOnly = compileOnly,
                SecurityLevel = securityLevel,
                YieldToForegroundRequests = yieldToForegroundRequests
            };
        }

        private async Task<ExecutionResult> RetryMissingReturnIfNeeded(
            ExecutionResult executionResult,
            string originalCode,
            object[] parameters,
            bool compileOnly,
            DynamicCodeSecurityLevel securityLevel,
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
                securityLevel,
                yieldToForegroundRequests);
            ExecutionResult retryResult = await ExecuteRequestAsync(retryRequest, cancellationToken);
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
                return await _runtime.ExecuteAsync(request, cancellationToken);
            }

            (bool entered, ExecutionResult result) = await _runtime.TryExecuteIfIdleAsync(
                request,
                cancellationToken);
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
            DynamicCodeSecurityLevel securityLevel,
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
                DynamicCodeExecutionRequest warmupRequest = CreateExecutionRequest(
                    ForegroundWarmupCode,
                    null,
                    compileOnly: false,
                    securityLevel,
                    yieldToForegroundRequests: false);
                ExecutionResult warmupResult = await ExecuteRequestAsync(warmupRequest, cancellationToken);
                completed = warmupResult.Success;
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

        private static bool IsCancelledResult(ExecutionResult executionResult)
        {
            return executionResult != null
                && !executionResult.Success
                && string.Equals(
                    executionResult.ErrorMessage,
                    UnityCliLoopConstants.ERROR_MESSAGE_EXECUTION_CANCELLED,
                    StringComparison.Ordinal);
        }

        private ExecuteDynamicCodeResponse CreateExceptionResponse(
            Exception ex,
            DynamicCodeSecurityLevel securityLevel)
        {
            return new ExecuteDynamicCodeResponse
            {
                Success = false,
                Result = string.Empty,
                Logs = new List<string> { $"Original Error: {ex.Message}" },
                CompilationErrors = new List<CompilationErrorDto>(),
                ErrorMessage = CreateFriendlyExceptionMessage(ex),
                SecurityLevel = securityLevel.ToString()
            };
        }

        private static string CreateFriendlyExceptionMessage(Exception ex)
        {
            if (ex == null)
            {
                return "Unknown error occurred";
            }

            if (ex is TimeoutException)
            {
                return "Request timeout";
            }

            if (ex is ArgumentException)
            {
                return "Invalid request parameters";
            }

            return ex.Message ?? "Unknown error occurred";
        }

        private static ExecuteDynamicCodeResponse CreateCancelledResponse(
            DynamicCodeSecurityLevel securityLevel)
        {
            return new ExecuteDynamicCodeResponse
            {
                Success = false,
                Result = string.Empty,
                Logs = new List<string> { "Execution cancelled" },
                CompilationErrors = new List<CompilationErrorDto>(),
                ErrorMessage = UnityCliLoopConstants.ERROR_MESSAGE_EXECUTION_CANCELLED,
                SecurityLevel = securityLevel.ToString()
            };
        }

        private static void LogExecutionException(Exception ex, string correlationId)
        {
            VibeLogger.LogError(
                "execute_dynamic_code_error",
                "Dynamic code execution failed with exception",
                new
                {
                    correlationId,
                    error = ex.Message,
                    stackTrace = ex.StackTrace
                },
                correlationId,
                "Unexpected error during dynamic code execution",
                "Investigate error cause and improve error handling");
        }

        private ExecuteDynamicCodeResponse ConvertExecutionResultToResponse(
            ExecutionResult result,
            string originalCode)
        {
            ExecuteDynamicCodeResponse response = new()            {
                Success = result.Success,
                Result = result.Result?.ToString() ?? string.Empty,
                Logs = result.Logs ?? new List<string>(),
                CompilationErrors = new List<CompilationErrorDto>(),
                ErrorMessage = result.ErrorMessage ?? string.Empty,
                Timings = result.Timings != null ? new List<string>(result.Timings) : new List<string>()
            };

            if (!result.Success)
            {
                DynamicCodeFriendlyError friendlyError = _friendlyErrorConverter.Convert(result);
                response.ErrorMessage = friendlyError.FriendlyMessage;
                response.Logs = result.Logs != null ? new List<string>(result.Logs) : new List<string>();
                AddFriendlyFailureDetails(response.Logs, friendlyError);

                if (result.CompilationErrors?.Any() == true)
                {
                    response.Diagnostics = BuildDiagnostics(
                        result.CompilationErrors,
                        result.UpdatedCode,
                        result.AmbiguousTypeCandidates);
                    response.CompilationErrors = response.Diagnostics;

                    int total = response.Diagnostics.Count;
                    int unique = response.Diagnostics
                        .GroupBy(error => new { error.Line, error.Column, error.ErrorCode, error.Message })
                        .Count();
                    CompilationErrorDto first = response.Diagnostics.First();
                    response.DiagnosticsSummary =
                        $"Errors: {unique} unique ({total} total). First at L{first.Line}: {first.ErrorCode} {first.Message}";

                    response.Logs.Add(response.DiagnosticsSummary);
                }

                response.UpdatedCode = result.UpdatedCode ?? response.UpdatedCode;
            }

            if (result.Exception != null)
            {
                if (response.Logs == null)
                {
                    response.Logs = new List<string>();
                }

                response.Logs.Add($"Exception: {result.Exception.Message}");
                if (!string.IsNullOrEmpty(result.Exception.StackTrace))
                {
                    response.Logs.Add($"Stack Trace: {result.Exception.StackTrace}");
                }
            }

            if (result.AutoInjectedNamespaces != null && result.AutoInjectedNamespaces.Count > 0)
            {
                if (response.Logs == null)
                {
                    response.Logs = new List<string>();
                }

                string usingList = string.Join(" ", result.AutoInjectedNamespaces.Select(ns => $"using {ns};"));
                response.Logs.Add(
                    $"Performance hint: Auto-resolved {result.AutoInjectedNamespaces.Count} missing using directive(s): "
                    + $"{usingList} — Include them in your code to skip auto-resolution and improve compilation speed.");
            }

            return response;
        }

        private static void AddFriendlyFailureDetails(
            List<string> logs,
            DynamicCodeFriendlyError friendlyError)
        {
            System.Diagnostics.Debug.Assert(logs != null, "logs must not be null");
            System.Diagnostics.Debug.Assert(friendlyError != null, "friendlyError must not be null");

            AddLogIfNotEmpty(logs, "Explanation: ", friendlyError.Explanation);
            AddLogIfNotEmpty(logs, "Example: ", friendlyError.Example);

            if (friendlyError.SuggestedSolutions.Count == 0)
            {
                return;
            }

            logs.Add("Solutions:");
            foreach (string solution in friendlyError.SuggestedSolutions)
            {
                logs.Add("- " + solution);
            }
        }

        private static void AddLogIfNotEmpty(List<string> logs, string prefix, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            logs.Add(prefix + value);
        }

        private static List<CompilationErrorDto> BuildDiagnostics(
            List<CompilationError> errors,
            string updatedCode,
            Dictionary<string, List<string>> ambiguousCandidates = null)
        {
            List<CompilationErrorDto> list = new();
            string[] lines = string.IsNullOrEmpty(updatedCode)
                ? Array.Empty<string>()
                : updatedCode.Split(new[] { '\n' }, StringSplitOptions.None);

            foreach (CompilationError error in errors)
            {
                (string hint, List<string> suggestions) = GetHintAndSuggestions(error, ambiguousCandidates);
                string context = ExtractContext(lines, error.Line, error.Column);
                list.Add(new CompilationErrorDto
                {
                    Line = error.Line,
                    Column = error.Column,
                    Message = error.Message,
                    ErrorCode = error.ErrorCode,
                    Hint = hint,
                    Suggestions = suggestions,
                    Context = context,
                    PointerColumn = error.Column
                });
            }

            return list
                .GroupBy(diagnostic => new
                {
                    diagnostic.Line,
                    diagnostic.Column,
                    diagnostic.ErrorCode,
                    diagnostic.Message
                })
                .Select(group => group.First())
                .ToList();
        }

        private static (string hint, List<string> suggestions) GetHintAndSuggestions(
            CompilationError error,
            Dictionary<string, List<string>> ambiguousCandidates = null)
        {
            string hint = string.Empty;
            List<string> suggestions = new();

            switch (error.ErrorCode)
            {
                case "CS0246":
                    string typeName = CompilationDiagnosticMessageParser.ExtractTypeNameFromMessage(error.Message);
                    if (typeName != null
                        && ambiguousCandidates != null
                        && ambiguousCandidates.TryGetValue(typeName, out List<string> candidates))
                    {
                        string candidateList = string.Join(", ", candidates);
                        hint = $"Auto-using resolution found multiple candidates for '{typeName}': {candidateList}. Use a fully-qualified name or add the correct using directive.";
                        foreach (string ns in candidates)
                        {
                            suggestions.Add($"Use {ns}.{typeName}");
                        }

                        return (hint, suggestions);
                    }

                    hint = "Auto-using resolution was attempted but could not resolve this identifier. Use a fully-qualified name (e.g., UnityEngine.Mathf) or add the correct using directive.";
                    suggestions.Add("Use fully-qualified name (e.g., UnityEngine.Mathf, System.Linq.Enumerable)");
                    suggestions.Add("Add the appropriate using directive at the top of the snippet");
                    return (hint, suggestions);

                case "CS0103":
                    string identifierName = CompilationDiagnosticMessageParser.ExtractTypeNameFromMessage(error.Message);
                    if (identifierName != null
                        && ambiguousCandidates != null
                        && ambiguousCandidates.TryGetValue(identifierName, out List<string> identifierCandidates))
                    {
                        string candidateList = string.Join(", ", identifierCandidates);
                        hint = $"Auto-using resolution found multiple candidates for '{identifierName}': {candidateList}. Use a fully-qualified name or add the correct using directive.";
                        foreach (string ns in identifierCandidates)
                        {
                            suggestions.Add($"Use {ns}.{identifierName}");
                        }

                        return (hint, suggestions);
                    }

                    hint = "Identifier does not exist in the current context. Check spelling, declaration scope, and whether this should be a type name.";
                    suggestions.Add("Declare the identifier before use");
                    suggestions.Add("If this is a type name, use a fully-qualified name or add the correct using directive");
                    return (hint, suggestions);

                case "CS0104":
                    hint = "Identifier is ambiguous; qualify explicitly (e.g., UnityEngine.Object).";
                    suggestions.Add("Qualify with full namespace (e.g., UnityEngine.Object)");
                    return (hint, suggestions);

                default:
                    return (hint, suggestions);
            }
        }

        private static string ExtractContext(
            string[] lines,
            int lineNumber1Based,
            int column1Based)
        {
            if (lines == null || lines.Length == 0 || lineNumber1Based <= 0 || lineNumber1Based > lines.Length)
            {
                return string.Empty;
            }

            int start = Math.Max(1, lineNumber1Based - 3);
            int end = Math.Min(lines.Length, lineNumber1Based + 3);
            StringBuilder sb = new();
            for (int i = start; i <= end; i++)
            {
                string line = lines[i - 1].TrimEnd('\r');
                string linePrefix = $"L{i}:";
                sb.AppendLine(linePrefix + line);
                if (i == lineNumber1Based)
                {
                    int caretPos = Math.Max(1, column1Based);
                    sb.AppendLine(
                        new string(' ', linePrefix.Length)
                        + new string(' ', Math.Max(0, caretPos - 1))
                        + "^");
                }
            }

            return sb.ToString();
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
