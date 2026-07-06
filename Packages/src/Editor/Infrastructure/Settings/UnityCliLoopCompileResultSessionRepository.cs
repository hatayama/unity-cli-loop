using System;
using System.Collections.Generic;
using System.Diagnostics;

using UnityEditor;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Stores compile-result session records in Unity SessionState.
    /// </summary>
    public sealed class UnityCliLoopCompileResultSessionRepository : ICompileResultSessionRepository
    {
        private const string KeyPrefix = "io.github.hatayama.uloopmcp.editorSession.";
        private const string CompileResultRequestIdsKey = KeyPrefix + "compileResultRequestIds";
        private const string LegacyCompileResultRequestIdKey = KeyPrefix + "compileResultRequestId";
        private const string LegacyCompileResultForceRecompileKey = KeyPrefix + "compileResultForceRecompile";
        private const string LegacyCompileResultJsonKey = KeyPrefix + "compileResultJson";
        private const string LegacyCompileResultCompletedAtUtcTicksKey =
            KeyPrefix + "compileResultCompletedAtUtcTicks";
        private const string CompileResultKeyPrefix = KeyPrefix + "compileResult.";
        private const string CompileResultForceRecompileKeySuffix = ".forceRecompile";
        private const string CompileResultJsonKeySuffix = ".json";
        private const string CompileResultCompletedAtUtcTicksKeySuffix = ".completedAtUtcTicks";

        public void StoreCompileResult(
            string requestId,
            bool forceRecompile,
            string resultJson,
            DateTime completedAtUtc)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(requestId), "requestId must not be null or whitespace");
            Debug.Assert(!string.IsNullOrWhiteSpace(resultJson), "resultJson must not be null or whitespace");
            Debug.Assert(completedAtUtc.Kind == DateTimeKind.Utc, "completedAtUtc must be UTC");

            SetCompileResultRequestIds(AddRequestIdToIndex(GetCompileResultRequestIds(), requestId));
            SetCompileResultForceRecompile(requestId, forceRecompile);
            SetCompileResultJson(requestId, resultJson);
            SetCompileResultCompletedAtUtcTicks(requestId, completedAtUtc.Ticks.ToString());
        }

        public UnityCliLoopStoredCompileResult GetCompileResult(string requestId)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(requestId), "requestId must not be null or whitespace");

            string resultJson = GetCompileResultJson(requestId);
            if (string.IsNullOrWhiteSpace(resultJson))
            {
                UnityCliLoopStoredCompileResult legacyResult =
                    GetLegacyCompileResultForRequestId(requestId);
                if (legacyResult.HasResult)
                {
                    return legacyResult;
                }

                ClearCompileResultForRequestId(requestId);
                return UnityCliLoopStoredCompileResult.None();
            }

            string completedAtUtcTicksText = GetCompileResultCompletedAtUtcTicks(requestId);
            (bool isValid, long completedAtUtcTicks) =
                ParseUtcTicks(completedAtUtcTicksText);
            if (!isValid || completedAtUtcTicks <= 0)
            {
                ClearCompileResultForRequestId(requestId);
                return UnityCliLoopStoredCompileResult.None();
            }

            return UnityCliLoopStoredCompileResult.Create(
                requestId,
                GetCompileResultForceRecompile(requestId),
                resultJson,
                completedAtUtcTicks);
        }

        public UnityCliLoopStoredCompileResult GetStoredCompileResult()
        {
            UnityCliLoopStoredCompileResult[] storedResults = GetStoredCompileResults();
            if (storedResults.Length == 0)
            {
                return UnityCliLoopStoredCompileResult.None();
            }

            return storedResults[0];
        }

        public UnityCliLoopStoredCompileResult[] GetStoredCompileResults()
        {
            string[] requestIds = ParseRequestIdIndex(GetCompileResultRequestIds());
            List<UnityCliLoopStoredCompileResult> storedResults =
                new List<UnityCliLoopStoredCompileResult>();
            foreach (string requestId in requestIds)
            {
                UnityCliLoopStoredCompileResult storedResult = GetCompileResult(requestId);
                if (storedResult.HasResult)
                {
                    storedResults.Add(storedResult);
                }
            }

            UnityCliLoopStoredCompileResult legacyResult = GetLegacyCompileResult();
            if (legacyResult.HasResult && !ContainsCompileResult(storedResults, legacyResult.RequestId))
            {
                storedResults.Add(legacyResult);
            }

            return storedResults.ToArray();
        }

        public void ClearCompileResult()
        {
            foreach (string requestId in ParseRequestIdIndex(GetCompileResultRequestIds()))
            {
                ClearCompileResultValues(requestId);
            }

            SetCompileResultRequestIds("");
            ClearLegacyCompileResult();
        }

        public bool ClearExpiredCompileResult(DateTime utcNow, TimeSpan lifetime)
        {
            Debug.Assert(utcNow.Kind == DateTimeKind.Utc, "utcNow must be UTC");
            Debug.Assert(lifetime > TimeSpan.Zero, "lifetime must be positive");

            bool cleared = false;
            UnityCliLoopStoredCompileResult[] storedResults = GetStoredCompileResults();
            foreach (UnityCliLoopStoredCompileResult storedResult in storedResults)
            {
                if (!storedResult.IsExpiredAt(utcNow, lifetime))
                {
                    continue;
                }

                ClearCompileResultForRequestId(storedResult.RequestId);
                cleared = true;
            }

            return cleared;
        }

        // Tests seed pre-refactor and corrupt values through these helpers without widening the aggregate port.
        internal static void SetLegacyCompileResultRequestId(string compileResultRequestId)
        {
            SetString(LegacyCompileResultRequestIdKey, compileResultRequestId);
        }

        internal static string GetLegacyCompileResultRequestId()
        {
            return GetString(LegacyCompileResultRequestIdKey);
        }

        internal static void SetLegacyCompileResultForceRecompile(bool compileResultForceRecompile)
        {
            SetBool(LegacyCompileResultForceRecompileKey, compileResultForceRecompile);
        }

        internal static void SetLegacyCompileResultJson(string compileResultJson)
        {
            SetString(LegacyCompileResultJsonKey, compileResultJson);
        }

        internal static void SetLegacyCompileResultCompletedAtUtcTicks(string compileResultCompletedAtUtcTicks)
        {
            SetString(LegacyCompileResultCompletedAtUtcTicksKey, compileResultCompletedAtUtcTicks);
        }

        internal static void SetCompileResultRequestIds(string compileResultRequestIds)
        {
            SetString(CompileResultRequestIdsKey, compileResultRequestIds);
        }

        internal static void SetCompileResultForceRecompile(string requestId, bool compileResultForceRecompile)
        {
            SetBool(CreateCompileResultKey(requestId, CompileResultForceRecompileKeySuffix), compileResultForceRecompile);
        }

        internal static void SetCompileResultJson(string requestId, string compileResultJson)
        {
            SetString(CreateCompileResultKey(requestId, CompileResultJsonKeySuffix), compileResultJson);
        }

        internal static void SetCompileResultCompletedAtUtcTicks(string requestId, string compileResultCompletedAtUtcTicks)
        {
            SetString(CreateCompileResultKey(requestId, CompileResultCompletedAtUtcTicksKeySuffix), compileResultCompletedAtUtcTicks);
        }

        private static string GetCompileResultRequestIds()
        {
            return GetString(CompileResultRequestIdsKey);
        }

        private static bool GetLegacyCompileResultForceRecompile()
        {
            return GetBool(LegacyCompileResultForceRecompileKey);
        }

        private static string GetLegacyCompileResultJson()
        {
            return GetString(LegacyCompileResultJsonKey);
        }

        private static string GetLegacyCompileResultCompletedAtUtcTicks()
        {
            return GetString(LegacyCompileResultCompletedAtUtcTicksKey);
        }

        private static bool GetCompileResultForceRecompile(string requestId)
        {
            return GetBool(CreateCompileResultKey(requestId, CompileResultForceRecompileKeySuffix));
        }

        private static string GetCompileResultJson(string requestId)
        {
            return GetString(CreateCompileResultKey(requestId, CompileResultJsonKeySuffix));
        }

        private static string GetCompileResultCompletedAtUtcTicks(string requestId)
        {
            return GetString(CreateCompileResultKey(requestId, CompileResultCompletedAtUtcTicksKeySuffix));
        }

        private static void ClearCompileResultForRequestId(string requestId)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(requestId), "requestId must not be null or whitespace");

            ClearCompileResultValues(requestId);
            SetCompileResultRequestIds(
                RemoveRequestIdFromIndex(GetCompileResultRequestIds(), requestId));
        }

        private static void ClearCompileResultValues(string requestId)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(requestId), "requestId must not be null or whitespace");

            SetCompileResultForceRecompile(requestId, false);
            SetCompileResultJson(requestId, "");
            SetCompileResultCompletedAtUtcTicks(requestId, "");
        }

        private static bool ContainsCompileResult(
            List<UnityCliLoopStoredCompileResult> storedResults,
            string requestId)
        {
            Debug.Assert(storedResults != null, "storedResults must not be null");

            foreach (UnityCliLoopStoredCompileResult storedResult in storedResults)
            {
                if (storedResult.RequestId == requestId)
                {
                    return true;
                }
            }

            return false;
        }

        private static UnityCliLoopStoredCompileResult GetLegacyCompileResult()
        {
            string requestId = GetLegacyCompileResultRequestId();
            if (string.IsNullOrWhiteSpace(requestId))
            {
                return UnityCliLoopStoredCompileResult.None();
            }

            return GetLegacyCompileResultForRequestId(requestId);
        }

        private static UnityCliLoopStoredCompileResult GetLegacyCompileResultForRequestId(string requestId)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(requestId), "requestId must not be null or whitespace");

            string legacyRequestId = GetLegacyCompileResultRequestId();
            if (legacyRequestId != requestId)
            {
                return UnityCliLoopStoredCompileResult.None();
            }

            string resultJson = GetLegacyCompileResultJson();
            if (string.IsNullOrWhiteSpace(resultJson))
            {
                ClearLegacyCompileResult();
                return UnityCliLoopStoredCompileResult.None();
            }

            string completedAtUtcTicksText =
                GetLegacyCompileResultCompletedAtUtcTicks();
            (bool isValid, long completedAtUtcTicks) =
                ParseUtcTicks(completedAtUtcTicksText);
            if (!isValid || completedAtUtcTicks <= 0)
            {
                ClearLegacyCompileResult();
                return UnityCliLoopStoredCompileResult.None();
            }

            bool forceRecompile = GetLegacyCompileResultForceRecompile();
            StoreMigratedCompileResult(
                requestId,
                forceRecompile,
                resultJson,
                new DateTime(completedAtUtcTicks, DateTimeKind.Utc));
            ClearLegacyCompileResult();
            return UnityCliLoopStoredCompileResult.Create(
                requestId,
                forceRecompile,
                resultJson,
                completedAtUtcTicks);
        }

        private static void StoreMigratedCompileResult(
            string requestId,
            bool forceRecompile,
            string resultJson,
            DateTime completedAtUtc)
        {
            SetCompileResultRequestIds(AddRequestIdToIndex(GetCompileResultRequestIds(), requestId));
            SetCompileResultForceRecompile(requestId, forceRecompile);
            SetCompileResultJson(requestId, resultJson);
            SetCompileResultCompletedAtUtcTicks(requestId, completedAtUtc.Ticks.ToString());
        }

        private static void ClearLegacyCompileResult()
        {
            SetLegacyCompileResultRequestId("");
            SetBool(LegacyCompileResultForceRecompileKey, false);
            SetString(LegacyCompileResultJsonKey, "");
            SetString(LegacyCompileResultCompletedAtUtcTicksKey, "");
        }

        private static (bool IsValid, long Value) ParseUtcTicks(string utcTicksText)
        {
            if (string.IsNullOrWhiteSpace(utcTicksText))
            {
                return (true, 0);
            }

            string trimmedText = utcTicksText.Trim();
            long value = 0;
            foreach (char character in trimmedText)
            {
                if (character < '0' || character > '9')
                {
                    return (false, 0);
                }

                int digit = character - '0';
                if (value > (long.MaxValue - digit) / 10)
                {
                    return (false, 0);
                }

                value = value * 10 + digit;
            }

            if (value > DateTime.MaxValue.Ticks)
            {
                return (false, 0);
            }

            return (true, value);
        }

        private static string[] ParseRequestIdIndex(string requestIdIndex)
        {
            if (string.IsNullOrWhiteSpace(requestIdIndex))
            {
                return Array.Empty<string>();
            }

            string[] rawRequestIds = requestIdIndex.Split(
                new[] { '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            List<string> requestIds = new List<string>();
            foreach (string rawRequestId in rawRequestIds)
            {
                string requestId = rawRequestId.Trim();
                if (string.IsNullOrWhiteSpace(requestId) || requestIds.Contains(requestId))
                {
                    continue;
                }

                requestIds.Add(requestId);
            }

            return requestIds.ToArray();
        }

        private static string FormatRequestIdIndex(List<string> requestIds)
        {
            Debug.Assert(requestIds != null, "requestIds must not be null");
            return string.Join("\n", requestIds.ToArray());
        }

        private static string AddRequestIdToIndex(string requestIdIndex, string requestId)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(requestId), "requestId must not be null or whitespace");

            List<string> requestIds = new List<string>(ParseRequestIdIndex(requestIdIndex));
            if (!requestIds.Contains(requestId))
            {
                requestIds.Add(requestId);
            }

            return FormatRequestIdIndex(requestIds);
        }

        private static string RemoveRequestIdFromIndex(string requestIdIndex, string requestId)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(requestId), "requestId must not be null or whitespace");

            List<string> requestIds = new List<string>();
            foreach (string indexedRequestId in ParseRequestIdIndex(requestIdIndex))
            {
                if (indexedRequestId == requestId)
                {
                    continue;
                }

                requestIds.Add(indexedRequestId);
            }

            return FormatRequestIdIndex(requestIds);
        }

        private static string CreateCompileResultKey(string requestId, string suffix)
        {
            return CompileResultKeyPrefix + requestId + suffix;
        }

        private static bool GetBool(string key)
        {
            return SessionState.GetBool(key, false);
        }

        private static void SetBool(string key, bool value)
        {
            SessionState.SetBool(key, value);
        }

        private static string GetString(string key)
        {
            return SessionState.GetString(key, "");
        }

        private static void SetString(string key, string value)
        {
            SessionState.SetString(key, value ?? "");
        }
    }
}
