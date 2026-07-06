using System;
using System.Diagnostics;
using Newtonsoft.Json;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Stores CompileResponse JSON in SessionState after CompileResponseFactory shapes the raw compile result.
    /// </summary>
    internal static class CompileSessionResultStore
    {
        internal static void StoreCompileResult(
            ICompileResultSessionRepository compileResultSessionRepository,
            IPendingCompileSessionRepository pendingCompileSessionRepository,
            string requestId,
            bool forceRecompile,
            CompileResponse result,
            string correlationId)
        {
            Debug.Assert(compileResultSessionRepository != null, "compileResultSessionRepository must not be null");
            Debug.Assert(pendingCompileSessionRepository != null, "pendingCompileSessionRepository must not be null");
            Debug.Assert(!string.IsNullOrWhiteSpace(requestId), "requestId must not be null or whitespace");
            Debug.Assert(result != null, "result must not be null");

            ICompileResultSessionRepository compileResultRepository = compileResultSessionRepository ??
                throw new ArgumentNullException(nameof(compileResultSessionRepository));
            IPendingCompileSessionRepository pendingCompileRepository = pendingCompileSessionRepository ??
                throw new ArgumentNullException(nameof(pendingCompileSessionRepository));
            result.ProjectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            string resultJson = JsonConvert.SerializeObject(
                result,
                Formatting.None,
                JsonRpcResponseSerializer.Settings);
            UnityCliLoopStoredCompileResult previousResult =
                compileResultRepository.GetCompileResult(requestId);
            UnityCliLoopPendingCompileRequest pendingRequest =
                pendingCompileRepository.GetPendingCompileRequestForRequestId(requestId);
            compileResultRepository.StoreCompileResult(
                requestId,
                forceRecompile,
                resultJson,
                DateTime.UtcNow);
            bool pendingRequestCleared =
                pendingCompileRepository.ClearPendingCompileRequestIfMatches(requestId);
            VibeLogger.LogInfo(
                "compile_result_session_state_store_complete",
                "Stored compile result in SessionState for CLI status polling.",
                new
                {
                    request_id = requestId,
                    force_recompile = forceRecompile,
                    success = result.Success,
                    error_count = result.ErrorCount,
                    warning_count = result.WarningCount,
                    result_bytes = System.Text.Encoding.UTF8.GetByteCount(resultJson),
                    store_sequence = previousResult.HasResult ? 2 : 1,
                    pending_request_before = pendingRequest.HasRequest,
                    pending_request_cleared = pendingRequestCleared,
                    duplicate_result_for_request = previousResult.HasResult
                },
                correlationId);
        }
    }
}
