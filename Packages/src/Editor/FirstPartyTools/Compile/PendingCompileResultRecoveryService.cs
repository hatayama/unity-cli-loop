using System;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    internal enum PendingCompileRecoveryStatus
    {
        Completed,
        Retry
    }

    /// <summary>
    /// Completes compile result files for requests that crossed Domain Reload before the live callback persisted them.
    /// </summary>
    internal sealed class PendingCompileResultRecoveryService
    {
        private readonly UnityCliLoopEditorSessionStateService _sessionStateService;
        private readonly Func<bool> _isEditorCompiling;
        private readonly Func<string, bool> _resultExists;
        private readonly Action<string, UnityCliLoopCompileResult> _saveResult;
        private readonly Func<string> _getProjectRoot;
        private readonly Func<DateTime> _getUtcNow;

        internal PendingCompileResultRecoveryService(
            UnityCliLoopEditorSessionStateService sessionStateService,
            Func<bool> isEditorCompiling,
            Func<string, bool> resultExists,
            Action<string, UnityCliLoopCompileResult> saveResult,
            Func<string> getProjectRoot,
            Func<DateTime> getUtcNow)
        {
            Debug.Assert(sessionStateService != null, "sessionStateService must not be null");
            Debug.Assert(isEditorCompiling != null, "isEditorCompiling must not be null");
            Debug.Assert(resultExists != null, "resultExists must not be null");
            Debug.Assert(saveResult != null, "saveResult must not be null");
            Debug.Assert(getProjectRoot != null, "getProjectRoot must not be null");
            Debug.Assert(getUtcNow != null, "getUtcNow must not be null");

            _sessionStateService = sessionStateService ?? throw new ArgumentNullException(nameof(sessionStateService));
            _isEditorCompiling = isEditorCompiling ?? throw new ArgumentNullException(nameof(isEditorCompiling));
            _resultExists = resultExists ?? throw new ArgumentNullException(nameof(resultExists));
            _saveResult = saveResult ?? throw new ArgumentNullException(nameof(saveResult));
            _getProjectRoot = getProjectRoot ?? throw new ArgumentNullException(nameof(getProjectRoot));
            _getUtcNow = getUtcNow ?? throw new ArgumentNullException(nameof(getUtcNow));
        }

        internal PendingCompileRecoveryStatus Recover(bool recoverWhileEditorCompiling)
        {
            UnityCliLoopPendingCompileRequest pendingCompileRequest =
                _sessionStateService.GetPendingCompileRequest();
            if (!pendingCompileRequest.HasRequest)
            {
                return PendingCompileRecoveryStatus.Completed;
            }

            DateTime utcNow = _getUtcNow();
            if (pendingCompileRequest.IsExpiredAt(utcNow))
            {
                VibeLogger.LogInfo(
                    "compile_pending_result_expired",
                    "Pending compile recovery expired before Domain Reload recovery could use it.",
                    new
                    {
                        request_id = pendingCompileRequest.RequestId,
                        force_recompile = pendingCompileRequest.ForceRecompile
                    },
                    pendingCompileRequest.RequestId);
                _sessionStateService.ClearPendingCompileRequestIfMatches(pendingCompileRequest.RequestId);
                return PendingCompileRecoveryStatus.Completed;
            }

            if (_isEditorCompiling() && !recoverWhileEditorCompiling)
            {
                return PendingCompileRecoveryStatus.Retry;
            }

            if (_resultExists(pendingCompileRequest.RequestId))
            {
                VibeLogger.LogInfo(
                    "compile_pending_result_already_persisted",
                    "Pending compile recovery found an existing result file and cleared SessionState.",
                    new
                    {
                        request_id = pendingCompileRequest.RequestId,
                        force_recompile = pendingCompileRequest.ForceRecompile
                    },
                    pendingCompileRequest.RequestId);
                _sessionStateService.ClearPendingCompileRequestIfMatches(pendingCompileRequest.RequestId);
                return PendingCompileRecoveryStatus.Completed;
            }

            UnityCliLoopCompileResult result = CreateIndeterminateResult(pendingCompileRequest);
            _saveResult(pendingCompileRequest.RequestId, result);
            VibeLogger.LogWarning(
                "compile_pending_result_recovered_after_domain_reload",
                result.Message,
                new
                {
                    request_id = pendingCompileRequest.RequestId,
                    force_recompile = pendingCompileRequest.ForceRecompile
                },
                pendingCompileRequest.RequestId);
            _sessionStateService.ClearPendingCompileRequestIfMatches(pendingCompileRequest.RequestId);
            return PendingCompileRecoveryStatus.Completed;
        }

        private UnityCliLoopCompileResult CreateIndeterminateResult(
            UnityCliLoopPendingCompileRequest pendingCompileRequest)
        {
            string message = pendingCompileRequest.ForceRecompile
                ? "Force compilation crossed Domain Reload before Unity CLI Loop could persist the live result. Use get-logs to inspect the compiler output."
                : "Compilation crossed Domain Reload before Unity CLI Loop could persist the live result. Use get-logs to inspect the compiler output.";

            return new UnityCliLoopCompileResult
            {
                Success = null,
                ErrorCount = null,
                WarningCount = null,
                Errors = null,
                Warnings = null,
                Message = message,
                ProjectRoot = _getProjectRoot()
            };
        }
    }
}
