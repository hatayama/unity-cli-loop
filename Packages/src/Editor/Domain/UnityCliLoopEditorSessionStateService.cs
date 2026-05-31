using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    public interface IUnityCliLoopEditorSessionStatePort
    {
        bool GetIsServerRunning();
        void SetIsServerRunning(bool isServerRunning);
        bool GetIsServerManuallyStopped();
        void SetIsServerManuallyStopped(bool isServerManuallyStopped);
        bool GetIsAfterCompile();
        void SetIsAfterCompile(bool isAfterCompile);
        bool GetIsDomainReloadInProgress();
        void SetIsDomainReloadInProgress(bool isDomainReloadInProgress);
        bool GetIsReconnecting();
        void SetIsReconnecting(bool isReconnecting);
        bool GetShowReconnectingUI();
        void SetShowReconnectingUI(bool showReconnectingUI);
        bool GetShowPostCompileReconnectingUI();
        void SetShowPostCompileReconnectingUI(bool showPostCompileReconnectingUI);
        bool GetShouldAutoScanThirdPartyToolMigration();
        void SetShouldAutoScanThirdPartyToolMigration(bool shouldAutoScanThirdPartyToolMigration);
        string GetCompileResultRequestIds();
        void SetCompileResultRequestIds(string compileResultRequestIds);
        string GetLegacyCompileResultRequestId();
        void SetLegacyCompileResultRequestId(string compileResultRequestId);
        bool GetLegacyCompileResultForceRecompile();
        void SetLegacyCompileResultForceRecompile(bool compileResultForceRecompile);
        string GetLegacyCompileResultJson();
        void SetLegacyCompileResultJson(string compileResultJson);
        string GetLegacyCompileResultCompletedAtUtcTicks();
        void SetLegacyCompileResultCompletedAtUtcTicks(string compileResultCompletedAtUtcTicks);
        bool GetCompileResultForceRecompile(string requestId);
        void SetCompileResultForceRecompile(string requestId, bool compileResultForceRecompile);
        string GetCompileResultJson(string requestId);
        void SetCompileResultJson(string requestId, string compileResultJson);
        string GetCompileResultCompletedAtUtcTicks(string requestId);
        void SetCompileResultCompletedAtUtcTicks(string requestId, string compileResultCompletedAtUtcTicks);
        string GetPendingCompileRequestIds();
        void SetPendingCompileRequestIds(string pendingCompileRequestIds);
        string GetLegacyPendingCompileRequestId();
        void SetLegacyPendingCompileRequestId(string pendingCompileRequestId);
        bool GetLegacyPendingCompileForceRecompile();
        void SetLegacyPendingCompileForceRecompile(bool pendingCompileForceRecompile);
        string GetLegacyPendingCompileExpiresAtUtcTicks();
        void SetLegacyPendingCompileExpiresAtUtcTicks(string pendingCompileExpiresAtUtcTicks);
        bool GetLegacyPendingCompileReloadObserved();
        void SetLegacyPendingCompileReloadObserved(bool pendingCompileReloadObserved);
        bool GetPendingCompileForceRecompile(string requestId);
        void SetPendingCompileForceRecompile(string requestId, bool pendingCompileForceRecompile);
        string GetPendingCompileExpiresAtUtcTicks(string requestId);
        void SetPendingCompileExpiresAtUtcTicks(string requestId, string pendingCompileExpiresAtUtcTicks);
        bool GetPendingCompileReloadObserved(string requestId);
        void SetPendingCompileReloadObserved(string requestId, bool pendingCompileReloadObserved);
    }

    /// <summary>
    /// Records the compile result that the CLI reads after Unity finishes Domain Reload.
    /// </summary>
    public sealed class UnityCliLoopStoredCompileResult
    {
        private UnityCliLoopStoredCompileResult(
            bool hasResult,
            string requestId,
            bool forceRecompile,
            string resultJson,
            long completedAtUtcTicks)
        {
            HasResult = hasResult;
            RequestId = requestId;
            ForceRecompile = forceRecompile;
            ResultJson = resultJson;
            CompletedAtUtcTicks = completedAtUtcTicks;
        }

        public bool HasResult { get; }
        public string RequestId { get; }
        public bool ForceRecompile { get; }
        public string ResultJson { get; }
        public long CompletedAtUtcTicks { get; }

        public static UnityCliLoopStoredCompileResult None()
        {
            return new UnityCliLoopStoredCompileResult(false, "", false, "", 0);
        }

        public static UnityCliLoopStoredCompileResult Create(
            string requestId,
            bool forceRecompile,
            string resultJson,
            long completedAtUtcTicks)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(requestId), "requestId must not be null or whitespace");
            Debug.Assert(!string.IsNullOrWhiteSpace(resultJson), "resultJson must not be null or whitespace");
            Debug.Assert(completedAtUtcTicks > 0, "completedAtUtcTicks must be positive");

            if (string.IsNullOrWhiteSpace(requestId))
            {
                throw new ArgumentException("requestId must not be null or whitespace.", nameof(requestId));
            }

            if (string.IsNullOrWhiteSpace(resultJson))
            {
                throw new ArgumentException("resultJson must not be null or whitespace.", nameof(resultJson));
            }

            return new UnityCliLoopStoredCompileResult(
                true,
                requestId,
                forceRecompile,
                resultJson,
                completedAtUtcTicks);
        }

        public bool IsExpiredAt(DateTime utcNow, TimeSpan lifetime)
        {
            Debug.Assert(utcNow.Kind == DateTimeKind.Utc, "utcNow must be UTC");
            Debug.Assert(lifetime > TimeSpan.Zero, "lifetime must be positive");
            return HasResult && CompletedAtUtcTicks <= (utcNow - lifetime).Ticks;
        }
    }

    /// <summary>
    /// Records a compile request that may need an indeterminate result after Domain Reload.
    /// </summary>
    public sealed class UnityCliLoopPendingCompileRequest
    {
        private UnityCliLoopPendingCompileRequest(
            bool hasRequest,
            string requestId,
            bool forceRecompile,
            long expiresAtUtcTicks,
            bool reloadObserved)
        {
            HasRequest = hasRequest;
            RequestId = requestId;
            ForceRecompile = forceRecompile;
            ExpiresAtUtcTicks = expiresAtUtcTicks;
            ReloadObserved = reloadObserved;
        }

        public bool HasRequest { get; }
        public string RequestId { get; }
        public bool ForceRecompile { get; }
        public long ExpiresAtUtcTicks { get; }
        public bool ReloadObserved { get; }

        public static UnityCliLoopPendingCompileRequest None()
        {
            return new UnityCliLoopPendingCompileRequest(false, "", false, 0, false);
        }

        public static UnityCliLoopPendingCompileRequest Create(
            string requestId,
            bool forceRecompile,
            long expiresAtUtcTicks,
            bool reloadObserved)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(requestId), "requestId must not be null or whitespace");
            Debug.Assert(expiresAtUtcTicks > 0, "expiresAtUtcTicks must be positive");

            if (string.IsNullOrWhiteSpace(requestId))
            {
                throw new ArgumentException("requestId must not be null or whitespace.", nameof(requestId));
            }

            return new UnityCliLoopPendingCompileRequest(
                true,
                requestId,
                forceRecompile,
                expiresAtUtcTicks,
                reloadObserved);
        }

        public bool IsExpiredAt(DateTime utcNow)
        {
            Debug.Assert(utcNow.Kind == DateTimeKind.Utc, "utcNow must be UTC");
            return HasRequest && ExpiresAtUtcTicks <= utcNow.Ticks;
        }
    }

    /// <summary>
    /// Coordinates Unity Editor session-scoped runtime flags through the storage port owned by Infrastructure.
    /// </summary>
    public sealed class UnityCliLoopEditorSessionStateService
    {
        private static readonly TimeSpan CompileResultLifetime = TimeSpan.FromMinutes(32);
        private readonly IUnityCliLoopEditorSessionStatePort _sessionStatePort;

        public UnityCliLoopEditorSessionStateService(IUnityCliLoopEditorSessionStatePort sessionStatePort)
        {
            Debug.Assert(sessionStatePort != null, "sessionStatePort must not be null");

            _sessionStatePort = sessionStatePort ?? throw new ArgumentNullException(nameof(sessionStatePort));
        }

        public bool GetIsServerRunning()
        {
            return _sessionStatePort.GetIsServerRunning();
        }

        public void SetIsServerRunning(bool isServerRunning)
        {
            _sessionStatePort.SetIsServerRunning(isServerRunning);
        }

        public bool GetIsServerManuallyStopped()
        {
            return _sessionStatePort.GetIsServerManuallyStopped();
        }

        public void SetIsServerManuallyStopped(bool isServerManuallyStopped)
        {
            _sessionStatePort.SetIsServerManuallyStopped(isServerManuallyStopped);
        }

        public bool GetIsAfterCompile()
        {
            return _sessionStatePort.GetIsAfterCompile();
        }

        public void SetIsAfterCompile(bool isAfterCompile)
        {
            _sessionStatePort.SetIsAfterCompile(isAfterCompile);
        }

        public bool GetIsDomainReloadInProgress()
        {
            return _sessionStatePort.GetIsDomainReloadInProgress();
        }

        public void SetIsDomainReloadInProgress(bool isDomainReloadInProgress)
        {
            _sessionStatePort.SetIsDomainReloadInProgress(isDomainReloadInProgress);
        }

        public bool GetIsReconnecting()
        {
            return _sessionStatePort.GetIsReconnecting();
        }

        public void SetIsReconnecting(bool isReconnecting)
        {
            _sessionStatePort.SetIsReconnecting(isReconnecting);
        }

        public bool GetShowReconnectingUI()
        {
            return _sessionStatePort.GetShowReconnectingUI();
        }

        public void SetShowReconnectingUI(bool showReconnectingUI)
        {
            _sessionStatePort.SetShowReconnectingUI(showReconnectingUI);
        }

        public bool GetShowPostCompileReconnectingUI()
        {
            return _sessionStatePort.GetShowPostCompileReconnectingUI();
        }

        public void SetShowPostCompileReconnectingUI(bool showPostCompileReconnectingUI)
        {
            _sessionStatePort.SetShowPostCompileReconnectingUI(showPostCompileReconnectingUI);
        }

        public bool GetShouldAutoScanThirdPartyToolMigration()
        {
            return _sessionStatePort.GetShouldAutoScanThirdPartyToolMigration();
        }

        public void SetShouldAutoScanThirdPartyToolMigration(bool shouldAutoScanThirdPartyToolMigration)
        {
            _sessionStatePort.SetShouldAutoScanThirdPartyToolMigration(shouldAutoScanThirdPartyToolMigration);
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

        public void StoreCompileResult(
            string requestId,
            bool forceRecompile,
            string resultJson,
            DateTime completedAtUtc)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(requestId), "requestId must not be null or whitespace");
            Debug.Assert(!string.IsNullOrWhiteSpace(resultJson), "resultJson must not be null or whitespace");
            Debug.Assert(completedAtUtc.Kind == DateTimeKind.Utc, "completedAtUtc must be UTC");

            _sessionStatePort.SetCompileResultRequestIds(
                AddRequestIdToIndex(_sessionStatePort.GetCompileResultRequestIds(), requestId));
            _sessionStatePort.SetCompileResultForceRecompile(requestId, forceRecompile);
            _sessionStatePort.SetCompileResultJson(requestId, resultJson);
            _sessionStatePort.SetCompileResultCompletedAtUtcTicks(requestId, completedAtUtc.Ticks.ToString());
        }

        public UnityCliLoopStoredCompileResult GetCompileResult(string requestId)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(requestId), "requestId must not be null or whitespace");

            string resultJson = _sessionStatePort.GetCompileResultJson(requestId);
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

            string completedAtUtcTicksText = _sessionStatePort.GetCompileResultCompletedAtUtcTicks(requestId);
            (bool isValid, long completedAtUtcTicks) =
                ParseUtcTicks(completedAtUtcTicksText);
            if (!isValid || completedAtUtcTicks <= 0)
            {
                ClearCompileResultForRequestId(requestId);
                return UnityCliLoopStoredCompileResult.None();
            }

            return UnityCliLoopStoredCompileResult.Create(
                requestId,
                _sessionStatePort.GetCompileResultForceRecompile(requestId),
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
            string[] requestIds = ParseRequestIdIndex(_sessionStatePort.GetCompileResultRequestIds());
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
            foreach (string requestId in ParseRequestIdIndex(_sessionStatePort.GetCompileResultRequestIds()))
            {
                ClearCompileResultValues(requestId);
            }

            _sessionStatePort.SetCompileResultRequestIds("");
            ClearLegacyCompileResult();
        }

        private void ClearCompileResultForRequestId(string requestId)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(requestId), "requestId must not be null or whitespace");

            ClearCompileResultValues(requestId);
            _sessionStatePort.SetCompileResultRequestIds(
                RemoveRequestIdFromIndex(_sessionStatePort.GetCompileResultRequestIds(), requestId));
        }

        private void ClearCompileResultValues(string requestId)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(requestId), "requestId must not be null or whitespace");

            _sessionStatePort.SetCompileResultForceRecompile(requestId, false);
            _sessionStatePort.SetCompileResultJson(requestId, "");
            _sessionStatePort.SetCompileResultCompletedAtUtcTicks(requestId, "");
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
            string requestId = _sessionStatePort.GetLegacyCompileResultRequestId();
            if (string.IsNullOrWhiteSpace(requestId))
            {
                return UnityCliLoopStoredCompileResult.None();
            }

            return GetLegacyCompileResultForRequestId(requestId);
        }

        private UnityCliLoopStoredCompileResult GetLegacyCompileResultForRequestId(string requestId)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(requestId), "requestId must not be null or whitespace");

            string legacyRequestId = _sessionStatePort.GetLegacyCompileResultRequestId();
            if (legacyRequestId != requestId)
            {
                return UnityCliLoopStoredCompileResult.None();
            }

            string resultJson = _sessionStatePort.GetLegacyCompileResultJson();
            if (string.IsNullOrWhiteSpace(resultJson))
            {
                ClearLegacyCompileResult();
                return UnityCliLoopStoredCompileResult.None();
            }

            string completedAtUtcTicksText =
                _sessionStatePort.GetLegacyCompileResultCompletedAtUtcTicks();
            (bool isValid, long completedAtUtcTicks) =
                ParseUtcTicks(completedAtUtcTicksText);
            if (!isValid || completedAtUtcTicks <= 0)
            {
                ClearLegacyCompileResult();
                return UnityCliLoopStoredCompileResult.None();
            }

            bool forceRecompile = _sessionStatePort.GetLegacyCompileResultForceRecompile();
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

        private void ClearLegacyCompileResult()
        {
            _sessionStatePort.SetLegacyCompileResultRequestId("");
            _sessionStatePort.SetLegacyCompileResultForceRecompile(false);
            _sessionStatePort.SetLegacyCompileResultJson("");
            _sessionStatePort.SetLegacyCompileResultCompletedAtUtcTicks("");
        }

        public bool ClearExpiredCompileResult(DateTime utcNow)
        {
            Debug.Assert(utcNow.Kind == DateTimeKind.Utc, "utcNow must be UTC");

            bool cleared = false;
            UnityCliLoopStoredCompileResult[] storedResults = GetStoredCompileResults();
            foreach (UnityCliLoopStoredCompileResult storedResult in storedResults)
            {
                if (!storedResult.IsExpiredAt(utcNow, CompileResultLifetime))
                {
                    continue;
                }

                ClearCompileResultForRequestId(storedResult.RequestId);
                cleared = true;
            }

            return cleared;
        }

        public void MarkPendingCompileRequest(
            string requestId,
            bool forceRecompile,
            DateTime markedAtUtc)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(requestId), "requestId must not be null or whitespace");
            Debug.Assert(markedAtUtc.Kind == DateTimeKind.Utc, "markedAtUtc must be UTC");

            StorePendingCompileRequest(
                requestId,
                forceRecompile,
                markedAtUtc.Add(CompileResultLifetime).Ticks,
                reloadObserved: false);
        }

        public void StorePendingCompileRequest(
            string requestId,
            bool forceRecompile,
            long expiresAtUtcTicks,
            bool reloadObserved)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(requestId), "requestId must not be null or whitespace");
            Debug.Assert(expiresAtUtcTicks > 0, "expiresAtUtcTicks must be positive");

            _sessionStatePort.SetPendingCompileRequestIds(
                AddRequestIdToIndex(_sessionStatePort.GetPendingCompileRequestIds(), requestId));
            _sessionStatePort.SetPendingCompileForceRecompile(requestId, forceRecompile);
            _sessionStatePort.SetPendingCompileExpiresAtUtcTicks(requestId, expiresAtUtcTicks.ToString());
            _sessionStatePort.SetPendingCompileReloadObserved(requestId, reloadObserved);
        }

        public UnityCliLoopPendingCompileRequest GetPendingCompileRequest()
        {
            UnityCliLoopPendingCompileRequest[] pendingRequests = GetPendingCompileRequests();
            if (pendingRequests.Length == 0)
            {
                return UnityCliLoopPendingCompileRequest.None();
            }

            return pendingRequests[0];
        }

        public UnityCliLoopPendingCompileRequest[] GetPendingCompileRequests()
        {
            string[] requestIds = ParseRequestIdIndex(_sessionStatePort.GetPendingCompileRequestIds());
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
                    pendingRequest.ExpiresAtUtcTicks,
                    reloadObserved: true);
            }

            return true;
        }

        public UnityCliLoopPendingCompileRequest GetPendingCompileRequestForRequestId(string requestId)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(requestId), "requestId must not be null or whitespace");

            string expiresAtUtcTicksText = _sessionStatePort.GetPendingCompileExpiresAtUtcTicks(requestId);
            (bool isValid, long expiresAtUtcTicks) =
                ParseUtcTicks(expiresAtUtcTicksText);
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
                _sessionStatePort.GetPendingCompileForceRecompile(requestId),
                expiresAtUtcTicks,
                _sessionStatePort.GetPendingCompileReloadObserved(requestId));
        }

        public void ClearPendingCompileRequest()
        {
            foreach (string requestId in ParseRequestIdIndex(_sessionStatePort.GetPendingCompileRequestIds()))
            {
                ClearPendingCompileRequestValues(requestId);
            }

            _sessionStatePort.SetPendingCompileRequestIds("");
            ClearLegacyPendingCompileRequest();
        }

        private void ClearPendingCompileRequestForRequestId(string requestId)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(requestId), "requestId must not be null or whitespace");

            ClearPendingCompileRequestValues(requestId);
            _sessionStatePort.SetPendingCompileRequestIds(
                RemoveRequestIdFromIndex(_sessionStatePort.GetPendingCompileRequestIds(), requestId));
        }

        private void ClearPendingCompileRequestValues(string requestId)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(requestId), "requestId must not be null or whitespace");

            _sessionStatePort.SetPendingCompileForceRecompile(requestId, false);
            _sessionStatePort.SetPendingCompileExpiresAtUtcTicks(requestId, "");
            _sessionStatePort.SetPendingCompileReloadObserved(requestId, false);
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
            string requestId = _sessionStatePort.GetLegacyPendingCompileRequestId();
            if (string.IsNullOrWhiteSpace(requestId))
            {
                return UnityCliLoopPendingCompileRequest.None();
            }

            return GetLegacyPendingCompileRequestForRequestId(requestId);
        }

        private UnityCliLoopPendingCompileRequest GetLegacyPendingCompileRequestForRequestId(string requestId)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(requestId), "requestId must not be null or whitespace");

            string legacyRequestId = _sessionStatePort.GetLegacyPendingCompileRequestId();
            if (legacyRequestId != requestId)
            {
                return UnityCliLoopPendingCompileRequest.None();
            }

            string expiresAtUtcTicksText =
                _sessionStatePort.GetLegacyPendingCompileExpiresAtUtcTicks();
            (bool isValid, long expiresAtUtcTicks) =
                ParseUtcTicks(expiresAtUtcTicksText);
            if (!isValid || expiresAtUtcTicks <= 0)
            {
                ClearLegacyPendingCompileRequest();
                return UnityCliLoopPendingCompileRequest.None();
            }

            bool forceRecompile = _sessionStatePort.GetLegacyPendingCompileForceRecompile();
            bool reloadObserved = _sessionStatePort.GetLegacyPendingCompileReloadObserved();
            StorePendingCompileRequest(
                requestId,
                forceRecompile,
                expiresAtUtcTicks,
                reloadObserved);
            ClearLegacyPendingCompileRequest();
            return UnityCliLoopPendingCompileRequest.Create(
                requestId,
                forceRecompile,
                expiresAtUtcTicks,
                reloadObserved);
        }

        private void ClearLegacyPendingCompileRequest()
        {
            _sessionStatePort.SetLegacyPendingCompileRequestId("");
            _sessionStatePort.SetLegacyPendingCompileForceRecompile(false);
            _sessionStatePort.SetLegacyPendingCompileExpiresAtUtcTicks("");
            _sessionStatePort.SetLegacyPendingCompileReloadObserved(false);
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

        public bool ConsumeShouldAutoScanThirdPartyToolMigration()
        {
            if (!GetShouldAutoScanThirdPartyToolMigration())
            {
                return false;
            }

            SetShouldAutoScanThirdPartyToolMigration(false);
            return true;
        }

        public void MarkDomainReloadStarted(bool serverIsRunning)
        {
            SetIsDomainReloadInProgress(true);
            MarkPendingCompileRequestReloadObserved();
            if (!serverIsRunning)
            {
                return;
            }

            SetIsServerRunning(true);
            SetIsServerManuallyStopped(false);
            SetIsAfterCompile(true);
            SetIsReconnecting(true);
            SetShowReconnectingUI(true);
            SetShowPostCompileReconnectingUI(true);
        }

        public void MarkServerStarted()
        {
            SetIsServerRunning(true);
            SetIsServerManuallyStopped(false);
        }

        public void MarkServerManuallyStopped()
        {
            ClearServerSession();
            SetIsServerManuallyStopped(true);
        }

        public void ClearServerSession()
        {
            SetIsServerRunning(false);
        }

        public void ClearAfterCompileFlag()
        {
            SetIsAfterCompile(false);
        }

        public void ClearReconnectingFlags()
        {
            SetIsReconnecting(false);
            SetShowReconnectingUI(false);
        }

        public void ClearPostCompileReconnectingUI()
        {
            SetShowPostCompileReconnectingUI(false);
        }

        public void ClearDomainReloadFlag()
        {
            SetIsDomainReloadInProgress(false);
        }

        public void ClearDomainReloadRecoveryFlags()
        {
            SetIsDomainReloadInProgress(false);
            SetIsAfterCompile(false);
            SetIsReconnecting(false);
            SetShowReconnectingUI(false);
            SetShowPostCompileReconnectingUI(false);
        }

        public void ClearAll()
        {
            ClearServerSession();
            ClearDomainReloadRecoveryFlags();
            ClearCompileResult();
            ClearPendingCompileRequest();
            SetShouldAutoScanThirdPartyToolMigration(false);
            SetIsServerManuallyStopped(false);
        }
    }
}
