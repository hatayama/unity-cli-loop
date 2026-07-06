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
        private readonly ICompileResultSessionRepository _compileResultSessionRepository;
        private readonly IPendingCompileSessionRepository _pendingCompileSessionRepository;

        public UnityCliLoopEditorSessionStateService(
            IUnityCliLoopEditorSessionStatePort sessionStatePort,
            ICompileResultSessionRepository compileResultSessionRepository,
            IPendingCompileSessionRepository pendingCompileSessionRepository)
        {
            Debug.Assert(sessionStatePort != null, "sessionStatePort must not be null");
            Debug.Assert(compileResultSessionRepository != null, "compileResultSessionRepository must not be null");
            Debug.Assert(pendingCompileSessionRepository != null, "pendingCompileSessionRepository must not be null");

            _sessionStatePort = sessionStatePort ?? throw new ArgumentNullException(nameof(sessionStatePort));
            _compileResultSessionRepository = compileResultSessionRepository ??
                throw new ArgumentNullException(nameof(compileResultSessionRepository));
            _pendingCompileSessionRepository = pendingCompileSessionRepository ??
                throw new ArgumentNullException(nameof(pendingCompileSessionRepository));
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

        public void StoreCompileResult(
            string requestId,
            bool forceRecompile,
            string resultJson,
            DateTime completedAtUtc)
        {
            _compileResultSessionRepository.StoreCompileResult(
                requestId,
                forceRecompile,
                resultJson,
                completedAtUtc);
        }

        public UnityCliLoopStoredCompileResult GetCompileResult(string requestId)
        {
            return _compileResultSessionRepository.GetCompileResult(requestId);
        }

        public UnityCliLoopStoredCompileResult GetStoredCompileResult()
        {
            return _compileResultSessionRepository.GetStoredCompileResult();
        }

        public UnityCliLoopStoredCompileResult[] GetStoredCompileResults()
        {
            return _compileResultSessionRepository.GetStoredCompileResults();
        }

        public void ClearCompileResult()
        {
            _compileResultSessionRepository.ClearCompileResult();
        }

        public bool ClearExpiredCompileResult(DateTime utcNow)
        {
            Debug.Assert(utcNow.Kind == DateTimeKind.Utc, "utcNow must be UTC");

            return _compileResultSessionRepository.ClearExpiredCompileResult(utcNow, CompileResultLifetime);
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
                markedAtUtc.Add(CompileResultLifetime),
                reloadObserved: false);
        }

        public void StorePendingCompileRequest(
            string requestId,
            bool forceRecompile,
            DateTime expiresAtUtc,
            bool reloadObserved)
        {
            _pendingCompileSessionRepository.StorePendingCompileRequest(
                requestId,
                forceRecompile,
                expiresAtUtc,
                reloadObserved);
        }

        public UnityCliLoopPendingCompileRequest GetPendingCompileRequest()
        {
            UnityCliLoopPendingCompileRequest[] pendingRequests =
                _pendingCompileSessionRepository.GetPendingCompileRequests();
            if (pendingRequests.Length == 0)
            {
                return UnityCliLoopPendingCompileRequest.None();
            }

            return pendingRequests[0];
        }

        public UnityCliLoopPendingCompileRequest[] GetPendingCompileRequests()
        {
            return _pendingCompileSessionRepository.GetPendingCompileRequests();
        }

        public bool MarkPendingCompileRequestReloadObserved()
        {
            return _pendingCompileSessionRepository.MarkPendingCompileRequestReloadObserved();
        }

        public UnityCliLoopPendingCompileRequest GetPendingCompileRequestForRequestId(string requestId)
        {
            return _pendingCompileSessionRepository.GetPendingCompileRequestForRequestId(requestId);
        }

        public void ClearPendingCompileRequest()
        {
            _pendingCompileSessionRepository.ClearPendingCompileRequest();
        }

        public bool ClearPendingCompileRequestIfMatches(string requestId)
        {
            return _pendingCompileSessionRepository.ClearPendingCompileRequestIfMatches(requestId);
        }

        public bool ClearExpiredPendingCompileRequest(DateTime utcNow)
        {
            Debug.Assert(utcNow.Kind == DateTimeKind.Utc, "utcNow must be UTC");

            return _pendingCompileSessionRepository.ClearExpiredPendingCompileRequest(utcNow);
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
