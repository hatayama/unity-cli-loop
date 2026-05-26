using System;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;
using UnityEditor;
using UnityEngine;

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

            PrepareResultStorage(request);
            string correlationId = ResolveCorrelationId(request);
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
                return PersistResponseIfNeeded(request, response, correlationId);
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
                    return PersistResponseIfNeeded(request, response, correlationId);
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
                return PersistResponseIfNeeded(request, response, correlationId);
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
                return PersistResponseIfNeeded(request, response, correlationId);
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
            return PersistResponseIfNeeded(request, successResponse, correlationId);
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

            if (!string.IsNullOrWhiteSpace(request.RequestId) && IsRequestIdSafe(request.RequestId))
            {
                return;
            }

            request.RequestId = CreateRequestId();
        }

        private static bool IsRequestIdSafe(string requestId)
        {
            foreach (char c in requestId)
            {
                bool isSafe = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')
                              || (c >= '0' && c <= '9') || c == '_' || c == '-';
                if (!isSafe)
                {
                    return false;
                }
            }
            return true;
        }

        private static UnityCliLoopCompileResult PersistResponseIfNeeded(
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

            CompileResultPersistenceService.SaveResult(request.RequestId, response);
            VibeLogger.LogInfo(
                "compile_result_persisted",
                "Compile result persisted for CLI domain reload wait.",
                new
                {
                    request_id = request.RequestId,
                    success = response.Success,
                    error_count = response.ErrorCount,
                    warning_count = response.WarningCount
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

        private static string CreateRequestId()
        {
            long unixTimeMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string correlationId = UnityCliLoopConstants.GenerateCorrelationId();
            return $"compile_{unixTimeMilliseconds}_{correlationId}";
        }
    }
}
