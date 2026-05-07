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
    /// Design reference: @Packages/docs/ARCHITECTURE_Unity.md - UseCase + Tool Pattern (DDD Integration)
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

            // 1. Play Mode preparation check
            PlayModeCompilationPreparationService preparationService = new();
            PreparationResult preparation = preparationService.DeterminePreparationAction();

            if (!preparation.CanProceed)
            {
                UnityCliLoopCompileResult response = CreateCompileResult(
                    false,
                    1,
                    0,
                    new[] { CreateIssue(preparation.ErrorMessage, "", 0) },
                    Array.Empty<UnityCliLoopCompileIssue>(),
                    null);
                return PersistResponseIfNeeded(request, response);
            }

            if (preparation.NeedsPlayModeStop)
            {
                preparationService.StopPlayMode();
                bool exited = await WaitForPlayModeExitAsync(ct);
                if (!exited)
                {
                    UnityCliLoopCompileResult response = CreateCompileResult(
                        false,
                        1,
                        0,
                        new[] { CreateIssue("Play Mode did not exit within 5 seconds; compilation aborted.", "", 0) },
                        Array.Empty<UnityCliLoopCompileIssue>(),
                        null);
                    return PersistResponseIfNeeded(request, response);
                }
            }

            // 2. Compilation state validation
            CompilationStateValidationService validationService = new();
            ValidationResult validation = validationService.ValidateCompilationState();
            
            if (!validation.IsValid)
            {
                UnityCliLoopCompileResult response = CreateCompileResult(
                    false,
                    1,
                    0,
                    new[] { CreateIssue(validation.ErrorMessage, "", 0) },
                    Array.Empty<UnityCliLoopCompileIssue>(),
                    null);
                return PersistResponseIfNeeded(request, response);
            }

            // 3. Compilation execution
            ct.ThrowIfCancellationRequested();
            CompilationExecutionService executionService = new();
            CompileResult result = await executionService.ExecuteCompilationAsync(request.ForceRecompile, ct);
            
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
                return PersistResponseIfNeeded(request, response);
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
            return PersistResponseIfNeeded(request, successResponse);
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

        private static UnityCliLoopCompileResult PersistResponseIfNeeded(UnityCliLoopCompileRequest request, UnityCliLoopCompileResult response)
        {
            Debug.Assert(request != null, "request must not be null");
            Debug.Assert(response != null, "response must not be null");

            if (!request.WaitForDomainReload)
            {
                return response;
            }

            response.ProjectRoot = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));

            if (string.IsNullOrWhiteSpace(request.RequestId))
            {
                return response;
            }

            CompileResultPersistenceService.SaveResult(request.RequestId, response);
            return response;
        }

        private static string CreateRequestId()
        {
            long unixTimeMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string correlationId = UnityCliLoopConstants.GenerateCorrelationId();
            return $"compile_{unixTimeMilliseconds}_{correlationId}";
        }
    }
}
