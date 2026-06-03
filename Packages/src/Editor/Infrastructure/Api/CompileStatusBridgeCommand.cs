using System;
using System.Collections.Generic;
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
        private static readonly Dictionary<string, string> LastLoggedStatusByRequestId =
            new Dictionary<string, string>();

        public static GetCompileStatusResponse Execute(JToken paramsToken)
        {
            string requestId = ReadRequestId(paramsToken);
            UnityCliLoopEditorSessionStateService sessionStateService =
                new UnityCliLoopEditorSessionStateService(new UnityCliLoopEditorSessionStateRepository());
            sessionStateService.ClearExpiredCompileResult(DateTime.UtcNow);
            bool isCompiling = EditorApplication.isCompiling;
            bool isUpdating = EditorApplication.isUpdating;
            bool isDomainReloadInProgress =
                sessionStateService.GetIsDomainReloadInProgress() ||
                DomainReloadStateRegistry.IsDomainReloadInProgress();
            return BuildResponse(
                requestId,
                isCompiling,
                isUpdating,
                isDomainReloadInProgress,
                sessionStateService);
        }

        internal static GetCompileStatusResponse BuildResponse(
            string requestId,
            bool isCompiling,
            bool isUpdating,
            bool isDomainReloadInProgress,
            UnityCliLoopEditorSessionStateService sessionStateService)
        {
            Debug.Assert(sessionStateService != null, "sessionStateService must not be null");

            bool ready = !isCompiling && !isUpdating && !isDomainReloadInProgress;
            sessionStateService.ClearExpiredPendingCompileRequest(DateTime.UtcNow);
            UnityCliLoopStoredCompileResult storedResult = string.IsNullOrWhiteSpace(requestId)
                ? UnityCliLoopStoredCompileResult.None()
                : sessionStateService.GetCompileResult(requestId);
            if (ready && !storedResult.HasResult)
            {
                storedResult = RecoverPendingCompileResult(requestId, sessionStateService);
            }

            JToken result = storedResult.HasResult ? JToken.Parse(storedResult.ResultJson) : null;
            GetCompileStatusResponse response = new GetCompileStatusResponse
            {
                Ready = ready,
                HasResult = storedResult.HasResult,
                IsCompiling = isCompiling,
                IsUpdating = isUpdating,
                IsDomainReloadInProgress = isDomainReloadInProgress,
                Result = result,
                Message = CreateMessage(ready, storedResult.HasResult)
            };
            LogCompileStatusQueryReceived(requestId, response);
            return response;
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

        private static UnityCliLoopStoredCompileResult RecoverPendingCompileResult(
            string requestId,
            UnityCliLoopEditorSessionStateService sessionStateService)
        {
            Debug.Assert(sessionStateService != null, "sessionStateService must not be null");

            if (string.IsNullOrWhiteSpace(requestId))
            {
                return UnityCliLoopStoredCompileResult.None();
            }

            UnityCliLoopPendingCompileRequest pendingRequest =
                sessionStateService.GetPendingCompileRequestForRequestId(requestId);
            if (!pendingRequest.HasRequest)
            {
                return UnityCliLoopStoredCompileResult.None();
            }

            if (!pendingRequest.ReloadObserved)
            {
                return UnityCliLoopStoredCompileResult.None();
            }

            JObject recoveredResult = CreateRecoveredCompileResult(pendingRequest);
            sessionStateService.StoreCompileResult(
                requestId,
                pendingRequest.ForceRecompile,
                recoveredResult.ToString(Formatting.None),
                DateTime.UtcNow);
            sessionStateService.ClearPendingCompileRequestIfMatches(requestId);
            return sessionStateService.GetCompileResult(requestId);
        }

        private static JObject CreateRecoveredCompileResult(
            UnityCliLoopPendingCompileRequest pendingRequest)
        {
            Debug.Assert(pendingRequest != null, "pendingRequest must not be null");

            return new JObject
            {
                ["Success"] = JValue.CreateNull(),
                ["ErrorCount"] = JValue.CreateNull(),
                ["WarningCount"] = JValue.CreateNull(),
                ["Errors"] = JValue.CreateNull(),
                ["Warnings"] = JValue.CreateNull(),
                ["Message"] = CreateRecoveredCompileMessage(pendingRequest.ForceRecompile),
                ["ProjectRoot"] = UnityCliLoopPathResolver.GetProjectRoot()
            };
        }

        private static JToken CreateRecoveredCompileMessage(bool forceRecompile)
        {
            if (forceRecompile)
            {
                return JValue.CreateNull();
            }

            return new JValue(RecoveredCompileResultMessage);
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

        private static void LogCompileStatusQueryReceived(
            string requestId,
            GetCompileStatusResponse response)
        {
            Debug.Assert(response != null, "response must not be null");

            string signature = CreateStatusSignature(response);
            if (!string.IsNullOrWhiteSpace(requestId) &&
                LastLoggedStatusByRequestId.TryGetValue(requestId, out string previousSignature) &&
                previousSignature == signature)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(requestId))
            {
                LastLoggedStatusByRequestId[requestId] = signature;
            }

            VibeLogger.LogInfo(
                "compile_status_query_received",
                "CLI queried Unity compile status.",
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

        private static string CreateStatusSignature(GetCompileStatusResponse response)
        {
            Debug.Assert(response != null, "response must not be null");

            return string.Join(
                "|",
                response.Ready,
                response.HasResult,
                response.IsCompiling,
                response.IsUpdating,
                response.IsDomainReloadInProgress,
                response.Message ?? "");
        }
    }
}
