using System;
using System.Threading.Tasks;
using System.Threading;
using UnityEditor;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Handles temporal cohesion for compilation processing
    /// Processing sequence: 1. Play Mode preparation, 2. Compilation state validation, 3. Compilation execution, 4. Result formatting
    /// Related classes: CompileTool, PlayModeCompilationPreparationService, CompilationStateValidationService, CompilationExecutionService
    /// </summary>
    public class CompileUseCase
    {
        private const int MAX_WAIT_MS = 5000;
        private const int POLL_INTERVAL_MS = 50;
        private readonly UnityCliLoopEditorSessionStateService _sessionStateService;

        public CompileUseCase(UnityCliLoopEditorSessionStateService sessionStateService)
        {
            Debug.Assert(sessionStateService != null, "sessionStateService must not be null");

            _sessionStateService =
                sessionStateService ?? throw new ArgumentNullException(nameof(sessionStateService));
        }

        /// <summary>
        /// Executes compilation processing
        /// </summary>
        /// <param name="request">Compilation parameters</param>
        /// <param name="ct">Cancellation control token</param>
        /// <returns>Compilation result</returns>
        public async Task<CompileResponse> CompileAsync(CompileSchema request, CancellationToken ct)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            PrepareResultStorage(request);
            string correlationId = ResolveCorrelationId(request);
            LogCompileRequestReceived(request, correlationId);

            DateTime utcNow = DateTime.UtcNow;
            _sessionStateService.ClearExpiredCompileResult(utcNow);
            _sessionStateService.ClearExpiredPendingCompileRequest(utcNow);
            MarkPendingCompileRequestIfNeeded(request, utcNow, correlationId);

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
                CompileResponse response = CreateCompileResult(
                    false,
                    1,
                    0,
                    new[] { CreateIssue(preparation.ErrorMessage, "", 0) },
                    Array.Empty<CompileIssue>(),
                    null);
                CompileResponse persistedResponse =
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
                    CompileResponse response = CreateCompileResult(
                        false,
                        1,
                        0,
                        new[] { CreateIssue("Play Mode did not exit within 5 seconds; compilation aborted.", "", 0) },
                        Array.Empty<CompileIssue>(),
                        null);
                    CompileResponse persistedResponse =
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
                CompileResponse response = CreateCompileResult(
                    false,
                    1,
                    0,
                    new[] { CreateIssue(validation.ErrorMessage, "", 0) },
                    Array.Empty<CompileIssue>(),
                    null);
                CompileResponse persistedResponse =
                    StoreResponseIfNeeded(request, response, correlationId);
                return persistedResponse;
            }

            // 3. Compilation execution
            ct.ThrowIfCancellationRequested();
            CompilationExecutionService executionService = new(_sessionStateService);
            CompileResult result = await executionService.ExecuteCompilationAsync(request, ct);

            // 4. Result formatting
            CompileResponse successResponse =
                CompileSessionResultService.CreateCompileResult(result, request.ForceRecompile);
            CompileResponse persistedSuccessResponse =
                StoreResponseIfNeeded(request, successResponse, correlationId);
            return persistedSuccessResponse;
        }

        private static CompileResponse CreateCompileResult(
            bool? success,
            int? errorCount,
            int? warningCount,
            CompileIssue[] errors,
            CompileIssue[] warnings,
            string message)
        {
            return new CompileResponse
            {
                Success = success,
                ErrorCount = errorCount,
                WarningCount = warningCount,
                Errors = errors,
                Warnings = warnings,
                Message = message,
            };
        }

        private static CompileIssue CreateIssue(string message, string file, int line)
        {
            return new CompileIssue
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

        private static void PrepareResultStorage(CompileSchema request)
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

        private CompileResponse StoreResponseIfNeeded(
            CompileSchema request,
            CompileResponse response,
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

            CompileSessionResultService.StoreCompileResult(
                _sessionStateService,
                request.RequestId,
                request.ForceRecompile,
                response,
                correlationId);
            return response;
        }

        private void MarkPendingCompileRequestIfNeeded(
            CompileSchema request,
            DateTime markedAtUtc,
            string correlationId)
        {
            Debug.Assert(request != null, "request must not be null");
            Debug.Assert(markedAtUtc.Kind == DateTimeKind.Utc, "markedAtUtc must be UTC");

            if (!request.WaitForDomainReload)
            {
                return;
            }

            Debug.Assert(!string.IsNullOrWhiteSpace(request.RequestId), "request.RequestId must not be null or whitespace");
            UnityCliLoopPendingCompileRequest[] previousPendingRequests =
                _sessionStateService.GetPendingCompileRequests();
            _sessionStateService.MarkPendingCompileRequest(
                request.RequestId,
                request.ForceRecompile,
                markedAtUtc);
            VibeLogger.LogInfo(
                "compile_request_registered_for_status_polling",
                "Registered compile request for CLI status polling.",
                new
                {
                    request_id = request.RequestId,
                    force_recompile = request.ForceRecompile,
                    pending_request_replaced = false,
                    pending_request_count_before = previousPendingRequests.Length,
                    previous_request_id = previousPendingRequests.Length > 0
                        ? previousPendingRequests[0].RequestId
                        : ""
                },
                correlationId);
        }

        private static void LogCompileRequestReceived(
            CompileSchema request,
            string correlationId)
        {
            Debug.Assert(request != null, "request must not be null");

            VibeLogger.LogInfo(
                "compile_request_received",
                "Received compile request from CLI.",
                new
                {
                    request_id = request.RequestId,
                    force_recompile = request.ForceRecompile,
                    wait_for_domain_reload = request.WaitForDomainReload,
                    stop_on_external_scene_changes = !request.ReloadExternalSceneChanges,
                    is_compiling = EditorApplication.isCompiling,
                    is_updating = EditorApplication.isUpdating
                },
                correlationId);
        }

        private static string ResolveCorrelationId(CompileSchema request)
        {
            Debug.Assert(request != null, "request must not be null");

            if (!string.IsNullOrWhiteSpace(request.RequestId))
            {
                return request.RequestId;
            }

            return VibeLogger.GenerateCorrelationId();
        }

        private static object BuildCompileLogContext(CompileSchema request)
        {
            Debug.Assert(request != null, "request must not be null");

            return new
            {
                request_id = request.RequestId,
                force_recompile = request.ForceRecompile,
                wait_for_domain_reload = request.WaitForDomainReload,
                reload_external_scene_changes = request.ReloadExternalSceneChanges
            };
        }

        private static string CreateRequestId()
        {
            long unixTimeMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string correlationId = UnityCliLoopConstants.GenerateCorrelationId();
            return $"compile_{unixTimeMilliseconds}_{correlationId}";
        }
    }
}
