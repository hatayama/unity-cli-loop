using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Converts dynamic-code execution results into wire-visible tool responses.
    /// </summary>
    internal sealed class DynamicCodeExecutionResponseFactory
    {
        private readonly DynamicCodeFriendlyErrorConverter _friendlyErrorConverter;

        internal DynamicCodeExecutionResponseFactory()
        {
            _friendlyErrorConverter = new DynamicCodeFriendlyErrorConverter();
        }

        internal static bool IsCancelledResult(ExecutionResult executionResult)
        {
            return executionResult != null
                && !executionResult.Success
                && string.Equals(
                    executionResult.ErrorMessage,
                    UnityCliLoopConstants.ERROR_MESSAGE_EXECUTION_CANCELLED,
                    StringComparison.Ordinal);
        }

        internal static bool IsRuntimeRestartingResult(ExecutionResult executionResult)
        {
            return executionResult != null
                && !executionResult.Success
                && string.Equals(
                    executionResult.ErrorMessage,
                    UnityCliLoopConstants.ERROR_MESSAGE_DYNAMIC_CODE_RUNTIME_RESTARTING,
                    StringComparison.Ordinal);
        }

        internal static ExecuteDynamicCodeResponse CreateCancelledResponse()
        {
            return new ExecuteDynamicCodeResponse
            {
                Success = false,
                Result = string.Empty,
                Logs = new List<string> { "Execution cancelled" },
                CompilationErrors = new List<CompilationErrorDto>(),
                ErrorMessage = UnityCliLoopConstants.ERROR_MESSAGE_EXECUTION_CANCELLED
            };
        }

        internal static ExecuteDynamicCodeResponse CreateRuntimeRestartingResponse()
        {
            return new ExecuteDynamicCodeResponse
            {
                Success = false,
                Result = string.Empty,
                Logs = new List<string>
                {
                    UnityCliLoopConstants.ERROR_MESSAGE_DYNAMIC_CODE_RUNTIME_RESTARTING
                },
                CompilationErrors = new List<CompilationErrorDto>(),
                ErrorMessage = UnityCliLoopConstants.ERROR_MESSAGE_DYNAMIC_CODE_RUNTIME_RESTARTING,
                NextActions = UnityCliLoopConstants.DYNAMIC_CODE_RUNTIME_RESTARTING_NEXT_ACTIONS
            };
        }

        internal static ExecutionResult CreateRuntimeRestartingExecutionResult()
        {
            return new ExecutionResult
            {
                Success = false,
                ErrorMessage = UnityCliLoopConstants.ERROR_MESSAGE_DYNAMIC_CODE_RUNTIME_RESTARTING,
                Logs = new List<string>
                {
                    UnityCliLoopConstants.ERROR_MESSAGE_DYNAMIC_CODE_RUNTIME_RESTARTING
                }
            };
        }

        internal ExecuteDynamicCodeResponse ConvertExecutionResultToResponse(
            ExecutionResult result,
            string originalUserSnippet = null)
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
                ApplyFailureResponseDetails(response, result, originalUserSnippet);
            }

            if (result.Exception != null)
            {
                ApplyExceptionResponseDetails(response, result.Exception);
            }
            else
            {
                // Why also scan Logs: CommandRunner puts runtime stacks into Logs without setting
                // ExecutionResult.Exception, so ApplyExceptionResponseDetails alone would miss them.
                PrependUserSnippetExceptionLine(response, null);
            }

            if (result.AutoInjectedNamespaces != null && result.AutoInjectedNamespaces.Count > 0)
            {
                AddAutoInjectedNamespaceHint(response, result.AutoInjectedNamespaces);
            }

            return response;
        }

        private void ApplyFailureResponseDetails(
            ExecuteDynamicCodeResponse response,
            ExecutionResult result,
            string originalUserSnippet)
        {
            DynamicCodeFriendlyError friendlyError = _friendlyErrorConverter.Convert(result);
            response.ErrorMessage = friendlyError.FriendlyMessage;
            response.Logs = result.Logs != null ? new List<string>(result.Logs) : new List<string>();
            AddFriendlyFailureDetails(response.Logs, friendlyError);
            ApplyCompilationDiagnostics(response, result, originalUserSnippet);
            response.UpdatedCode = result.UpdatedCode ?? response.UpdatedCode;
        }

        private static void ApplyCompilationDiagnostics(
            ExecuteDynamicCodeResponse response,
            ExecutionResult result,
            string originalUserSnippet)
        {
            if (result.CompilationErrors?.Any() != true)
            {
                return;
            }

            response.Diagnostics = BuildDiagnostics(
                result.CompilationErrors,
                result.UpdatedCode,
                originalUserSnippet,
                result.AmbiguousTypeCandidates);
            response.CompilationErrors = response.Diagnostics;
            response.DiagnosticsSummary = CreateDiagnosticsSummary(
                response.Diagnostics,
                result.CompilationErrors.Count);
            response.Logs.Add(response.DiagnosticsSummary);
        }

        // Why totalBeforeDeduplication is a parameter: BuildDiagnostics already deduplicated the
        // list, so counting it here can only ever reproduce the unique count — the raw total must
        // come from the pre-deduplication source.
        private static string CreateDiagnosticsSummary(
            List<CompilationErrorDto> diagnostics,
            int totalBeforeDeduplication)
        {
            int unique = diagnostics.Count;
            CompilationErrorDto first = diagnostics.First();
            return $"Errors: {unique} unique ({totalBeforeDeduplication} total). First at L{first.Line}: {first.ErrorCode} {first.Message}";
        }

        private static void ApplyExceptionResponseDetails(
            ExecuteDynamicCodeResponse response,
            Exception exception)
        {
            response.Logs ??= new List<string>();
            PrependUserSnippetExceptionLine(response, exception);
            response.Logs.Add($"Exception: {exception.Message}");
            if (!string.IsNullOrEmpty(exception.StackTrace))
            {
                response.Logs.Add($"Stack Trace: {exception.StackTrace}");
            }
        }

        // Why prepend: agents scan Logs top-down; the raw stack still follows for detail.
        private static void PrependUserSnippetExceptionLine(
            ExecuteDynamicCodeResponse response,
            Exception exception)
        {
            response.Logs ??= new List<string>();
            string stackHaystack = exception?.StackTrace;
            if (string.IsNullOrEmpty(stackHaystack))
            {
                stackHaystack = string.Join("\n", response.Logs);
            }

            if (!TryExtractUserSnippetLineNumber(stackHaystack, out int userSnippetLine))
            {
                return;
            }

            string message = exception?.Message;
            if (string.IsNullOrEmpty(message))
            {
                message = response.ErrorMessage ?? string.Empty;
            }

            string header = $"Exception at user snippet line {userSnippetLine}: {message}";
            if (response.Logs.Count > 0 && string.Equals(response.Logs[0], header, StringComparison.Ordinal))
            {
                return;
            }

            response.Logs.Insert(0, header);
        }

        // Why string parse only: wrapper already emits #line 1 "user-snippet.cs", so a portable
        // PDB records user lines directly — no wrapper-to-user conversion table.
        // Why both formats: .NET uses "user-snippet.cs:line N"; Unity/Mono often uses
        // "…/user-snippet.cs:N" without the "line" keyword.
        internal static bool TryExtractUserSnippetLineNumber(string stackTrace, out int lineNumber)
        {
            lineNumber = 0;
            if (string.IsNullOrEmpty(stackTrace))
            {
                return false;
            }

            Match match = Regex.Match(stackTrace, @"user-snippet\.cs:(?:line )?(\d+)");
            if (!match.Success)
            {
                return false;
            }

            return int.TryParse(match.Groups[1].Value, out lineNumber) && lineNumber > 0;
        }

        private static void AddAutoInjectedNamespaceHint(
            ExecuteDynamicCodeResponse response,
            List<string> autoInjectedNamespaces)
        {
            response.Logs ??= new List<string>();
            string usingList = string.Join(" ", autoInjectedNamespaces.Select(ns => $"using {ns};"));
            response.Logs.Add(
                $"Performance hint: Auto-resolved {autoInjectedNamespaces.Count} missing using directive(s): "
                + $"{usingList} — Include them in your code to skip auto-resolution and improve compilation speed.");
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
            string originalUserSnippet,
            Dictionary<string, List<string>> ambiguousCandidates = null)
        {
            List<CompilationErrorDto> list = new();
            bool hasUserSnippetRegion = WrappedDynamicCodeUserSnippetExtractor.TryExtract(updatedCode, out _);
            string[] originalUserLines = DynamicCodeUserSnippetLines.Split(originalUserSnippet);
            string[] fallbackLines = string.IsNullOrEmpty(updatedCode)
                ? System.Array.Empty<string>()
                : updatedCode.Split(new[] { '\n' }, StringSplitOptions.None);

            foreach (CompilationError error in errors)
            {
                (string hint, List<string> suggestions) = GetHintAndSuggestions(error, ambiguousCandidates);
                bool useOriginalUserContext = hasUserSnippetRegion
                    && DynamicCodeDiagnosticContextBuilder.IsUserSnippetLineInRange(originalUserLines, error.Line);
                string[] contextLines = useOriginalUserContext ? originalUserLines : fallbackLines;
                // why: compiler column is measured on hoisted wrapped source while Context shows original
                // user text; literals hoisted on the same line before the error site can shift column a few
                // positions even after indent subtraction.
                int contextColumn = useOriginalUserContext
                    ? DynamicCodeDiagnosticColumnMapper.MapWrappedColumnToUserColumn(error.Column)
                    : error.Column;
                int reportedColumn = useOriginalUserContext ? contextColumn : error.Column;
                string context = ExtractContext(contextLines, error.Line, contextColumn);
                if (hasUserSnippetRegion && !useOriginalUserContext && error.Line > 0)
                {
                    hint = AppendWrapperOriginHint(hint);
                }

                list.Add(new CompilationErrorDto
                {
                    Line = error.Line,
                    Column = reportedColumn,
                    Message = error.Message,
                    ErrorCode = error.ErrorCode,
                    Hint = hint,
                    Suggestions = suggestions,
                    Context = context,
                    PointerColumn = reportedColumn
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

        private static string AppendWrapperOriginHint(string hint)
        {
            const string wrapperOriginHint =
                "This diagnostic line does not map to the user snippet; it likely refers to generated wrapper code.";
            if (string.IsNullOrEmpty(hint))
            {
                return wrapperOriginHint;
            }

            if (hint.Contains(wrapperOriginHint, StringComparison.Ordinal))
            {
                return hint;
            }

            return hint + " " + wrapperOriginHint;
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
                    (bool matched, string constraintHint, string constraintSuggestion) = DynamicCodeTranspilerConstraintHints.TryBuildHint(
                        error.ErrorCode,
                        error.Message);
                    if (matched)
                    {
                        hint = constraintHint;
                        if (!string.IsNullOrEmpty(constraintSuggestion))
                        {
                            suggestions.Add(constraintSuggestion);
                        }
                    }

                    return (hint, suggestions);
            }
        }

        private static string ExtractContext(
            string[] lines,
            int lineNumber1Based,
            int column1Based)
        {
            return DynamicCodeDiagnosticContextBuilder.BuildContext(lines, lineNumber1Based, column1Based);
        }
    }
}
