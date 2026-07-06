using System;
using System.Collections.Generic;
using System.Diagnostics;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Stores pending compile recovery records in Unity SessionState.
    /// </summary>
    public sealed class UnityCliLoopPendingCompileSessionRepository : IPendingCompileSessionRepository
    {
        private const string PendingCompileRequestIdsKey =
            UnityCliLoopEditorSessionStateStorage.KeyPrefix + "pendingCompileRequestIds";
        private const string LegacyPendingCompileRequestIdKey =
            UnityCliLoopEditorSessionStateStorage.KeyPrefix + "pendingCompileRequestId";
        private const string LegacyPendingCompileForceRecompileKey =
            UnityCliLoopEditorSessionStateStorage.KeyPrefix + "pendingCompileForceRecompile";
        private const string LegacyPendingCompileExpiresAtUtcTicksKey =
            UnityCliLoopEditorSessionStateStorage.KeyPrefix + "pendingCompileExpiresAtUtcTicks";
        private const string LegacyPendingCompileReloadObservedKey =
            UnityCliLoopEditorSessionStateStorage.KeyPrefix + "pendingCompileReloadObserved";
        private const string PendingCompileKeyPrefix =
            UnityCliLoopEditorSessionStateStorage.KeyPrefix + "pendingCompile.";
        private const string PendingCompileForceRecompileKeySuffix = ".forceRecompile";
        private const string PendingCompileExpiresAtUtcTicksKeySuffix = ".expiresAtUtcTicks";
        private const string PendingCompileReloadObservedKeySuffix = ".reloadObserved";

        public void StorePendingCompileRequest(
            string requestId,
            bool forceRecompile,
            DateTime expiresAtUtc,
            bool reloadObserved)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(requestId), "requestId must not be null or whitespace");
            Debug.Assert(expiresAtUtc.Kind == DateTimeKind.Utc, "expiresAtUtc must be UTC");
            Debug.Assert(expiresAtUtc.Ticks > 0, "expiresAtUtc ticks must be positive");

            SetPendingCompileRequestIds(
                UnityCliLoopEditorSessionStateStorage.AddRequestIdToIndex(
                    GetPendingCompileRequestIds(),
                    requestId));
            SetPendingCompileForceRecompile(requestId, forceRecompile);
            SetPendingCompileExpiresAtUtcTicks(requestId, expiresAtUtc.Ticks.ToString());
            SetPendingCompileReloadObserved(requestId, reloadObserved);
        }

        public UnityCliLoopPendingCompileRequest[] GetPendingCompileRequests()
        {
            string[] requestIds =
                UnityCliLoopEditorSessionStateStorage.ParseRequestIdIndex(GetPendingCompileRequestIds());
            List<UnityCliLoopPendingCompileRequest> pendingRequests =
                new List<UnityCliLoopPendingCompileRequest>();
            foreach (string requestId in requestIds)
            {
                UnityCliLoopPendingCompileRequest pendingRequest =
                    GetPendingCompileRequestForRequestId(requestId);
                if (pendingRequest.HasRequest)
                {
                    pendingRequests.Add(pendingRequest);
                }
            }

            UnityCliLoopPendingCompileRequest legacyRequest = GetLegacyPendingCompileRequest();
            if (legacyRequest.HasRequest && !ContainsPendingCompileRequest(pendingRequests, legacyRequest.RequestId))
            {
                pendingRequests.Add(legacyRequest);
            }

            return pendingRequests.ToArray();
        }

        public bool MarkPendingCompileRequestReloadObserved()
        {
            UnityCliLoopPendingCompileRequest[] pendingRequests = GetPendingCompileRequests();
            if (pendingRequests.Length == 0)
            {
                return false;
            }

            foreach (UnityCliLoopPendingCompileRequest pendingRequest in pendingRequests)
            {
                StorePendingCompileRequest(
                    pendingRequest.RequestId,
                    pendingRequest.ForceRecompile,
                    new DateTime(pendingRequest.ExpiresAtUtcTicks, DateTimeKind.Utc),
                    reloadObserved: true);
            }

            return true;
        }

        public UnityCliLoopPendingCompileRequest GetPendingCompileRequestForRequestId(string requestId)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(requestId), "requestId must not be null or whitespace");

            string expiresAtUtcTicksText = GetPendingCompileExpiresAtUtcTicks(requestId);
            (bool isValid, long expiresAtUtcTicks) =
                UnityCliLoopEditorSessionStateStorage.ParseUtcTicks(expiresAtUtcTicksText);
            if (!isValid || expiresAtUtcTicks <= 0)
            {
                UnityCliLoopPendingCompileRequest legacyRequest =
                    GetLegacyPendingCompileRequestForRequestId(requestId);
                if (legacyRequest.HasRequest)
                {
                    return legacyRequest;
                }

                ClearPendingCompileRequestForRequestId(requestId);
                return UnityCliLoopPendingCompileRequest.None();
            }

            return UnityCliLoopPendingCompileRequest.Create(
                requestId,
                GetPendingCompileForceRecompile(requestId),
                expiresAtUtcTicks,
                GetPendingCompileReloadObserved(requestId));
        }

        public void ClearPendingCompileRequest()
        {
            foreach (string requestId in UnityCliLoopEditorSessionStateStorage.ParseRequestIdIndex(
                GetPendingCompileRequestIds()))
            {
                ClearPendingCompileRequestValues(requestId);
            }

            SetPendingCompileRequestIds("");
            ClearLegacyPendingCompileRequest();
        }

        public bool ClearPendingCompileRequestIfMatches(string requestId)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(requestId), "requestId must not be null or whitespace");

            UnityCliLoopPendingCompileRequest pendingRequest =
                GetPendingCompileRequestForRequestId(requestId);
            if (!pendingRequest.HasRequest)
            {
                return false;
            }

            ClearPendingCompileRequestForRequestId(requestId);
            return true;
        }

        public bool ClearExpiredPendingCompileRequest(DateTime utcNow)
        {
            Debug.Assert(utcNow.Kind == DateTimeKind.Utc, "utcNow must be UTC");

            bool cleared = false;
            UnityCliLoopPendingCompileRequest[] pendingRequests = GetPendingCompileRequests();
            foreach (UnityCliLoopPendingCompileRequest pendingRequest in pendingRequests)
            {
                if (!pendingRequest.IsExpiredAt(utcNow))
                {
                    continue;
                }

                ClearPendingCompileRequestForRequestId(pendingRequest.RequestId);
                cleared = true;
            }

            return cleared;
        }

        // Tests and legacy migration use these helpers without widening the aggregate port.
        internal static void SetLegacyPendingCompileRequestId(string pendingCompileRequestId)
        {
            UnityCliLoopEditorSessionStateStorage.SetString(
                LegacyPendingCompileRequestIdKey,
                pendingCompileRequestId);
        }

        internal static string GetLegacyPendingCompileRequestId()
        {
            return UnityCliLoopEditorSessionStateStorage.GetString(LegacyPendingCompileRequestIdKey);
        }

        internal static void SetLegacyPendingCompileForceRecompile(bool pendingCompileForceRecompile)
        {
            UnityCliLoopEditorSessionStateStorage.SetBool(
                LegacyPendingCompileForceRecompileKey,
                pendingCompileForceRecompile);
        }

        internal static void SetLegacyPendingCompileExpiresAtUtcTicks(string pendingCompileExpiresAtUtcTicks)
        {
            UnityCliLoopEditorSessionStateStorage.SetString(
                LegacyPendingCompileExpiresAtUtcTicksKey,
                pendingCompileExpiresAtUtcTicks);
        }

        internal static void SetLegacyPendingCompileReloadObserved(bool pendingCompileReloadObserved)
        {
            UnityCliLoopEditorSessionStateStorage.SetBool(
                LegacyPendingCompileReloadObservedKey,
                pendingCompileReloadObserved);
        }

        internal static void SetPendingCompileRequestIds(string pendingCompileRequestIds)
        {
            UnityCliLoopEditorSessionStateStorage.SetString(PendingCompileRequestIdsKey, pendingCompileRequestIds);
        }

        internal static void SetPendingCompileForceRecompile(string requestId, bool pendingCompileForceRecompile)
        {
            UnityCliLoopEditorSessionStateStorage.SetBool(
                CreatePendingCompileKey(requestId, PendingCompileForceRecompileKeySuffix),
                pendingCompileForceRecompile);
        }

        internal static void SetPendingCompileExpiresAtUtcTicks(
            string requestId,
            string pendingCompileExpiresAtUtcTicks)
        {
            UnityCliLoopEditorSessionStateStorage.SetString(
                CreatePendingCompileKey(requestId, PendingCompileExpiresAtUtcTicksKeySuffix),
                pendingCompileExpiresAtUtcTicks);
        }

        internal static void SetPendingCompileReloadObserved(string requestId, bool pendingCompileReloadObserved)
        {
            UnityCliLoopEditorSessionStateStorage.SetBool(
                CreatePendingCompileKey(requestId, PendingCompileReloadObservedKeySuffix),
                pendingCompileReloadObserved);
        }

        private static string GetPendingCompileRequestIds()
        {
            return UnityCliLoopEditorSessionStateStorage.GetString(PendingCompileRequestIdsKey);
        }

        private static bool GetLegacyPendingCompileForceRecompile()
        {
            return UnityCliLoopEditorSessionStateStorage.GetBool(LegacyPendingCompileForceRecompileKey);
        }

        private static string GetLegacyPendingCompileExpiresAtUtcTicks()
        {
            return UnityCliLoopEditorSessionStateStorage.GetString(LegacyPendingCompileExpiresAtUtcTicksKey);
        }

        private static bool GetLegacyPendingCompileReloadObserved()
        {
            return UnityCliLoopEditorSessionStateStorage.GetBool(LegacyPendingCompileReloadObservedKey);
        }

        private static bool GetPendingCompileForceRecompile(string requestId)
        {
            return UnityCliLoopEditorSessionStateStorage.GetBool(
                CreatePendingCompileKey(requestId, PendingCompileForceRecompileKeySuffix));
        }

        private static string GetPendingCompileExpiresAtUtcTicks(string requestId)
        {
            return UnityCliLoopEditorSessionStateStorage.GetString(
                CreatePendingCompileKey(requestId, PendingCompileExpiresAtUtcTicksKeySuffix));
        }

        private static bool GetPendingCompileReloadObserved(string requestId)
        {
            return UnityCliLoopEditorSessionStateStorage.GetBool(
                CreatePendingCompileKey(requestId, PendingCompileReloadObservedKeySuffix));
        }

        private static void ClearPendingCompileRequestForRequestId(string requestId)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(requestId), "requestId must not be null or whitespace");

            ClearPendingCompileRequestValues(requestId);
            SetPendingCompileRequestIds(
                UnityCliLoopEditorSessionStateStorage.RemoveRequestIdFromIndex(
                    GetPendingCompileRequestIds(),
                    requestId));
        }

        private static void ClearPendingCompileRequestValues(string requestId)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(requestId), "requestId must not be null or whitespace");

            SetPendingCompileForceRecompile(requestId, false);
            SetPendingCompileExpiresAtUtcTicks(requestId, "");
            SetPendingCompileReloadObserved(requestId, false);
        }

        private static bool ContainsPendingCompileRequest(
            List<UnityCliLoopPendingCompileRequest> pendingRequests,
            string requestId)
        {
            Debug.Assert(pendingRequests != null, "pendingRequests must not be null");

            foreach (UnityCliLoopPendingCompileRequest pendingRequest in pendingRequests)
            {
                if (pendingRequest.RequestId == requestId)
                {
                    return true;
                }
            }

            return false;
        }

        private UnityCliLoopPendingCompileRequest GetLegacyPendingCompileRequest()
        {
            string requestId = GetLegacyPendingCompileRequestId();
            if (string.IsNullOrWhiteSpace(requestId))
            {
                return UnityCliLoopPendingCompileRequest.None();
            }

            return GetLegacyPendingCompileRequestForRequestId(requestId);
        }

        private UnityCliLoopPendingCompileRequest GetLegacyPendingCompileRequestForRequestId(string requestId)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(requestId), "requestId must not be null or whitespace");

            string legacyRequestId = GetLegacyPendingCompileRequestId();
            if (legacyRequestId != requestId)
            {
                return UnityCliLoopPendingCompileRequest.None();
            }

            string expiresAtUtcTicksText = GetLegacyPendingCompileExpiresAtUtcTicks();
            (bool isValid, long expiresAtUtcTicks) =
                UnityCliLoopEditorSessionStateStorage.ParseUtcTicks(expiresAtUtcTicksText);
            if (!isValid || expiresAtUtcTicks <= 0)
            {
                ClearLegacyPendingCompileRequest();
                return UnityCliLoopPendingCompileRequest.None();
            }

            bool forceRecompile = GetLegacyPendingCompileForceRecompile();
            bool reloadObserved = GetLegacyPendingCompileReloadObserved();
            StorePendingCompileRequest(
                requestId,
                forceRecompile,
                new DateTime(expiresAtUtcTicks, DateTimeKind.Utc),
                reloadObserved);
            ClearLegacyPendingCompileRequest();
            return UnityCliLoopPendingCompileRequest.Create(
                requestId,
                forceRecompile,
                expiresAtUtcTicks,
                reloadObserved);
        }

        private static void ClearLegacyPendingCompileRequest()
        {
            SetLegacyPendingCompileRequestId("");
            SetLegacyPendingCompileForceRecompile(false);
            SetLegacyPendingCompileExpiresAtUtcTicks("");
            SetLegacyPendingCompileReloadObserved(false);
        }

        private static string CreatePendingCompileKey(string requestId, string suffix)
        {
            return UnityCliLoopEditorSessionStateStorage.CreateRequestScopedKey(
                PendingCompileKeyPrefix,
                requestId,
                suffix);
        }
    }
}
