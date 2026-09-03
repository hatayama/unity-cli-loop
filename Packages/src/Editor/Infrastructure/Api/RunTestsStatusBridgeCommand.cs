using System;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Reports run-tests completion state for the CLI without entering the normal tool execution slot.
    /// </summary>
    internal static class RunTestsStatusBridgeCommand
    {
        private const string RequestIdParamName = "RequestId";

        public static GetRunTestsStatusResponse Execute(JToken paramsToken)
        {
            string requestId = ReadRequestId(paramsToken);
            IRunTestsSessionRepository repository = UnityCliLoopRunTestsSessionRepositoryFacade.Repository;
            repository.ClearExpired(DateTime.UtcNow);
            ISessionFlagsRepository sessionFlagsRepository = UnityCliLoopSessionFlagsFacade.Repository;
            bool isCompiling = EditorApplication.isCompiling;
            bool isUpdating = EditorApplication.isUpdating;
            bool isDomainReloadInProgress =
                sessionFlagsRepository.GetIsDomainReloadInProgress() ||
                DomainReloadStateRegistry.IsDomainReloadInProgress();
            return BuildResponse(
                requestId,
                isCompiling,
                isUpdating,
                isDomainReloadInProgress,
                repository);
        }

        internal static GetRunTestsStatusResponse BuildResponse(
            string requestId,
            bool isCompiling,
            bool isUpdating,
            bool isDomainReloadInProgress,
            IRunTestsSessionRepository repository)
        {
            Debug.Assert(repository != null, "repository must not be null");

            bool ready = !isCompiling && !isUpdating && !isDomainReloadInProgress;
            UnityCliLoopStoredRunTestsResult storedResult = string.IsNullOrWhiteSpace(requestId)
                ? UnityCliLoopStoredRunTestsResult.None()
                : repository.GetRunResult(requestId);
            JToken result = storedResult.HasResult ? JToken.Parse(storedResult.ResultJson) : null;
            return new GetRunTestsStatusResponse
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

        private static string CreateMessage(bool ready, bool hasResult)
        {
            if (!ready)
            {
                return "Unity is still compiling, updating assets, or reloading scripts.";
            }

            if (!hasResult)
            {
                return "Run-tests result is not available for this request. Unity may have restarted or the request may not have completed.";
            }

            return "Run-tests result is available.";
        }
    }
}
