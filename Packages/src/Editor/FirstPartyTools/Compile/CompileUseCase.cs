using System;
using System.Threading.Tasks;
using System.Threading;
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

            _sessionStateService.ClearExpiredCompileResult(DateTime.UtcNow);
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
                    StoreResponseIfNeeded(request, response, correlationId);
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
                        StoreResponseIfNeeded(request, response, correlationId);
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
                    StoreResponseIfNeeded(request, response, correlationId);
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
            CompileResult result = await executionService.ExecuteCompilationAsync(request, ct);
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
            UnityCliLoopCompileResult successResponse =
                CompileSessionResultService.CreateCompileResult(result, request.ForceRecompile);
            UnityCliLoopCompileResult persistedSuccessResponse =
                StoreResponseIfNeeded(request, successResponse, correlationId);
            return persistedSuccessResponse;
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

        private UnityCliLoopCompileResult StoreResponseIfNeeded(
            UnityCliLoopCompileRequest request,
            UnityCliLoopCompileResult response,
            string correlationId)
        {
            Debug.Assert(request != null, "request must not be null");
            Debug.Assert(response != null, "response must not be null");

            if (!request.WaitForDomainReload)
            {
                return response;
            }

            if (string.IsNullOrWhiteSpace(request.RequestId))
            {
                VibeLogger.LogWarning(
                    "compile_result_not_stored",
                    "Compile result was not stored because the request ID is empty.",
                    null,
                    correlationId);
                return response;
            }

            VibeLogger.LogInfo(
                "compile_result_session_state_store_start",
                "Storing compile result for CLI status polling.",
                new
                {
                    request_id = request.RequestId,
                    force_recompile = request.ForceRecompile,
                    success = response.Success,
                    error_count = response.ErrorCount,
                    warning_count = response.WarningCount
                },
                correlationId);
            CompileSessionResultService.StoreCompileResult(
                _sessionStateService,
                request.RequestId,
                request.ForceRecompile,
                response,
                correlationId);
            UnityCliLoopStoredCompileResult storedResult =
                _sessionStateService.GetCompileResult(request.RequestId);
            VibeLogger.LogInfo(
                "compile_result_session_state_stored",
                "Compile result was stored for CLI status polling.",
                new
                {
                    request_id = request.RequestId,
                    force_recompile = request.ForceRecompile,
                    success = response.Success,
                    error_count = response.ErrorCount,
                    warning_count = response.WarningCount,
                    result_exists_after_store = storedResult.HasResult
                },
                correlationId);
            return response;
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
