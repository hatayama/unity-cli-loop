using System;
using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Coordinates compile session lifetime rules and reload transitions that span session flags.
    /// </summary>
    public sealed class UnityCliLoopCompileSessionLifecycleService
    {
        // Why 20m: Go compileWaitTimeout is 10m; TTL must stay longer (wait ≤ TTL) so a
        // timed-out client can still retrieve the result by retrying uloop compile for
        // about 10 more minutes. Why not keep 32m: shrink session-state leak window.
        private static readonly TimeSpan CompileResultLifetime = TimeSpan.FromMinutes(20);
        private readonly ISessionFlagsRepository _sessionFlagsRepository;
        private readonly ICompileResultSessionRepository _compileResultSessionRepository;
        private readonly IPendingCompileSessionRepository _pendingCompileSessionRepository;

        public UnityCliLoopCompileSessionLifecycleService(
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

        public void MarkDomainReloadStarted(bool serverIsRunning)
        {
            _sessionFlagsRepository.SetIsDomainReloadInProgress(true);
            _pendingCompileSessionRepository.MarkPendingCompileRequestReloadObserved();
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

        public void MarkPendingCompileRequest(
            string requestId,
            bool forceRecompile,
            DateTime markedAtUtc)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(requestId), "requestId must not be null or whitespace");
            Debug.Assert(markedAtUtc.Kind == DateTimeKind.Utc, "markedAtUtc must be UTC");

            _pendingCompileSessionRepository.StorePendingCompileRequest(
                requestId,
                forceRecompile,
                markedAtUtc.Add(CompileResultLifetime),
                reloadObserved: false);
        }

        public bool ClearExpiredCompileResult(DateTime utcNow)
        {
            Debug.Assert(utcNow.Kind == DateTimeKind.Utc, "utcNow must be UTC");

            return _compileResultSessionRepository.ClearExpiredCompileResult(utcNow, CompileResultLifetime);
        }

        public bool ClearExpiredPendingCompileRequest(DateTime utcNow)
        {
            Debug.Assert(utcNow.Kind == DateTimeKind.Utc, "utcNow must be UTC");

            return _pendingCompileSessionRepository.ClearExpiredPendingCompileRequest(utcNow);
        }
    }
}
