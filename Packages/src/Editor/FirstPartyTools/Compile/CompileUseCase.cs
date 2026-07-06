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
        private readonly UnityCliLoopCompileSessionLifecycleService _compileSessionLifecycleService;
        private readonly ICompileResultSessionRepository _compileResultSessionRepository;
        private readonly IPendingCompileSessionRepository _pendingCompileSessionRepository;
        private Func<CompileSchema, CancellationToken, Task<CompileResult>> _executeCompilationAsync;

        public CompileUseCase(
            UnityCliLoopCompileSessionLifecycleService compileSessionLifecycleService,
            ICompileResultSessionRepository compileResultSessionRepository,
            IPendingCompileSessionRepository pendingCompileSessionRepository)
        {
            Debug.Assert(compileSessionLifecycleService != null, "compileSessionLifecycleService must not be null");
            Debug.Assert(compileResultSessionRepository != null, "compileResultSessionRepository must not be null");
            Debug.Assert(pendingCompileSessionRepository != null, "pendingCompileSessionRepository must not be null");

            _compileSessionLifecycleService = compileSessionLifecycleService ??
                throw new ArgumentNullException(nameof(compileSessionLifecycleService));
            _compileResultSessionRepository = compileResultSessionRepository ??
                throw new ArgumentNullException(nameof(compileResultSessionRepository));
            _pendingCompileSessionRepository = pendingCompileSessionRepository ??
                throw new ArgumentNullException(nameof(pendingCompileSessionRepository));
            _executeCompilationAsync = ExecuteCompilationWithDefaultServiceAsync;
        }

        /// <summary>
        /// Replaces compilation execution for tests that must not start Unity's real compilation pipeline.
        /// </summary>
        internal void SetCompilationExecutionForTesting(Func<CompileSchema, CancellationToken, Task<CompileResult>> executeCompilationAsync)
        {
            Debug.Assert(executeCompilationAsync != null, "executeCompilationAsync must not be null");
            _executeCompilationAsync = executeCompilationAsync ??
                throw new ArgumentNullException(nameof(executeCompilationAsync));
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
            _compileSessionLifecycleService.ClearExpiredCompileResult(utcNow);
            _compileSessionLifecycleService.ClearExpiredPendingCompileRequest(utcNow);
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
                CompileResponse response = new(
                    success: false,
                    errorCount: 1,
                    warningCount: 0,
                    errors: new[] { new CompileIssue(preparation.ErrorMessage, "", 0) },
                    warnings: Array.Empty<CompileIssue>());
                CompileResponse persistedResponse =
                    StorePreControllerResponseIfNeeded(request, response, correlationId);
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
                    CompileResponse response = new(
                        success: false,
                        errorCount: 1,
                        warningCount: 0,
                        errors: new[] { new CompileIssue("Play Mode did not exit within 5 seconds; compilation aborted.", "", 0) },
                        warnings: Array.Empty<CompileIssue>());
                    CompileResponse persistedResponse =
                        StorePreControllerResponseIfNeeded(request, response, correlationId);
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
                CompileResponse response = new(
                    success: false,
                    errorCount: 1,
                    warningCount: 0,
                    errors: new[] { new CompileIssue(validation.ErrorMessage, "", 0) },
                    warnings: Array.Empty<CompileIssue>());
                CompileResponse persistedResponse =
                    StorePreControllerResponseIfNeeded(request, response, correlationId);
                return persistedResponse;
            }

            // 3. Compilation execution
            ct.ThrowIfCancellationRequested();
            CompileResult result = await _executeCompilationAsync(request, ct);

            // 4. Result formatting
            CompileResponse successResponse =
                CompileResponseFactory.CreateResponse(result, request.ForceRecompile);
            StampProjectRootForDelayedResponseIfNeeded(request, successResponse);
            return successResponse;
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

        private CompileResponse StorePreControllerResponseIfNeeded(
            CompileSchema request,
            CompileResponse response,
            string correlationId)
        {
            Debug.Assert(request != null, "request must not be null");
            Debug.Assert(response != null, "response must not be null");

            if (!CompileResultRecordingContext.CanRecord(request))
            {
                LogMissingRecordableRequestIfNeeded(request, correlationId);
                return response;
            }

            CompileResultSessionRecorder.RecordCompileResponse(
                _compileResultSessionRepository,
                _pendingCompileSessionRepository,
                request.RequestId,
                request.ForceRecompile,
                response,
                correlationId);
            return response;
        }

        private void StampProjectRootForDelayedResponseIfNeeded(
            CompileSchema request,
            CompileResponse response)
        {
            Debug.Assert(request != null, "request must not be null");
            Debug.Assert(response != null, "response must not be null");

            if (!CompileResultRecordingContext.CanRecord(request))
            {
                return;
            }

            CompileSessionResultStore.StampProjectRoot(response);
        }

        private static void LogMissingRecordableRequestIfNeeded(
            CompileSchema request,
            string correlationId)
        {
            Debug.Assert(request != null, "request must not be null");

            if (!request.WaitForDomainReload)
            {
                return;
            }

            VibeLogger.LogWarning(
                "compile_result_not_stored",
                "Compile result was not stored because the request ID is empty.",
                null,
                correlationId);
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
                _pendingCompileSessionRepository.GetPendingCompileRequests();
            _compileSessionLifecycleService.MarkPendingCompileRequest(
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

        private Task<CompileResult> ExecuteCompilationWithDefaultServiceAsync(CompileSchema request, CancellationToken ct)
        {
            Debug.Assert(request != null, "request must not be null");

            CompilationExecutionService executionService = new(
                _compileResultSessionRepository,
                _pendingCompileSessionRepository);
            return executionService.ExecuteCompilationAsync(request, ct);
        }
    }
}
