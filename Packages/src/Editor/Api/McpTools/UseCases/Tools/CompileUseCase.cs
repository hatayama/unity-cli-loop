using System;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Handles temporal cohesion for compilation processing
    /// Processing sequence: 1. Play Mode preparation, 2. Compilation state validation, 3. Compilation execution, 4. Result formatting
    /// Related classes: CompileTool, PlayModeCompilationPreparationService, CompilationStateValidationService, CompilationExecutionService
    /// Design reference: @Packages/docs/ARCHITECTURE_Unity.md - UseCase + Tool Pattern (DDD Integration)
    /// </summary>
    public class CompileUseCase : AbstractUseCase<CompileSchema, CompileResponse>
    {
        private const int MAX_WAIT_MS = 5000;
        private const int POLL_INTERVAL_MS = 50;

        /// <summary>
        /// Executes compilation processing
        /// </summary>
        /// <param name="parameters">Compilation parameters</param>
        /// <param name="ct">Cancellation control token</param>
        /// <returns>Compilation result</returns>
        public override async Task<CompileResponse> ExecuteAsync(CompileSchema parameters, CancellationToken ct)
        {
            PrepareResultStorage(parameters);

            // 1. Play Mode preparation check
            PlayModeCompilationPreparationService preparationService = new();
            PreparationResult preparation = preparationService.DeterminePreparationAction();

            if (!preparation.CanProceed)
            {
                CompileResponse response = new CompileResponse(
                    success: false,
                    errorCount: 1,
                    warningCount: 0,
                    errors: new[] { new CompileIssue(preparation.ErrorMessage, "", 0) },
                    warnings: Array.Empty<CompileIssue>()
                );
                return PersistResponseIfNeeded(parameters, response);
            }

            if (preparation.NeedsPlayModeStop)
            {
                preparationService.StopPlayMode();
                bool exited = await WaitForPlayModeExitAsync(ct);
                if (!exited)
                {
                    CompileResponse response = new CompileResponse(
                        success: false,
                        errorCount: 1,
                        warningCount: 0,
                        errors: new[] { new CompileIssue("Play Mode did not exit within 5 seconds; compilation aborted.", "", 0) },
                        warnings: Array.Empty<CompileIssue>()
                    );
                    return PersistResponseIfNeeded(parameters, response);
                }
            }

            // 2. Compilation state validation
            CompilationStateValidationService validationService = new();
            ValidationResult validation = validationService.ValidateCompilationState();
            
            if (!validation.IsValid)
            {
                CompileResponse response = new CompileResponse(
                    success: false,
                    errorCount: 1,
                    warningCount: 0,
                    errors: new[] { new CompileIssue(validation.ErrorMessage, "", 0) },
                    warnings: Array.Empty<CompileIssue>()
                );
                return PersistResponseIfNeeded(parameters, response);
            }

            // 3. Compilation execution
            ct.ThrowIfCancellationRequested();
            CompilationExecutionService executionService = new();
            CompileResult result = await executionService.ExecuteCompilationAsync(parameters.ForceRecompile, ct);
            
            // 4. Result formatting
            if (result.IsIndeterminate)
            {
                CompileResponse response = new CompileResponse(
                    success: result.Success,
                    errorCount: result.ErrorCount,
                    warningCount: result.WarningCount,
                    errors: null,
                    warnings: null,
                    message: result.Message ?? "Compilation status is indeterminate. Use get-logs tool to check results."
                );
                return PersistResponseIfNeeded(parameters, response);
            }
            
            CompileIssue[] errors = result.error?.Select(e => new CompileIssue(e.message, e.file, e.line)).ToArray();
            CompileIssue[] warnings = result.warning?.Select(w => new CompileIssue(w.message, w.file, w.line)).ToArray();
            
            CompileResponse successResponse = new CompileResponse(
                success: result.Success,
                errorCount: result.error?.Length ?? 0,
                warningCount: result.warning?.Length ?? 0,
                errors: errors,
                warnings: warnings
            );
            return PersistResponseIfNeeded(parameters, successResponse);
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

        private static void PrepareResultStorage(CompileSchema parameters)
        {
            Debug.Assert(parameters != null, "parameters must not be null");

            CompileResultPersistenceService.ClearStaleResults();

            if (!parameters.WaitForDomainReload)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(parameters.RequestId) && IsRequestIdSafe(parameters.RequestId))
            {
                return;
            }

            parameters.RequestId = CreateRequestId();
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

        private static CompileResponse PersistResponseIfNeeded(CompileSchema parameters, CompileResponse response)
        {
            Debug.Assert(parameters != null, "parameters must not be null");
            Debug.Assert(response != null, "response must not be null");

            if (!parameters.WaitForDomainReload)
            {
                return response;
            }

            response.ProjectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            if (string.IsNullOrWhiteSpace(parameters.RequestId))
            {
                return response;
            }

            CompileResultPersistenceService.SaveResult(parameters.RequestId, response);
            return response;
        }

        private static string CreateRequestId()
        {
            long unixTimeMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string correlationId = McpConstants.GenerateCorrelationId();
            return $"compile_{unixTimeMilliseconds}_{correlationId}";
        }
    }
}
