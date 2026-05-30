using System;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;
using UnityEditor;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Handles temporal cohesion for compilation processing
    /// Processing sequence: 1. Play Mode preparation, 2. Compilation state validation, 3. Compilation execution, 4. Result formatting
    /// Related classes: CompileTool, PlayModeCompilationPreparationService, CompilationStateValidationService, CompilationExecutionService
    /// </summary>
    public class CompileUseCase : IUnityCliLoopCompilationService
    {
        private const int MAX_WAIT_MS = 5000;
        private const int POLL_INTERVAL_MS = 50;
        private readonly UnityCliLoopEditorSessionStateService _sessionStateService;

        public CompileUseCase()
        {
            _sessionStateService =
                new UnityCliLoopEditorSessionStateService(new UnityCliLoopEditorSessionStateRepository());
        }

        /// <summary>
        /// Executes compilation processing
        /// </summary>
        /// <param name="parameters">Compilation parameters</param>
        /// <param name="ct">Cancellation control token</param>
        /// <returns>Compilation result</returns>
        public async Task<UnityCliLoopCompileResult> CompileAsync(UnityCliLoopCompileRequest request, CancellationToken ct)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            string originalRequestId = request.RequestId;
            PrepareResultStorage(request);
            string correlationId = ResolveCorrelationId(request);
            LogCompileResultStoragePrepared(request, originalRequestId, correlationId);
            bool resultPersistenceCompleted = false;

            try
            {
                bool clearedExpiredPendingRequest =
                    _sessionStateService.ClearExpiredPendingCompileRequest(DateTime.UtcNow);
                if (clearedExpiredPendingRequest)
                {
                    VibeLogger.LogWarning(
                        "compile_expired_pending_request_cleared",
                        "Cleared an expired pending compile request before accepting a new compile request.",
                        BuildCompileLogContext(request),
                        correlationId);
                }

                MarkPendingCompileRequestIfNeeded(request, correlationId);
                LogCompileRequestReceived(request, correlationId);

                // 1. Play Mode preparation check
                PlayModeCompilationPreparationService preparationService = new();
                PreparationResult preparation = preparationService.DeterminePreparationAction();

                if (!preparation.CanProceed)
                {
                    VibeLogger.LogWarning(
                        "compile_preparation_failed",
                        preparation.ErrorMessage,
                        BuildCompileLogContext(request),
                        correlationId);
                    UnityCliLoopCompileResult response = CreateCompileResult(
                        false,
                        1,
                        0,
                        new[] { CreateIssue(preparation.ErrorMessage, "", 0) },
                        Array.Empty<UnityCliLoopCompileIssue>(),
                        null);
                    UnityCliLoopCompileResult persistedResponse =
                        PersistResponseIfNeeded(request, response, correlationId);
                    resultPersistenceCompleted = true;
                    return persistedResponse;
                }

                if (preparation.NeedsPlayModeStop)
                {
                    VibeLogger.LogInfo(
                        "compile_playmode_stop_requested",
                        "Stopping Play Mode before compile.",
                        BuildCompileLogContext(request),
                        correlationId);
                    preparationService.StopPlayMode();
                    bool exited = await WaitForPlayModeExitAsync(ct);
                    VibeLogger.LogInfo(
                        "compile_playmode_exit_observed",
                        exited ? "Play Mode exited before compile." : "Play Mode did not exit before compile.",
                        new
                        {
                            request_id = request.RequestId,
                            exited
                        },
                        correlationId);
                    if (!exited)
                    {
                        UnityCliLoopCompileResult response = CreateCompileResult(
                            false,
                            1,
                            0,
                            new[] { CreateIssue("Play Mode did not exit within 5 seconds; compilation aborted.", "", 0) },
                            Array.Empty<UnityCliLoopCompileIssue>(),
                            null);
                        UnityCliLoopCompileResult persistedResponse =
                            PersistResponseIfNeeded(request, response, correlationId);
                        resultPersistenceCompleted = true;
                        return persistedResponse;
                    }
                }

                // 2. Compilation state validation
                CompilationStateValidationService validationService = new();
                ValidationResult validation = validationService.ValidateCompilationState();

                if (!validation.IsValid)
                {
                    VibeLogger.LogWarning(
                        "compile_state_validation_failed",
                        validation.ErrorMessage,
                        BuildCompileLogContext(request),
                        correlationId);
                    UnityCliLoopCompileResult response = CreateCompileResult(
                        false,
                        1,
                        0,
                        new[] { CreateIssue(validation.ErrorMessage, "", 0) },
                        Array.Empty<UnityCliLoopCompileIssue>(),
                        null);
                    UnityCliLoopCompileResult persistedResponse =
                        PersistResponseIfNeeded(request, response, correlationId);
                    resultPersistenceCompleted = true;
                    return persistedResponse;
                }

                // 3. Compilation execution
                ct.ThrowIfCancellationRequested();
                VibeLogger.LogInfo(
                    "compile_execution_start",
                    "Starting Unity compilation execution.",
                    BuildCompileLogContext(request),
                    correlationId);
                CompilationExecutionService executionService = new();
                CompileResult result = await executionService.ExecuteCompilationAsync(request.ForceRecompile, ct);
                VibeLogger.LogInfo(
                    "compile_execution_completed",
                    "Unity compilation execution completed.",
                    new
                    {
                        request_id = request.RequestId,
                        success = result.Success,
                        error_count = result.ErrorCount,
                        warning_count = result.WarningCount,
                        is_indeterminate = result.IsIndeterminate
                    },
                    correlationId);

                // 4. Result formatting
                if (result.IsIndeterminate)
                {
                    UnityCliLoopCompileResult response = CreateCompileResult(
                        result.Success,
                        result.ErrorCount,
                        result.WarningCount,
                        null,
                        null,
                        result.Message ?? "Compilation status is indeterminate. Use get-logs tool to check results.");
                    UnityCliLoopCompileResult persistedResponse =
                        PersistResponseIfNeeded(request, response, correlationId);
                    resultPersistenceCompleted = true;
                    return persistedResponse;
                }

                UnityCliLoopCompileIssue[] errors = result.error?.Select(e => CreateIssue(e.message, e.file, e.line)).ToArray();
                UnityCliLoopCompileIssue[] warnings = result.warning?.Select(w => CreateIssue(w.message, w.file, w.line)).ToArray();

                UnityCliLoopCompileResult successResponse = CreateCompileResult(
                    result.Success,
                    result.error?.Length ?? 0,
                    result.warning?.Length ?? 0,
                    errors,
                    warnings,
                    null);
                UnityCliLoopCompileResult persistedSuccessResponse =
                    PersistResponseIfNeeded(request, successResponse, correlationId);
                resultPersistenceCompleted = true;
                return persistedSuccessResponse;
            }
            finally
            {
                ClearPendingCompileRequestAfterCancellation(request, ct, correlationId);
                ClearPendingCompileRequestAfterInterruptedCompile(
                    request,
                    resultPersistenceCompleted,
                    ct,
                    correlationId);
            }
        }

        private static UnityCliLoopCompileResult CreateCompileResult(
            bool? success,
            int? errorCount,
            int? warningCount,
            UnityCliLoopCompileIssue[] errors,
            UnityCliLoopCompileIssue[] warnings,
            string message)
        {
            return new UnityCliLoopCompileResult
            {
                Success = success,
                ErrorCount = errorCount,
                WarningCount = warningCount,
                Errors = errors,
                Warnings = warnings,
                Message = message,
            };
        }

        private static UnityCliLoopCompileIssue CreateIssue(string message, string file, int line)
        {
            return new UnityCliLoopCompileIssue
            {
                Message = message,
                File = file,
                Line = line,
            };
        }

        private async Task<bool> WaitForPlayModeExitAsync(CancellationToken ct)
        {
            int waitedMs = 0;

            while (EditorApplication.isPlaying && waitedMs < MAX_WAIT_MS)
            {
                ct.ThrowIfCancellationRequested();
                await TimerDelay.Wait(POLL_INTERVAL_MS, ct);
                waitedMs += POLL_INTERVAL_MS;
            }

            return !EditorApplication.isPlaying;
        }

        private static void PrepareResultStorage(UnityCliLoopCompileRequest request)
        {
            Debug.Assert(request != null, "request must not be null");

            CompileResultPersistenceService.ClearStaleResults();

            if (!request.WaitForDomainReload)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(request.RequestId) && CompileRequestIdRules.IsSafe(request.RequestId))
            {
                return;
            }

            request.RequestId = CreateRequestId();
        }

        private UnityCliLoopCompileResult PersistResponseIfNeeded(
            UnityCliLoopCompileRequest request,
            UnityCliLoopCompileResult response,
            string correlationId)
        {
            Debug.Assert(request != null, "request must not be null");
            Debug.Assert(response != null, "response must not be null");

            if (!request.WaitForDomainReload)
            {
                VibeLogger.LogInfo(
                    "compile_result_returned_without_domain_reload_wait",
                    "Returning compile result without delayed domain reload wait.",
                    new
                    {
                        success = response.Success,
                        error_count = response.ErrorCount,
                        warning_count = response.WarningCount
                    },
                    correlationId);
                return response;
            }

            response.ProjectRoot = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));

            if (string.IsNullOrWhiteSpace(request.RequestId))
            {
                VibeLogger.LogWarning(
                    "compile_result_not_persisted",
                    "Compile result was not persisted because the request ID is empty.",
                    null,
                    correlationId);
                return response;
            }

            VibeLogger.LogInfo(
                "compile_result_persist_start",
                "Persisting compile result for CLI domain reload wait.",
                new
                {
                    request_id = request.RequestId,
                    success = response.Success,
                    error_count = response.ErrorCount,
                    warning_count = response.WarningCount
                },
                correlationId);
            CompileResultPersistenceService.SaveResult(request.RequestId, response);
            bool resultExistsAfterPersist = CompileResultPersistenceService.ResultExists(request.RequestId);
            _sessionStateService.ClearPendingCompileRequestIfMatches(request.RequestId);
            VibeLogger.LogInfo(
                "compile_result_persisted",
                "Compile result persisted for CLI domain reload wait.",
                new
                {
                    request_id = request.RequestId,
                    success = response.Success,
                    error_count = response.ErrorCount,
                    warning_count = response.WarningCount,
                    result_exists_after_persist = resultExistsAfterPersist
                },
                correlationId);
            return response;
        }

        private void MarkPendingCompileRequestIfNeeded(
            UnityCliLoopCompileRequest request,
            string correlationId)
        {
            Debug.Assert(request != null, "request must not be null");

            if (!request.WaitForDomainReload)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(request.RequestId))
            {
                return;
            }

            _sessionStateService.MarkPendingCompileRequest(request.RequestId, request.ForceRecompile);
            UnityCliLoopPendingCompileRequest pendingCompileRequest =
                _sessionStateService.GetPendingCompileRequest();
            VibeLogger.LogInfo(
                "compile_pending_request_marked",
                "Marked compile request for Domain Reload recovery.",
                new
                {
                    request_id = pendingCompileRequest.RequestId,
                    force_recompile = pendingCompileRequest.ForceRecompile,
                    expires_at_utc_ticks = pendingCompileRequest.ExpiresAtUtcTicks
                },
                correlationId);
        }

        private void ClearPendingCompileRequestAfterCancellation(
            UnityCliLoopCompileRequest request,
            CancellationToken ct,
            string correlationId)
        {
            Debug.Assert(request != null, "request must not be null");

            if (!ShouldClearPendingCompileRequestAfterCancellation(
                    request,
                    ct.IsCancellationRequested,
                    _sessionStateService.GetIsDomainReloadInProgress()))
            {
                return;
            }

            VibeLogger.LogWarning(
                "compile_pending_request_cleared_after_cancellation",
                "Cleared pending compile recovery because the caller cancelled before Domain Reload started.",
                BuildCompileLogContext(request),
                correlationId);
            _sessionStateService.ClearPendingCompileRequestIfMatches(request.RequestId);
        }

        private void ClearPendingCompileRequestAfterInterruptedCompile(
            UnityCliLoopCompileRequest request,
            bool resultPersistenceCompleted,
            CancellationToken ct,
            string correlationId)
        {
            Debug.Assert(request != null, "request must not be null");

            if (!ShouldClearPendingCompileRequestAfterInterruptedCompile(
                    request,
                    resultPersistenceCompleted,
                    ct.IsCancellationRequested,
                    _sessionStateService.GetIsDomainReloadInProgress()))
            {
                return;
            }

            VibeLogger.LogWarning(
                "compile_pending_request_cleared_after_interruption",
                "Cleared pending compile recovery because compilation stopped before Domain Reload started.",
                BuildCompileLogContext(request),
                correlationId);
            _sessionStateService.ClearPendingCompileRequestIfMatches(request.RequestId);
        }

        internal static bool ShouldClearPendingCompileRequestAfterCancellation(
            UnityCliLoopCompileRequest request,
            bool isCancellationRequested,
            bool isDomainReloadInProgress)
        {
            Debug.Assert(request != null, "request must not be null");

            if (!isCancellationRequested)
            {
                return false;
            }

            if (isDomainReloadInProgress)
            {
                return false;
            }

            if (!request.WaitForDomainReload)
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(request.RequestId);
        }

        internal static bool ShouldClearPendingCompileRequestAfterInterruptedCompile(
            UnityCliLoopCompileRequest request,
            bool resultPersistenceCompleted,
            bool isCancellationRequested,
            bool isDomainReloadInProgress)
        {
            Debug.Assert(request != null, "request must not be null");

            if (resultPersistenceCompleted)
            {
                return false;
            }

            if (isCancellationRequested)
            {
                return false;
            }

            if (isDomainReloadInProgress)
            {
                return false;
            }

            if (!request.WaitForDomainReload)
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(request.RequestId);
        }

        private static string ResolveCorrelationId(UnityCliLoopCompileRequest request)
        {
            Debug.Assert(request != null, "request must not be null");

            if (!string.IsNullOrWhiteSpace(request.RequestId))
            {
                return request.RequestId;
            }

            return VibeLogger.GenerateCorrelationId();
        }

        private static object BuildCompileLogContext(UnityCliLoopCompileRequest request)
        {
            Debug.Assert(request != null, "request must not be null");

            return new
            {
                request_id = request.RequestId,
                force_recompile = request.ForceRecompile,
                wait_for_domain_reload = request.WaitForDomainReload
            };
        }

        private static void LogCompileRequestReceived(UnityCliLoopCompileRequest request, string correlationId)
        {
            VibeLogger.LogInfo(
                "compile_request_received",
                "Compile request received.",
                BuildCompileLogContext(request),
                correlationId,
                humanNote: "Compile request entered the Unity-side use case.",
                aiTodo: "Compare this point with compile_execution_start, compile_controller_waiting_for_finish, domain_reload_start, and domain_reload_complete.");
        }

        private static void LogCompileResultStoragePrepared(
            UnityCliLoopCompileRequest request,
            string originalRequestId,
            string correlationId)
        {
            VibeLogger.LogInfo(
                "compile_result_storage_prepared",
                "Prepared compile result storage before execution.",
                new
                {
                    request_id = request.RequestId,
                    original_request_id = originalRequestId,
                    request_id_changed = request.RequestId != originalRequestId,
                    force_recompile = request.ForceRecompile,
                    wait_for_domain_reload = request.WaitForDomainReload
                },
                correlationId);
        }

        private static string CreateRequestId()
        {
            long unixTimeMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string correlationId = UnityCliLoopConstants.GenerateCorrelationId();
            return $"compile_{unixTimeMilliseconds}_{correlationId}";
        }
    }
}
