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
            "Compilation completed, but Unity reloaded scripts before Unity CLI Loop could record detailed errors or warnings. Use get-logs to inspect the compiler output.";

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
            GetCompileStatusResponse response = BuildResponse(
                requestId,
                isCompiling,
                isUpdating,
                isDomainReloadInProgress,
                compileSessionLifecycleService,
                compileResultSessionRepository,
                pendingCompileSessionRepository);
            LogCompileStatusQueryReceived(requestId, response);
            return response;
        }

        internal static GetCompileStatusResponse BuildResponse(
            string requestId,
            bool isCompiling,
            bool isUpdating,
            bool isDomainReloadInProgress,
            UnityCliLoopCompileSessionLifecycleService compileSessionLifecycleService,
            ICompileResultSessionRepository compileResultSessionRepository,
            IPendingCompileSessionRepository pendingCompileSessionRepository)
        {
            Debug.Assert(compileSessionLifecycleService != null, "compileSessionLifecycleService must not be null");
            Debug.Assert(compileResultSessionRepository != null, "compileResultSessionRepository must not be null");
            Debug.Assert(pendingCompileSessionRepository != null, "pendingCompileSessionRepository must not be null");

            bool ready = !isCompiling && !isUpdating && !isDomainReloadInProgress;
            compileSessionLifecycleService.ClearExpiredPendingCompileRequest(DateTime.UtcNow);
            UnityCliLoopStoredCompileResult storedResult = string.IsNullOrWhiteSpace(requestId)
                ? UnityCliLoopStoredCompileResult.None()
                : compileResultSessionRepository.GetCompileResult(requestId);
            if (ready && !storedResult.HasResult)
            {
                storedResult = RecoverPendingCompileResult(
                    requestId,
                    compileResultSessionRepository,
                    pendingCompileSessionRepository);
            }

            JToken result = storedResult.HasResult ? JToken.Parse(storedResult.ResultJson) : null;
            return new GetCompileStatusResponse
            {
                Ready = ready,
                HasResult = storedResult.HasResult,
                IsCompiling = isCompiling,
                IsUpdating = isUpdating,
                IsDomainReloadInProgress = isDomainReloadInProgress,
                Result = result,
                Message = CreateMessage(ready, storedResult.HasResult)
            };
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
            GetCompileStatusResponse response)
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
                    message = response.Message
                },
                requestId);
        }

        private static UnityCliLoopStoredCompileResult RecoverPendingCompileResult(
            string requestId,
            ICompileResultSessionRepository compileResultSessionRepository,
            IPendingCompileSessionRepository pendingCompileSessionRepository)
        {
            Debug.Assert(compileResultSessionRepository != null, "compileResultSessionRepository must not be null");
            Debug.Assert(pendingCompileSessionRepository != null, "pendingCompileSessionRepository must not be null");

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
            compileResultSessionRepository.StoreCompileResult(
                requestId,
                pendingRequest.ForceRecompile,
                recoveredResult.ToString(Formatting.None),
                DateTime.UtcNow);
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
