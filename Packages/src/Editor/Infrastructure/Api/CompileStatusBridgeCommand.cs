using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Reports compile completion state for the CLI without entering the normal tool execution slot.
    /// </summary>
    internal static class CompileStatusBridgeCommand
    {
        private const string RequestIdParamName = "RequestId";
        private const string RecoveredCompileResultMessage =
            "Compilation completed, but Unity reloaded scripts before " + UnityCliLoopConstants.DISPLAY_NAME + " could record detailed errors or warnings. Use get-logs to inspect the compiler output.";

        public static GetCompileStatusResponse Execute(JToken paramsToken)
        {
            string requestId = ReadRequestId(paramsToken);
            UnityCliLoopCompileSessionLifecycleService compileSessionLifecycleService =
                UnityCliLoopCompileSessionLifecycleFacade.Service;
            compileSessionLifecycleService.ClearExpiredCompileResult(DateTime.UtcNow);
            ISessionFlagsRepository sessionFlagsRepository =
                UnityCliLoopSessionFlagsFacade.Repository;
            ICompileResultSessionRepository compileResultSessionRepository =
                UnityCliLoopCompileResultSessionRepositoryFacade.Repository;
            IPendingCompileSessionRepository pendingCompileSessionRepository =
                UnityCliLoopPendingCompileSessionRepositoryFacade.Repository;
            bool isCompiling = EditorApplication.isCompiling;
            bool isUpdating = EditorApplication.isUpdating;
            bool isDomainReloadInProgress =
                sessionFlagsRepository.GetIsDomainReloadInProgress() ||
                DomainReloadStateRegistry.IsDomainReloadInProgress();
            bool isSnapshotCaptureHolding =
                HotReloadSnapshotCaptureCoordination.IsHoldingCompileCompletion?.Invoke() ?? false;
            double compileStartPendingSeconds =
                CompileStartPendingCoordination.ReadPendingSeconds?.Invoke() ?? 0d;
            GetCompileStatusResponse response = BuildResponse(
                requestId,
                isCompiling,
                isUpdating,
                isDomainReloadInProgress,
                isSnapshotCaptureHolding,
                compileStartPendingSeconds,
                compileSessionLifecycleService,
                compileResultSessionRepository,
                pendingCompileSessionRepository,
                DomainReloadDurationMonitor.CreateForEditor());
            LogCompileStatusQueryReceived(requestId, response, isSnapshotCaptureHolding);
            return response;
        }

        internal static GetCompileStatusResponse BuildResponse(
            string requestId,
            bool isCompiling,
            bool isUpdating,
            bool isDomainReloadInProgress,
            bool isSnapshotCaptureHolding,
            double compileStartPendingSeconds,
            UnityCliLoopCompileSessionLifecycleService compileSessionLifecycleService,
            ICompileResultSessionRepository compileResultSessionRepository,
            IPendingCompileSessionRepository pendingCompileSessionRepository,
            DomainReloadDurationMonitor reloadDurationMonitor)
        {
            Debug.Assert(compileSessionLifecycleService != null, "compileSessionLifecycleService must not be null");
            Debug.Assert(compileResultSessionRepository != null, "compileResultSessionRepository must not be null");
            Debug.Assert(pendingCompileSessionRepository != null, "pendingCompileSessionRepository must not be null");
            Debug.Assert(reloadDurationMonitor != null, "reloadDurationMonitor must not be null");
            Debug.Assert(compileStartPendingSeconds >= 0d, "compileStartPendingSeconds must not be negative");

            // The hot reload snapshot capture must finish before compile returns, or an edit made
            // right after the CLI returns could be captured as the source the compiler read.
            bool isEditorSettled = !isUpdating && !isDomainReloadInProgress && !isSnapshotCaptureHolding;
            bool ready = !isCompiling && isEditorSettled;
            compileSessionLifecycleService.ClearExpiredPendingCompileRequest(DateTime.UtcNow);
            UnityCliLoopStoredCompileResult storedResult = string.IsNullOrWhiteSpace(requestId)
                ? UnityCliLoopStoredCompileResult.None()
                : compileResultSessionRepository.GetCompileResult(requestId);
            if (ready && !storedResult.HasResult)
            {
                storedResult = RecoverPendingCompileResult(
                    requestId,
                    compileResultSessionRepository,
                    pendingCompileSessionRepository,
                    reloadDurationMonitor);
            }

            JToken result = storedResult.HasResult ? JToken.Parse(storedResult.ResultJson) : null;
            // A COMPILE_NOT_STARTED abort leaves the compile queued inside Unity, so isCompiling stays
            // true indefinitely; the CLI must still receive that result instead of waiting it out.
            // Only that gate is lifted: an asset refresh, domain reload, or snapshot capture in
            // progress still holds the result back, as it does for every other stored result.
            if (IsCompileNotStartedResult(result))
            {
                ready = isEditorSettled;
            }

            if (result is JObject resultObject &&
                SettleReloadAttribution(resultObject, storedResult, ready, reloadDurationMonitor))
            {
                // Why store it back: the result outlives this read (a timed-out CLI collects it later), and a
                // later read must report the same reload, not whichever one ran last. The original completion
                // time keeps the result's expiry where it was.
                compileResultSessionRepository.StoreCompileResult(
                    storedResult.RequestId,
                    storedResult.ForceRecompile,
                    resultObject.ToString(Formatting.None),
                    new DateTime(storedResult.CompletedAtUtcTicks, DateTimeKind.Utc),
                    storedResult.IsFollowedByReload);
            }

            return new GetCompileStatusResponse
            {
                Ready = ready,
                HasResult = storedResult.HasResult,
                IsCompiling = isCompiling,
                IsUpdating = isUpdating,
                IsDomainReloadInProgress = isDomainReloadInProgress,
                CompileStartPendingSeconds = compileStartPendingSeconds,
                Result = result,
                Message = CreateMessage(ready, storedResult.HasResult)
            };
        }

        private static bool IsCompileNotStartedResult(JToken result)
        {
            if (result is not JObject resultObject)
            {
                return false;
            }

            JToken errorCode = resultObject["ErrorCode"];
            if (errorCode == null || errorCode.Type != JTokenType.String)
            {
                return false;
            }

            return string.Equals(
                errorCode.Value<string>(),
                CompileLifecycleErrorCodes.CompileNotStartedErrorCodeText,
                StringComparison.Ordinal);
        }

        private static string ReadRequestId(JToken paramsToken)
        {
            if (paramsToken is not JObject paramsObject)
            {
                return "";
            }

            JToken requestIdToken = paramsObject.GetValue(RequestIdParamName, StringComparison.OrdinalIgnoreCase);
            return requestIdToken?.ToString() ?? "";
        }

        private static void LogCompileStatusQueryReceived(
            string requestId,
            GetCompileStatusResponse response,
            bool isSnapshotCaptureHolding)
        {
            Debug.Assert(response != null, "response must not be null");

            VibeLogger.LogInfo(
                "compile_status_query_received",
                "Received compile status polling request from CLI.",
                new
                {
                    request_id = requestId,
                    ready = response.Ready,
                    has_result = response.HasResult,
                    is_compiling = response.IsCompiling,
                    is_updating = response.IsUpdating,
                    is_domain_reload_in_progress = response.IsDomainReloadInProgress,
                    is_snapshot_capture_holding = isSnapshotCaptureHolding,
                    compile_start_pending_seconds = response.CompileStartPendingSeconds,
                    message = response.Message
                },
                requestId);
        }

        /// <summary>
        /// Sets which reload the result reports. Returns true when that is settled for good and the result must be
        /// stored back; false when it was settled before, or when this read reports null only for now.
        /// </summary>
        private static bool SettleReloadAttribution(
            JObject result,
            UnityCliLoopStoredCompileResult storedResult,
            bool ready,
            DomainReloadDurationMonitor reloadDurationMonitor)
        {
            if (!storedResult.IsFollowedByReload)
            {
                // Why: this compile was skipped, stopped, or had errors, so a reload after it has another cause.
                return reloadDurationMonitor.AttributeNoReloadTo(result);
            }

            // Why the first reload since the result: the compile stores its result just before Unity starts the
            // reload it causes, so any later reload has another cause, however late the result is read.
            ReloadAttributionOutcome outcome =
                reloadDurationMonitor.AttributeFirstReloadSince(result, storedResult.CompletedAtUtcTicks);
            if (outcome != ReloadAttributionOutcome.NoReloadYet)
            {
                return outcome == ReloadAttributionOutcome.Settled;
            }

            if (ready)
            {
                // Why: the Editor settled without the reload this compile should have caused, so a later reload
                // has another cause.
                return reloadDurationMonitor.AttributeNoReloadTo(result);
            }

            // The reload this compile causes has not finished yet, so this read reports none and a later read
            // settles it.
            reloadDurationMonitor.AttributeNoReloadTo(result);
            return false;
        }

        private static UnityCliLoopStoredCompileResult RecoverPendingCompileResult(
            string requestId,
            ICompileResultSessionRepository compileResultSessionRepository,
            IPendingCompileSessionRepository pendingCompileSessionRepository,
            DomainReloadDurationMonitor reloadDurationMonitor)
        {
            Debug.Assert(compileResultSessionRepository != null, "compileResultSessionRepository must not be null");
            Debug.Assert(pendingCompileSessionRepository != null, "pendingCompileSessionRepository must not be null");
            Debug.Assert(reloadDurationMonitor != null, "reloadDurationMonitor must not be null");

            if (string.IsNullOrWhiteSpace(requestId))
            {
                return UnityCliLoopStoredCompileResult.None();
            }

            UnityCliLoopPendingCompileRequest pendingRequest =
                pendingCompileSessionRepository.GetPendingCompileRequestForRequestId(requestId);
            if (!pendingRequest.HasRequest)
            {
                return UnityCliLoopStoredCompileResult.None();
            }

            if (!pendingRequest.ReloadObserved)
            {
                return UnityCliLoopStoredCompileResult.None();
            }

            JObject recoveredResult = CreateRecoveredCompileResult(pendingRequest);
            // Why from when the request was marked: this result is created after the reload the request observed,
            // so its own time would exclude that reload, and the first reload since the request is that one.
            long markedAtUtcTicks = pendingRequest.ExpiresAtUtcTicks -
                UnityCliLoopCompileSessionLifecycleService.CompileResultLifetime.Ticks;
            if (reloadDurationMonitor.AttributeFirstReloadSince(recoveredResult, markedAtUtcTicks) ==
                ReloadAttributionOutcome.NoReloadYet)
            {
                reloadDurationMonitor.AttributeNoReloadTo(recoveredResult);
            }

            compileResultSessionRepository.StoreCompileResult(
                requestId,
                pendingRequest.ForceRecompile,
                recoveredResult.ToString(Formatting.None),
                DateTime.UtcNow,
                isFollowedByReload: true);
            pendingCompileSessionRepository.ClearPendingCompileRequestIfMatches(requestId);
            return compileResultSessionRepository.GetCompileResult(requestId);
        }

        private static JObject CreateRecoveredCompileResult(
            UnityCliLoopPendingCompileRequest pendingRequest)
        {
            Debug.Assert(pendingRequest != null, "pendingRequest must not be null");

            string message = RecoveredCompileResultMessage;
            if (pendingRequest.ForceRecompile)
            {
                ForceCompileUnknownResult unknownForceCompileResult =
                    ForceCompileUnknownResult.Create();
                message = unknownForceCompileResult.Message;
            }

            return new JObject
            {
                ["Success"] = false,
                ["ErrorCount"] = JValue.CreateNull(),
                ["WarningCount"] = JValue.CreateNull(),
                ["Errors"] = JValue.CreateNull(),
                ["Warnings"] = JValue.CreateNull(),
                ["Message"] = message,
                ["ErrorCode"] = ForceCompileUnknownResult.ErrorCodeText,
                ["NextActions"] = new JArray(ForceCompileUnknownResult.NextActionText),
                ["ProjectRoot"] = UnityCliLoopPathResolver.GetProjectRoot()
            };
        }

        private static string CreateMessage(bool ready, bool hasResult)
        {
            if (!ready)
            {
                return "Unity is still compiling, updating assets, or reloading scripts.";
            }

            if (!hasResult)
            {
                return "Compile result is not available for this request. Unity may have restarted or the request may not have completed.";
            }

            return "Compile result is available.";
        }
    }
}
