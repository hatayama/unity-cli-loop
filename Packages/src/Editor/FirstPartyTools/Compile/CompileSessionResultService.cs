using System;
using System.Diagnostics;
using System.Linq;
using Newtonsoft.Json;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Stores compile results in Editor SessionState so the CLI can read them after Domain Reload.
    /// </summary>
    internal static class CompileSessionResultService
    {
        internal static UnityCliLoopCompileResult CreateCompileResult(
            CompileResult result,
            bool forceRecompile)
        {
            Debug.Assert(result != null, "result must not be null");

            if (forceRecompile)
            {
                return CreateForceCompileResult(result);
            }

            if (result.IsIndeterminate)
            {
                return new UnityCliLoopCompileResult
                {
                    Success = result.Success,
                    ErrorCount = result.ErrorCount,
                    WarningCount = result.WarningCount,
                    Errors = null,
                    Warnings = null,
                    Message = result.Message ?? "Compilation status is unknown. Use get-logs to inspect the compiler output."
                };
            }

            return new UnityCliLoopCompileResult
            {
                Success = result.Success,
                ErrorCount = result.Errors?.Length ?? 0,
                WarningCount = result.Warnings?.Length ?? 0,
                Errors = ToIssues(result.Errors),
                Warnings = ToIssues(result.Warnings),
                Message = result.Message
            };
        }

        internal static void StoreCompileResult(
            UnityCliLoopEditorSessionStateService sessionStateService,
            string requestId,
            bool forceRecompile,
            UnityCliLoopCompileResult result,
            string correlationId)
        {
            Debug.Assert(sessionStateService != null, "sessionStateService must not be null");
            Debug.Assert(!string.IsNullOrWhiteSpace(requestId), "requestId must not be null or whitespace");
            Debug.Assert(result != null, "result must not be null");

            result.ProjectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            string resultJson = JsonConvert.SerializeObject(result, Formatting.None);
            sessionStateService.StoreCompileResult(
                requestId,
                forceRecompile,
                resultJson,
                DateTime.UtcNow);
            bool pendingRequestCleared =
                sessionStateService.ClearPendingCompileRequestIfMatches(requestId);
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
                    pending_request_cleared = pendingRequestCleared
                },
                correlationId);
        }

        private static UnityCliLoopCompileResult CreateForceCompileResult(CompileResult result)
        {
            return new UnityCliLoopCompileResult
            {
                Success = result.Success,
                ErrorCount = null,
                WarningCount = null,
                Errors = null,
                Warnings = null,
                Message = null
            };
        }

        private static UnityCliLoopCompileIssue[] ToIssues(UnityEditor.Compilation.CompilerMessage[] messages)
        {
            if (messages == null)
            {
                return null;
            }

            return messages.Select(message => new UnityCliLoopCompileIssue
            {
                Message = message.message,
                File = message.file,
                Line = message.line
            }).ToArray();
        }
    }

    /// <summary>
    /// Describes whether a compile controller should record its result for delayed CLI polling.
    /// </summary>
    internal readonly struct CompileResultRecordingContext
    {
        private CompileResultRecordingContext(bool enabled, string requestId, bool forceRecompile)
        {
            Enabled = enabled;
            RequestId = requestId;
            ForceRecompile = forceRecompile;
        }

        internal bool Enabled { get; }
        internal string RequestId { get; }
        internal bool ForceRecompile { get; }

        internal static CompileResultRecordingContext Disabled()
        {
            return new CompileResultRecordingContext(false, "", false);
        }

        internal static CompileResultRecordingContext Create(UnityCliLoopCompileRequest request)
        {
            Debug.Assert(request != null, "request must not be null");

            if (!request.WaitForDomainReload || string.IsNullOrWhiteSpace(request.RequestId))
            {
                return Disabled();
            }

            return new CompileResultRecordingContext(
                true,
                request.RequestId,
                request.ForceRecompile);
        }
    }
}
