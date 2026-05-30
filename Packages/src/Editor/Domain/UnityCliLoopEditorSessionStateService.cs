using System;
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
        string GetCompileResultRequestId();
        void SetCompileResultRequestId(string compileResultRequestId);
        bool GetCompileResultForceRecompile();
        void SetCompileResultForceRecompile(bool compileResultForceRecompile);
        string GetCompileResultJson();
        void SetCompileResultJson(string compileResultJson);
        string GetCompileResultCompletedAtUtcTicks();
        void SetCompileResultCompletedAtUtcTicks(string compileResultCompletedAtUtcTicks);
        string GetPendingCompileRequestId();
        void SetPendingCompileRequestId(string pendingCompileRequestId);
        bool GetPendingCompileForceRecompile();
        void SetPendingCompileForceRecompile(bool pendingCompileForceRecompile);
        string GetPendingCompileExpiresAtUtcTicks();
        void SetPendingCompileExpiresAtUtcTicks(string pendingCompileExpiresAtUtcTicks);
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
            long expiresAtUtcTicks)
        {
            HasRequest = hasRequest;
            RequestId = requestId;
            ForceRecompile = forceRecompile;
            ExpiresAtUtcTicks = expiresAtUtcTicks;
        }

        public bool HasRequest { get; }
        public string RequestId { get; }
        public bool ForceRecompile { get; }
        public long ExpiresAtUtcTicks { get; }

        public static UnityCliLoopPendingCompileRequest None()
        {
            return new UnityCliLoopPendingCompileRequest(false, "", false, 0);
        }

        public static UnityCliLoopPendingCompileRequest Create(
            string requestId,
            bool forceRecompile,
            long expiresAtUtcTicks)
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
                expiresAtUtcTicks);
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

            return (true, value);
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

            _sessionStatePort.SetCompileResultRequestId(requestId);
            _sessionStatePort.SetCompileResultForceRecompile(forceRecompile);
            _sessionStatePort.SetCompileResultJson(resultJson);
            _sessionStatePort.SetCompileResultCompletedAtUtcTicks(completedAtUtc.Ticks.ToString());
        }

        public UnityCliLoopStoredCompileResult GetCompileResult(string requestId)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(requestId), "requestId must not be null or whitespace");

            UnityCliLoopStoredCompileResult storedResult = GetStoredCompileResult();
            if (!storedResult.HasResult)
            {
                return UnityCliLoopStoredCompileResult.None();
            }

            if (storedResult.RequestId != requestId)
            {
                return UnityCliLoopStoredCompileResult.None();
            }

            return storedResult;
        }

        public UnityCliLoopStoredCompileResult GetStoredCompileResult()
        {
            string requestId = _sessionStatePort.GetCompileResultRequestId();
            if (string.IsNullOrWhiteSpace(requestId))
            {
                return UnityCliLoopStoredCompileResult.None();
            }

            string resultJson = _sessionStatePort.GetCompileResultJson();
            if (string.IsNullOrWhiteSpace(resultJson))
            {
                ClearCompileResult();
                return UnityCliLoopStoredCompileResult.None();
            }

            string completedAtUtcTicksText = _sessionStatePort.GetCompileResultCompletedAtUtcTicks();
            (bool isValid, long completedAtUtcTicks) =
                ParseUtcTicks(completedAtUtcTicksText);
            if (!isValid || completedAtUtcTicks <= 0)
            {
                ClearCompileResult();
                return UnityCliLoopStoredCompileResult.None();
            }

            return UnityCliLoopStoredCompileResult.Create(
                requestId,
                _sessionStatePort.GetCompileResultForceRecompile(),
                resultJson,
                completedAtUtcTicks);
        }

        public void ClearCompileResult()
        {
            _sessionStatePort.SetCompileResultRequestId("");
            _sessionStatePort.SetCompileResultForceRecompile(false);
            _sessionStatePort.SetCompileResultJson("");
            _sessionStatePort.SetCompileResultCompletedAtUtcTicks("");
        }

        public bool ClearExpiredCompileResult(DateTime utcNow)
        {
            Debug.Assert(utcNow.Kind == DateTimeKind.Utc, "utcNow must be UTC");

            UnityCliLoopStoredCompileResult storedResult = GetStoredCompileResult();
            if (!storedResult.IsExpiredAt(utcNow, CompileResultLifetime))
            {
                return false;
            }

            ClearCompileResult();
            return true;
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
                markedAtUtc.Add(CompileResultLifetime).Ticks);
        }

        public void StorePendingCompileRequest(
            string requestId,
            bool forceRecompile,
            long expiresAtUtcTicks)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(requestId), "requestId must not be null or whitespace");
            Debug.Assert(expiresAtUtcTicks > 0, "expiresAtUtcTicks must be positive");

            _sessionStatePort.SetPendingCompileRequestId(requestId);
            _sessionStatePort.SetPendingCompileForceRecompile(forceRecompile);
            _sessionStatePort.SetPendingCompileExpiresAtUtcTicks(expiresAtUtcTicks.ToString());
        }

        public UnityCliLoopPendingCompileRequest GetPendingCompileRequest()
        {
            string requestId = _sessionStatePort.GetPendingCompileRequestId();
            if (string.IsNullOrWhiteSpace(requestId))
            {
                return UnityCliLoopPendingCompileRequest.None();
            }

            string expiresAtUtcTicksText = _sessionStatePort.GetPendingCompileExpiresAtUtcTicks();
            (bool isValid, long expiresAtUtcTicks) =
                ParseUtcTicks(expiresAtUtcTicksText);
            if (!isValid || expiresAtUtcTicks <= 0)
            {
                ClearPendingCompileRequest();
                return UnityCliLoopPendingCompileRequest.None();
            }

            return UnityCliLoopPendingCompileRequest.Create(
                requestId,
                _sessionStatePort.GetPendingCompileForceRecompile(),
                expiresAtUtcTicks);
        }

        public UnityCliLoopPendingCompileRequest GetPendingCompileRequestForRequestId(string requestId)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(requestId), "requestId must not be null or whitespace");

            UnityCliLoopPendingCompileRequest pendingRequest = GetPendingCompileRequest();
            if (!pendingRequest.HasRequest)
            {
                return UnityCliLoopPendingCompileRequest.None();
            }

            if (pendingRequest.RequestId != requestId)
            {
                return UnityCliLoopPendingCompileRequest.None();
            }

            return pendingRequest;
        }

        public void ClearPendingCompileRequest()
        {
            _sessionStatePort.SetPendingCompileRequestId("");
            _sessionStatePort.SetPendingCompileForceRecompile(false);
            _sessionStatePort.SetPendingCompileExpiresAtUtcTicks("");
        }

        public bool ClearPendingCompileRequestIfMatches(string requestId)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(requestId), "requestId must not be null or whitespace");

            UnityCliLoopPendingCompileRequest pendingRequest = GetPendingCompileRequest();
            if (!pendingRequest.HasRequest || pendingRequest.RequestId != requestId)
            {
                return false;
            }

            ClearPendingCompileRequest();
            return true;
        }

        public bool ClearExpiredPendingCompileRequest(DateTime utcNow)
        {
            Debug.Assert(utcNow.Kind == DateTimeKind.Utc, "utcNow must be UTC");

            UnityCliLoopPendingCompileRequest pendingRequest = GetPendingCompileRequest();
            if (!pendingRequest.IsExpiredAt(utcNow))
            {
                return false;
            }

            ClearPendingCompileRequest();
            return true;
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
