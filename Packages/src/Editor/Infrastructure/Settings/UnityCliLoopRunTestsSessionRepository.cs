using System;
using System.Collections.Generic;
using System.Diagnostics;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Stores pending run-tests recovery records and results in Unity SessionState.
    /// </summary>
    public sealed class UnityCliLoopRunTestsSessionRepository : IRunTestsSessionRepository
    {
        internal static readonly TimeSpan RunTestsResultLifetime = TimeSpan.FromMinutes(20);

        private const string PendingRequestIdsKey =
            UnityCliLoopEditorSessionStateStorage.KeyPrefix + "runTestsPendingRequestIds";
        private const string ResultRequestIdsKey =
            UnityCliLoopEditorSessionStateStorage.KeyPrefix + "runTestsResultRequestIds";
        private const string RunTestsKeyPrefix =
            UnityCliLoopEditorSessionStateStorage.KeyPrefix + "runTests.";
        private const string PendingExpiresAtUtcTicksKeySuffix = ".expiresAtUtcTicks";
        private const string ResultJsonKeySuffix = ".json";
        private const string ResultCompletedAtUtcTicksKeySuffix = ".completedAtUtcTicks";

        public void StorePendingRun(string requestId, DateTime expiresAtUtc)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(requestId), "requestId must not be null or whitespace");
            Debug.Assert(expiresAtUtc.Kind == DateTimeKind.Utc, "expiresAtUtc must be UTC");
            Debug.Assert(expiresAtUtc.Ticks > 0, "expiresAtUtc ticks must be positive");

            // Why: a reused request id must not surface the previous run's result to the new poll.
            ClearRunResult(requestId);
            SetPendingRequestIds(
                UnityCliLoopEditorSessionStateStorage.AddRequestIdToIndex(GetPendingRequestIds(), requestId));
            SetPendingExpiresAtUtcTicks(requestId, expiresAtUtc.Ticks.ToString());
        }

        public bool HasPendingRun(string requestId)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(requestId), "requestId must not be null or whitespace");

            IReadOnlyList<string> pendingIds = GetPendingRunRequestIds();
            for (int i = 0; i < pendingIds.Count; i++)
            {
                if (pendingIds[i] == requestId)
                {
                    return true;
                }
            }

            return false;
        }

        public IReadOnlyList<string> GetPendingRunRequestIds()
        {
            string[] requestIds =
                UnityCliLoopEditorSessionStateStorage.ParseRequestIdIndex(GetPendingRequestIds());
            List<string> pendingIds = new List<string>();
            foreach (string requestId in requestIds)
            {
                if (!HasValidPendingExpiresAt(requestId))
                {
                    ClearPendingRunValues(requestId);
                    continue;
                }

                pendingIds.Add(requestId);
            }

            if (pendingIds.Count != requestIds.Length)
            {
                string rebuiltIndex = "";
                foreach (string pendingId in pendingIds)
                {
                    rebuiltIndex = UnityCliLoopEditorSessionStateStorage.AddRequestIdToIndex(rebuiltIndex, pendingId);
                }

                SetPendingRequestIds(rebuiltIndex);
            }

            return pendingIds;
        }

        public bool HasAnyPendingRun()
        {
            return GetPendingRunRequestIds().Count > 0;
        }

        public void ClearPendingRun(string requestId)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(requestId), "requestId must not be null or whitespace");

            ClearPendingRunValues(requestId);
            SetPendingRequestIds(
                UnityCliLoopEditorSessionStateStorage.RemoveRequestIdFromIndex(GetPendingRequestIds(), requestId));
        }

        public void StoreRunResult(string requestId, string resultJson, DateTime completedAtUtc)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(requestId), "requestId must not be null or whitespace");
            Debug.Assert(!string.IsNullOrWhiteSpace(resultJson), "resultJson must not be null or whitespace");
            Debug.Assert(completedAtUtc.Kind == DateTimeKind.Utc, "completedAtUtc must be UTC");

            SetResultRequestIds(
                UnityCliLoopEditorSessionStateStorage.AddRequestIdToIndex(GetResultRequestIds(), requestId));
            SetResultJson(requestId, resultJson);
            SetResultCompletedAtUtcTicks(requestId, completedAtUtc.Ticks.ToString());
            ClearPendingRun(requestId);
        }

        public UnityCliLoopStoredRunTestsResult GetRunResult(string requestId)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(requestId), "requestId must not be null or whitespace");

            string resultJson = GetResultJson(requestId);
            if (string.IsNullOrWhiteSpace(resultJson))
            {
                ClearRunResult(requestId);
                return UnityCliLoopStoredRunTestsResult.None();
            }

            string completedAtUtcTicksText = GetResultCompletedAtUtcTicks(requestId);
            (bool isValid, long completedAtUtcTicks) =
                UnityCliLoopEditorSessionStateStorage.ParseUtcTicks(completedAtUtcTicksText);
            if (!isValid || completedAtUtcTicks <= 0)
            {
                ClearRunResult(requestId);
                return UnityCliLoopStoredRunTestsResult.None();
            }

            return UnityCliLoopStoredRunTestsResult.Create(requestId, resultJson, completedAtUtcTicks);
        }

        public void ClearExpired(DateTime utcNow)
        {
            Debug.Assert(utcNow.Kind == DateTimeKind.Utc, "utcNow must be UTC");

            IReadOnlyList<string> pendingIds = GetPendingRunRequestIds();
            foreach (string requestId in pendingIds)
            {
                if (!IsPendingExpired(requestId, utcNow))
                {
                    continue;
                }

                ClearPendingRun(requestId);
            }

            string[] resultIds =
                UnityCliLoopEditorSessionStateStorage.ParseRequestIdIndex(GetResultRequestIds());
            foreach (string requestId in resultIds)
            {
                UnityCliLoopStoredRunTestsResult storedResult = GetRunResult(requestId);
                if (!storedResult.IsExpiredAt(utcNow, RunTestsResultLifetime))
                {
                    continue;
                }

                ClearRunResult(requestId);
            }
        }

        internal void ClearAll()
        {
            foreach (string requestId in UnityCliLoopEditorSessionStateStorage.ParseRequestIdIndex(
                GetPendingRequestIds()))
            {
                ClearPendingRunValues(requestId);
            }

            SetPendingRequestIds("");

            foreach (string requestId in UnityCliLoopEditorSessionStateStorage.ParseRequestIdIndex(
                GetResultRequestIds()))
            {
                ClearRunResultValues(requestId);
            }

            SetResultRequestIds("");
        }

        private static bool HasValidPendingExpiresAt(string requestId)
        {
            (bool isValid, long expiresAtUtcTicks) =
                UnityCliLoopEditorSessionStateStorage.ParseUtcTicks(GetPendingExpiresAtUtcTicks(requestId));
            return isValid && expiresAtUtcTicks > 0;
        }

        private static bool IsPendingExpired(string requestId, DateTime utcNow)
        {
            (bool isValid, long expiresAtUtcTicks) =
                UnityCliLoopEditorSessionStateStorage.ParseUtcTicks(GetPendingExpiresAtUtcTicks(requestId));
            return isValid && expiresAtUtcTicks > 0 && expiresAtUtcTicks <= utcNow.Ticks;
        }

        private void ClearRunResult(string requestId)
        {
            ClearRunResultValues(requestId);
            SetResultRequestIds(
                UnityCliLoopEditorSessionStateStorage.RemoveRequestIdFromIndex(GetResultRequestIds(), requestId));
        }

        private static void ClearPendingRunValues(string requestId)
        {
            SetPendingExpiresAtUtcTicks(requestId, "");
        }

        private static void ClearRunResultValues(string requestId)
        {
            SetResultJson(requestId, "");
            SetResultCompletedAtUtcTicks(requestId, "");
        }

        private static string GetPendingRequestIds()
        {
            return UnityCliLoopEditorSessionStateStorage.GetString(PendingRequestIdsKey);
        }

        private static void SetPendingRequestIds(string pendingRequestIds)
        {
            UnityCliLoopEditorSessionStateStorage.SetString(PendingRequestIdsKey, pendingRequestIds);
        }

        private static string GetResultRequestIds()
        {
            return UnityCliLoopEditorSessionStateStorage.GetString(ResultRequestIdsKey);
        }

        private static void SetResultRequestIds(string resultRequestIds)
        {
            UnityCliLoopEditorSessionStateStorage.SetString(ResultRequestIdsKey, resultRequestIds);
        }

        private static string GetPendingExpiresAtUtcTicks(string requestId)
        {
            return UnityCliLoopEditorSessionStateStorage.GetString(
                CreateRunTestsKey(requestId, PendingExpiresAtUtcTicksKeySuffix));
        }

        private static void SetPendingExpiresAtUtcTicks(string requestId, string expiresAtUtcTicks)
        {
            UnityCliLoopEditorSessionStateStorage.SetString(
                CreateRunTestsKey(requestId, PendingExpiresAtUtcTicksKeySuffix),
                expiresAtUtcTicks);
        }

        private static string GetResultJson(string requestId)
        {
            return UnityCliLoopEditorSessionStateStorage.GetString(
                CreateRunTestsKey(requestId, ResultJsonKeySuffix));
        }

        private static void SetResultJson(string requestId, string resultJson)
        {
            UnityCliLoopEditorSessionStateStorage.SetString(
                CreateRunTestsKey(requestId, ResultJsonKeySuffix),
                resultJson);
        }

        private static string GetResultCompletedAtUtcTicks(string requestId)
        {
            return UnityCliLoopEditorSessionStateStorage.GetString(
                CreateRunTestsKey(requestId, ResultCompletedAtUtcTicksKeySuffix));
        }

        private static void SetResultCompletedAtUtcTicks(string requestId, string completedAtUtcTicks)
        {
            UnityCliLoopEditorSessionStateStorage.SetString(
                CreateRunTestsKey(requestId, ResultCompletedAtUtcTicksKeySuffix),
                completedAtUtcTicks);
        }

        private static string CreateRunTestsKey(string requestId, string suffix)
        {
            return UnityCliLoopEditorSessionStateStorage.CreateRequestScopedKey(
                RunTestsKeyPrefix,
                requestId,
                suffix);
        }
    }
}
