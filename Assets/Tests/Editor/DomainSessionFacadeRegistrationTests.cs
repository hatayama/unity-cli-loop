using System;
using System.Collections.Generic;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies the Domain session facades hand out the instance most recently registered.
    /// The CompositionRoot registers the real instances at editor load; each test swaps in a stand-in and
    /// the original instances are registered again in TearDown so the running server keeps its repositories.
    /// </summary>
    public sealed class DomainSessionFacadeRegistrationTests
    {
        private ISessionFlagsRepository _originalSessionFlagsRepository;
        private ICompileResultSessionRepository _originalCompileResultRepository;
        private IPendingCompileSessionRepository _originalPendingCompileRepository;
        private IRunTestsSessionRepository _originalRunTestsRepository;
        private UnityCliLoopCompileSessionLifecycleService _originalLifecycleService;

        [SetUp]
        public void SetUp()
        {
            _originalSessionFlagsRepository = UnityCliLoopSessionFlagsFacade.Repository;
            _originalCompileResultRepository = UnityCliLoopCompileResultSessionRepositoryFacade.Repository;
            _originalPendingCompileRepository = UnityCliLoopPendingCompileSessionRepositoryFacade.Repository;
            _originalRunTestsRepository = UnityCliLoopRunTestsSessionRepositoryFacade.Repository;
            _originalLifecycleService = UnityCliLoopCompileSessionLifecycleFacade.Service;
        }

        [TearDown]
        public void TearDown()
        {
            UnityCliLoopSessionFlagsFacade.RegisterRepository(_originalSessionFlagsRepository);
            UnityCliLoopCompileResultSessionRepositoryFacade.RegisterRepository(_originalCompileResultRepository);
            UnityCliLoopPendingCompileSessionRepositoryFacade.RegisterRepository(_originalPendingCompileRepository);
            UnityCliLoopRunTestsSessionRepositoryFacade.RegisterRepository(_originalRunTestsRepository);
            UnityCliLoopCompileSessionLifecycleFacade.RegisterService(_originalLifecycleService);
        }

        /// <summary>
        /// Verifies that the session flags facade returns the repository registered last.
        /// </summary>
        [Test]
        public void SessionFlagsFacade_WhenRepositoryIsRegistered_ReturnsRegisteredRepository()
        {
            ISessionFlagsRepository repository = new UnusedSessionFlagsRepository();

            UnityCliLoopSessionFlagsFacade.RegisterRepository(repository);

            Assert.That(UnityCliLoopSessionFlagsFacade.Repository, Is.SameAs(repository));
        }

        /// <summary>
        /// Verifies that the compile result facade returns the repository registered last.
        /// </summary>
        [Test]
        public void CompileResultFacade_WhenRepositoryIsRegistered_ReturnsRegisteredRepository()
        {
            ICompileResultSessionRepository repository = new UnusedCompileResultSessionRepository();

            UnityCliLoopCompileResultSessionRepositoryFacade.RegisterRepository(repository);

            Assert.That(UnityCliLoopCompileResultSessionRepositoryFacade.Repository, Is.SameAs(repository));
        }

        /// <summary>
        /// Verifies that the pending compile facade returns the repository registered last.
        /// </summary>
        [Test]
        public void PendingCompileFacade_WhenRepositoryIsRegistered_ReturnsRegisteredRepository()
        {
            IPendingCompileSessionRepository repository = new UnusedPendingCompileSessionRepository();

            UnityCliLoopPendingCompileSessionRepositoryFacade.RegisterRepository(repository);

            Assert.That(UnityCliLoopPendingCompileSessionRepositoryFacade.Repository, Is.SameAs(repository));
        }

        /// <summary>
        /// Verifies that the run-tests facade returns the repository registered last.
        /// </summary>
        [Test]
        public void RunTestsFacade_WhenRepositoryIsRegistered_ReturnsRegisteredRepository()
        {
            IRunTestsSessionRepository repository = new UnusedRunTestsSessionRepository();

            UnityCliLoopRunTestsSessionRepositoryFacade.RegisterRepository(repository);

            Assert.That(UnityCliLoopRunTestsSessionRepositoryFacade.Repository, Is.SameAs(repository));
        }

        /// <summary>
        /// Verifies that the compile session lifecycle facade returns the service registered last.
        /// </summary>
        [Test]
        public void CompileSessionLifecycleFacade_WhenServiceIsRegistered_ReturnsRegisteredService()
        {
            UnityCliLoopCompileSessionLifecycleService service = new UnityCliLoopCompileSessionLifecycleService(
                new UnusedSessionFlagsRepository(),
                new UnusedCompileResultSessionRepository(),
                new UnusedPendingCompileSessionRepository());

            UnityCliLoopCompileSessionLifecycleFacade.RegisterService(service);

            Assert.That(UnityCliLoopCompileSessionLifecycleFacade.Service, Is.SameAs(service));
        }

        private sealed class UnusedSessionFlagsRepository : ISessionFlagsRepository
        {
            public bool GetIsServerRunning() => throw new NotSupportedException();
            public bool GetIsServerManuallyStopped() => throw new NotSupportedException();
            public bool GetIsAfterCompile() => throw new NotSupportedException();
            public bool GetIsDomainReloadInProgress() => throw new NotSupportedException();
            public bool GetShowReconnectingUI() => throw new NotSupportedException();
            public void SetIsAfterCompile(bool isAfterCompile) => throw new NotSupportedException();
            public void SetIsDomainReloadInProgress(bool isDomainReloadInProgress) => throw new NotSupportedException();
            public void SetIsReconnecting(bool isReconnecting) => throw new NotSupportedException();
            public void SetShowReconnectingUI(bool showReconnectingUI) => throw new NotSupportedException();
            public void SetShowPostCompileReconnectingUI(bool showPostCompileReconnectingUI) => throw new NotSupportedException();
            public void SetShouldAutoScanThirdPartyToolMigration(bool shouldAutoScanThirdPartyToolMigration) => throw new NotSupportedException();
            public bool ConsumeShouldAutoScanThirdPartyToolMigration() => throw new NotSupportedException();
            public void MarkServerStarted() => throw new NotSupportedException();
            public void MarkServerManuallyStopped() => throw new NotSupportedException();
            public void ClearServerSession() => throw new NotSupportedException();
            public void ClearAfterCompileFlag() => throw new NotSupportedException();
            public void ClearReconnectingFlags() => throw new NotSupportedException();
            public void ClearPostCompileReconnectingUI() => throw new NotSupportedException();
            public void ClearDomainReloadFlag() => throw new NotSupportedException();
            public void ClearDomainReloadRecoveryFlags() => throw new NotSupportedException();
        }

        private sealed class UnusedCompileResultSessionRepository : ICompileResultSessionRepository
        {
            public void StoreCompileResult(string requestId, bool forceRecompile, string resultJson, DateTime completedAtUtc) => throw new NotSupportedException();
            public UnityCliLoopStoredCompileResult GetCompileResult(string requestId) => throw new NotSupportedException();
            public UnityCliLoopStoredCompileResult GetStoredCompileResult() => throw new NotSupportedException();
            public UnityCliLoopStoredCompileResult[] GetStoredCompileResults() => throw new NotSupportedException();
            public void ClearCompileResult() => throw new NotSupportedException();
            public bool ClearExpiredCompileResult(DateTime utcNow, TimeSpan lifetime) => throw new NotSupportedException();
        }

        private sealed class UnusedPendingCompileSessionRepository : IPendingCompileSessionRepository
        {
            public void StorePendingCompileRequest(string requestId, bool forceRecompile, DateTime expiresAtUtc, bool reloadObserved) => throw new NotSupportedException();
            public UnityCliLoopPendingCompileRequest[] GetPendingCompileRequests() => throw new NotSupportedException();
            public bool MarkPendingCompileRequestReloadObserved() => throw new NotSupportedException();
            public UnityCliLoopPendingCompileRequest GetPendingCompileRequestForRequestId(string requestId) => throw new NotSupportedException();
            public void ClearPendingCompileRequest() => throw new NotSupportedException();
            public bool ClearPendingCompileRequestIfMatches(string requestId) => throw new NotSupportedException();
            public bool ClearExpiredPendingCompileRequest(DateTime utcNow) => throw new NotSupportedException();
        }

        private sealed class UnusedRunTestsSessionRepository : IRunTestsSessionRepository
        {
            public void StorePendingRun(string requestId, DateTime expiresAtUtc) => throw new NotSupportedException();
            public bool HasPendingRun(string requestId) => throw new NotSupportedException();
            public IReadOnlyList<string> GetPendingRunRequestIds() => throw new NotSupportedException();
            public bool HasAnyPendingRun() => throw new NotSupportedException();
            public void ClearPendingRun(string requestId) => throw new NotSupportedException();
            public void StoreRunResult(string requestId, string resultJson, DateTime completedAtUtc) => throw new NotSupportedException();
            public UnityCliLoopStoredRunTestsResult GetRunResult(string requestId) => throw new NotSupportedException();
            public void ClearExpired(DateTime utcNow) => throw new NotSupportedException();
        }
    }
}
