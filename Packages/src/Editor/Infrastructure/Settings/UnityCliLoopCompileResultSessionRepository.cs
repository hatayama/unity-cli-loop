using System;
using System.Collections.Generic;
using System.Diagnostics;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Stores compile-result session records in Unity SessionState.
    /// </summary>
    public sealed class UnityCliLoopCompileResultSessionRepository : ICompileResultSessionRepository
    {
        private const string CompileResultRequestIdsKey =
            UnityCliLoopEditorSessionStateStorage.KeyPrefix + "compileResultRequestIds";
        private const string LegacyCompileResultRequestIdKey =
            UnityCliLoopEditorSessionStateStorage.KeyPrefix + "compileResultRequestId";
        private const string LegacyCompileResultForceRecompileKey =
            UnityCliLoopEditorSessionStateStorage.KeyPrefix + "compileResultForceRecompile";
        private const string LegacyCompileResultJsonKey =
            UnityCliLoopEditorSessionStateStorage.KeyPrefix + "compileResultJson";
        private const string LegacyCompileResultCompletedAtUtcTicksKey =
            UnityCliLoopEditorSessionStateStorage.KeyPrefix + "compileResultCompletedAtUtcTicks";
        private const string CompileResultKeyPrefix =
            UnityCliLoopEditorSessionStateStorage.KeyPrefix + "compileResult.";
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

            SetCompileResultRequestIds(
                UnityCliLoopEditorSessionStateStorage.AddRequestIdToIndex(GetCompileResultRequestIds(), requestId));
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
                UnityCliLoopEditorSessionStateStorage.ParseUtcTicks(completedAtUtcTicksText);
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
            string[] requestIds =
                UnityCliLoopEditorSessionStateStorage.ParseRequestIdIndex(GetCompileResultRequestIds());
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
            foreach (string requestId in UnityCliLoopEditorSessionStateStorage.ParseRequestIdIndex(
                GetCompileResultRequestIds()))
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

        // Tests and legacy migration use these helpers without widening the aggregate port.
        internal static void SetLegacyCompileResultRequestId(string compileResultRequestId)
        {
            UnityCliLoopEditorSessionStateStorage.SetString(
                LegacyCompileResultRequestIdKey,
                compileResultRequestId);
        }

        internal static string GetLegacyCompileResultRequestId()
        {
            return UnityCliLoopEditorSessionStateStorage.GetString(LegacyCompileResultRequestIdKey);
        }

        internal static void SetLegacyCompileResultForceRecompile(bool compileResultForceRecompile)
        {
            UnityCliLoopEditorSessionStateStorage.SetBool(
                LegacyCompileResultForceRecompileKey,
                compileResultForceRecompile);
        }

        internal static void SetLegacyCompileResultJson(string compileResultJson)
        {
            UnityCliLoopEditorSessionStateStorage.SetString(LegacyCompileResultJsonKey, compileResultJson);
        }

        internal static void SetLegacyCompileResultCompletedAtUtcTicks(string compileResultCompletedAtUtcTicks)
        {
            UnityCliLoopEditorSessionStateStorage.SetString(
                LegacyCompileResultCompletedAtUtcTicksKey,
                compileResultCompletedAtUtcTicks);
        }

        internal static void SetCompileResultRequestIds(string compileResultRequestIds)
        {
            UnityCliLoopEditorSessionStateStorage.SetString(CompileResultRequestIdsKey, compileResultRequestIds);
        }

        internal static void SetCompileResultForceRecompile(string requestId, bool compileResultForceRecompile)
        {
            UnityCliLoopEditorSessionStateStorage.SetBool(
                CreateCompileResultKey(requestId, CompileResultForceRecompileKeySuffix),
                compileResultForceRecompile);
        }

        internal static void SetCompileResultJson(string requestId, string compileResultJson)
        {
            UnityCliLoopEditorSessionStateStorage.SetString(
                CreateCompileResultKey(requestId, CompileResultJsonKeySuffix),
                compileResultJson);
        }

        internal static void SetCompileResultCompletedAtUtcTicks(string requestId, string compileResultCompletedAtUtcTicks)
        {
            UnityCliLoopEditorSessionStateStorage.SetString(
                CreateCompileResultKey(requestId, CompileResultCompletedAtUtcTicksKeySuffix),
                compileResultCompletedAtUtcTicks);
        }

        private static string GetCompileResultRequestIds()
        {
            return UnityCliLoopEditorSessionStateStorage.GetString(CompileResultRequestIdsKey);
        }

        private static bool GetLegacyCompileResultForceRecompile()
        {
            return UnityCliLoopEditorSessionStateStorage.GetBool(LegacyCompileResultForceRecompileKey);
        }

        private static string GetLegacyCompileResultJson()
        {
            return UnityCliLoopEditorSessionStateStorage.GetString(LegacyCompileResultJsonKey);
        }

        private static string GetLegacyCompileResultCompletedAtUtcTicks()
        {
            return UnityCliLoopEditorSessionStateStorage.GetString(LegacyCompileResultCompletedAtUtcTicksKey);
        }

        private static bool GetCompileResultForceRecompile(string requestId)
        {
            return UnityCliLoopEditorSessionStateStorage.GetBool(
                CreateCompileResultKey(requestId, CompileResultForceRecompileKeySuffix));
        }

        private static string GetCompileResultJson(string requestId)
        {
            return UnityCliLoopEditorSessionStateStorage.GetString(
                CreateCompileResultKey(requestId, CompileResultJsonKeySuffix));
        }

        private static string GetCompileResultCompletedAtUtcTicks(string requestId)
        {
            return UnityCliLoopEditorSessionStateStorage.GetString(
                CreateCompileResultKey(requestId, CompileResultCompletedAtUtcTicksKeySuffix));
        }

        private static void ClearCompileResultForRequestId(string requestId)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(requestId), "requestId must not be null or whitespace");

            ClearCompileResultValues(requestId);
            SetCompileResultRequestIds(
                UnityCliLoopEditorSessionStateStorage.RemoveRequestIdFromIndex(
                    GetCompileResultRequestIds(),
                    requestId));
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

        private UnityCliLoopStoredCompileResult GetLegacyCompileResult()
        {
            string requestId = GetLegacyCompileResultRequestId();
            if (string.IsNullOrWhiteSpace(requestId))
            {
                return UnityCliLoopStoredCompileResult.None();
            }

            return GetLegacyCompileResultForRequestId(requestId);
        }

        private UnityCliLoopStoredCompileResult GetLegacyCompileResultForRequestId(string requestId)
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
                UnityCliLoopEditorSessionStateStorage.ParseUtcTicks(completedAtUtcTicksText);
            if (!isValid || completedAtUtcTicks <= 0)
            {
                ClearLegacyCompileResult();
                return UnityCliLoopStoredCompileResult.None();
            }

            bool forceRecompile = GetLegacyCompileResultForceRecompile();
            StoreCompileResult(
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

        private static void ClearLegacyCompileResult()
        {
            SetLegacyCompileResultRequestId("");
            SetLegacyCompileResultForceRecompile(false);
            SetLegacyCompileResultJson("");
            SetLegacyCompileResultCompletedAtUtcTicks("");
        }

        private static string CreateCompileResultKey(string requestId, string suffix)
        {
            return UnityCliLoopEditorSessionStateStorage.CreateRequestScopedKey(
                CompileResultKeyPrefix,
                requestId,
                suffix);
        }
    }
}
