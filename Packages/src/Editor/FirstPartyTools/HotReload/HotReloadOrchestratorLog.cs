using System;
using System.Collections.Generic;
using System.Linq;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// VibeLogger wrappers for the hot-reload orchestrator pipeline.
    /// </summary>
    internal static class HotReloadOrchestratorLog
    {
        internal static void LogHotReloadFileStart(
            string projectRelativePath,
            HotReloadUnchangedSourceDecision unchangedDecision,
            string correlationId)
        {
            VibeLogger.LogInfo(
                HotReloadConstants.VibeLogFileStart,
                "Hot reload file start.",
                new
                {
                    projectRelativePath,
                    unchangedDecision = unchangedDecision.ToString()
                },
                correlationId);
        }

        internal static void LogHotReloadWorkerResult(
            TransformWorkerClientResult workerResult,
            string correlationId)
        {
            TransformWorkerOutputDto output = workerResult.Output;
            VibeLogger.LogInfo(
                HotReloadConstants.VibeLogWorkerResult,
                "Hot reload worker result.",
                new
                {
                    entryCount = output?.entries?.Length ?? 0,
                    skippedCount = output?.skipped?.Length ?? 0,
                    unchangedCount = output?.unchangedMethods?.Length ?? 0,
                    workerSuccess = workerResult.Success
                },
                correlationId);
        }

        internal static void LogHotReloadShimCompileFailed(
            HotReloadShimCompileResult compileResult,
            string stage,
            string correlationId)
        {
            string firstError = compileResult.Errors.Count > 0
                ? compileResult.Errors[0].Message
                : compileResult.ErrorMessage;
            VibeLogger.LogInfo(
                HotReloadConstants.VibeLogShimCompileFailed,
                "Hot reload shim compile failed.",
                new
                {
                    stage,
                    errorCount = compileResult.Errors.Count,
                    firstError
                },
                correlationId);
        }

        internal static void LogHotReloadIsolationRetry(
            int excludedMethodKeyCount,
            int excludedAddedMethodKeyCount,
            int retryEntryCount,
            int retrySkippedCount,
            int retryOnlySkippedCount,
            bool retryWorkerSuccess,
            string trigger,
            string correlationId)
        {
            VibeLogger.LogInfo(
                HotReloadConstants.VibeLogIsolationRetry,
                "Hot reload isolation retry.",
                new
                {
                    trigger,
                    retryWorkerSuccess,
                    excludedMethodKeyCount,
                    excludedAddedMethodKeyCount,
                    retryEntryCount,
                    retrySkippedCount,
                    retryOnlySkippedCount
                },
                correlationId);
        }

        internal static void LogHotReloadEmptyEntriesClear(
            IReadOnlyList<string> addedLabelsAtClear,
            string correlationId)
        {
            VibeLogger.LogInfo(
                HotReloadConstants.VibeLogEmptyEntriesClear,
                "Hot reload empty-entries registry clear.",
                new
                {
                    addedLabels = addedLabelsAtClear
                },
                correlationId);
        }

        internal static void LogHotReloadApplySummary(
            int patchedCount,
            int failedCount,
            int skippedCount,
            int alreadyActiveCount,
            int addedCount,
            int staleCount,
            bool success,
            string correlationId)
        {
            VibeLogger.LogInfo(
                HotReloadConstants.VibeLogApplySummary,
                "Hot reload apply summary.",
                new
                {
                    patchedCount,
                    failedCount,
                    skippedCount,
                    alreadyActiveCount,
                    addedCount,
                    staleCount,
                    success
                },
                correlationId);
        }

        // Why an anonymous array for the steps: the entry keys stay camelCase like the rest of the
        // entry, which the step type's PascalCase properties would not.
        internal static void LogHotReloadTimingDetail(HotReloadTimingDetailPayload payload, string correlationId)
        {
            VibeLogger.LogInfo(
                HotReloadConstants.VibeLogTimingDetail,
                "Hot reload timing detail.",
                new
                {
                    totalMs = payload.TotalMs,
                    analysisMs = payload.AnalysisMs,
                    shimCompileMs = payload.ShimCompileMs,
                    patchMs = payload.PatchMs,
                    otherMs = payload.OtherMs,
                    unaccountedMs = payload.UnaccountedMs,
                    groupCount = payload.GroupCount,
                    steps = payload.Steps.Select(step => new { step = step.Step, ms = step.Ms }).ToArray()
                },
                correlationId);
        }
    }
}
