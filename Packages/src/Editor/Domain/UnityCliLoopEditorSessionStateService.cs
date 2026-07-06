using System;
using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.Domain
{
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
    /// Coordinates compile-result and pending-compile session repositories during compile recovery.
    /// </summary>
    public sealed class UnityCliLoopEditorSessionStateService
    {
        private static readonly TimeSpan CompileResultLifetime = TimeSpan.FromMinutes(32);
        private readonly ISessionFlagsRepository _sessionFlagsRepository;
        private readonly ICompileResultSessionRepository _compileResultSessionRepository;
        private readonly IPendingCompileSessionRepository _pendingCompileSessionRepository;

        public UnityCliLoopEditorSessionStateService(
            ISessionFlagsRepository sessionFlagsRepository,
            ICompileResultSessionRepository compileResultSessionRepository,
            IPendingCompileSessionRepository pendingCompileSessionRepository)
        {
            Debug.Assert(sessionFlagsRepository != null, "sessionFlagsRepository must not be null");
            Debug.Assert(compileResultSessionRepository != null, "compileResultSessionRepository must not be null");
            Debug.Assert(pendingCompileSessionRepository != null, "pendingCompileSessionRepository must not be null");

            _sessionFlagsRepository = sessionFlagsRepository ??
                throw new ArgumentNullException(nameof(sessionFlagsRepository));
            _compileResultSessionRepository = compileResultSessionRepository ??
                throw new ArgumentNullException(nameof(compileResultSessionRepository));
            _pendingCompileSessionRepository = pendingCompileSessionRepository ??
                throw new ArgumentNullException(nameof(pendingCompileSessionRepository));
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

        public void MarkDomainReloadStarted(bool serverIsRunning)
        {
            _sessionFlagsRepository.SetIsDomainReloadInProgress(true);
            MarkPendingCompileRequestReloadObserved();
            if (!serverIsRunning)
            {
                return;
            }

            _sessionFlagsRepository.MarkServerStarted();
            _sessionFlagsRepository.SetIsAfterCompile(true);
            _sessionFlagsRepository.SetIsReconnecting(true);
            _sessionFlagsRepository.SetShowReconnectingUI(true);
            _sessionFlagsRepository.SetShowPostCompileReconnectingUI(true);
        }
    }
}
