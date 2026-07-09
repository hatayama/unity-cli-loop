using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Decides when missing-return failures can be retried and combines retry results.
    /// </summary>
    internal static class DynamicCodeMissingReturnRetryPolicy
    {
        internal static async Task<ExecutionResult> RetryMissingReturnIfNeeded(
            ExecutionResult executionResult,
            string originalCode,
            Func<string, CancellationToken, Task<ExecutionResult>> executeRetryAsync,
            CancellationToken ct)
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
            ExecutionResult retryResult = await executeRetryAsync(codeWithReturn, ct)
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

        internal static List<string> MergeLogs(List<string> originalLogs, List<string> retryLogs)
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

        internal static bool LooksLikeMissingReturn(ExecutionResult executionResult)
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

        internal static bool CanRetryMissingReturn(string originalCode)
        {
            SourceShapeResult shape = SourceShaper.Analyze(originalCode ?? string.Empty);
            return shape.HasTopLevelStatements
                   && !shape.HasNamespaceDeclaration
                   && !shape.HasTypeDeclaration;
        }

        internal static string AppendReturnIfMissing(string originalCode)
        {
            string code = originalCode ?? string.Empty;
            string trimmed = code.TrimEnd();
            bool endsWithSemicolon = trimmed.EndsWith(";");
            string builder = endsWithSemicolon ? code : code + ";";
            return builder + "\nreturn null;";
        }
    }
}
