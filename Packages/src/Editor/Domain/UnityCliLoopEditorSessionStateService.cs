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
        string GetPendingCompileRequestId();
        void SetPendingCompileRequestId(string pendingCompileRequestId);
        bool GetPendingCompileForceRecompile();
        void SetPendingCompileForceRecompile(bool pendingCompileForceRecompile);
        string GetPendingCompileExpiresAtUtcTicks();
        void SetPendingCompileExpiresAtUtcTicks(string pendingCompileExpiresAtUtcTicks);
    }

    /// <summary>
    /// Records the compile request that must still produce a CLI result file after Domain Reload.
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

            if (string.IsNullOrWhiteSpace(requestId))
            {
                throw new ArgumentException("requestId must not be null or whitespace.", nameof(requestId));
            }

            return new UnityCliLoopPendingCompileRequest(true, requestId, forceRecompile, expiresAtUtcTicks);
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
        // Why: accepted compile requests can still be running while the CLI waits for its
        // long final-response budget, so recovery must outlive that active wait window.
        private const int PendingCompileRequestLifetimeSeconds = 32 * 60;
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

        public UnityCliLoopPendingCompileRequest GetPendingCompileRequest()
        {
            string requestId = _sessionStatePort.GetPendingCompileRequestId();
            if (string.IsNullOrWhiteSpace(requestId))
            {
                return UnityCliLoopPendingCompileRequest.None();
            }

            string expiresAtUtcTicksText = _sessionStatePort.GetPendingCompileExpiresAtUtcTicks();
            long expiresAtUtcTicks = string.IsNullOrWhiteSpace(expiresAtUtcTicksText)
                ? 0
                : Convert.ToInt64(expiresAtUtcTicksText);
            return UnityCliLoopPendingCompileRequest.Create(
                requestId,
                _sessionStatePort.GetPendingCompileForceRecompile(),
                expiresAtUtcTicks);
        }

        public void MarkPendingCompileRequest(string requestId, bool forceRecompile)
        {
            long expiresAtUtcTicks = DateTime.UtcNow
                .AddSeconds(PendingCompileRequestLifetimeSeconds)
                .Ticks;
            MarkPendingCompileRequestWithExpiration(requestId, forceRecompile, expiresAtUtcTicks);
        }

        public void MarkPendingCompileRequestWithExpiration(
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

        public void ClearPendingCompileRequest()
        {
            _sessionStatePort.SetPendingCompileRequestId("");
            _sessionStatePort.SetPendingCompileForceRecompile(false);
            _sessionStatePort.SetPendingCompileExpiresAtUtcTicks("");
        }

        public void ClearPendingCompileRequestIfMatches(string requestId)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(requestId), "requestId must not be null or whitespace");

            UnityCliLoopPendingCompileRequest pendingCompileRequest = GetPendingCompileRequest();
            if (!pendingCompileRequest.HasRequest)
            {
                return;
            }

            if (pendingCompileRequest.RequestId != requestId)
            {
                return;
            }

            ClearPendingCompileRequest();
        }

        public bool ClearExpiredPendingCompileRequest(DateTime utcNow)
        {
            Debug.Assert(utcNow.Kind == DateTimeKind.Utc, "utcNow must be UTC");

            UnityCliLoopPendingCompileRequest pendingCompileRequest = GetPendingCompileRequest();
            if (!pendingCompileRequest.IsExpiredAt(utcNow))
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
            ClearPendingCompileRequest();
            SetShouldAutoScanThirdPartyToolMigration(false);
            SetIsServerManuallyStopped(false);
        }
    }
}
