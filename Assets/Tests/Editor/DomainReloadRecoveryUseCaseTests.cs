using NUnit.Framework;
using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests for DomainReloadRecoveryUseCase session state fallback functionality.
    /// Validates that domain reload recovery works correctly even when server instance is null.
    /// </summary>
    public class DomainReloadRecoveryUseCaseTests
    {
        private UnityCliLoopSessionFlagsRepository _sessionFlagsRepository;
        private UnityCliLoopCompileSessionLifecycleService _sessionStateService;
        private UnityCliLoopEditorSessionStateSnapshot _originalSessionState;
        private IDomainReloadDetectionService _domainReloadDetectionService;

        [SetUp]
        public void SetUp()
        {
            _sessionFlagsRepository = UnityCliLoopEditorSessionStateTestFactory.CreateSessionFlagsRepository();
            _sessionStateService = UnityCliLoopEditorSessionStateTestFactory.CreateCompileSessionLifecycleService();
            _originalSessionState = UnityCliLoopEditorSessionStateTestFactory.CaptureSnapshot();
            UnityCliLoopEditorSessionStateTestFactory.ClearAll();
            _domainReloadDetectionService = new DomainReloadDetectionFileService(
                _sessionFlagsRepository,
                new UnityCliLoopPendingCompileSessionRepository(),
                _sessionStateService);
        }

        [TearDown]
        public void TearDown()
        {
            _originalSessionState.Restore();
        }

        [Test]
        public void ExecuteBeforeDomainReload_ShouldUseSessionState_WhenServerInstanceIsNull()
        {
            // Arrange
            _sessionFlagsRepository.SetIsServerRunning(true);

            DomainReloadRecoveryUseCase useCase = CreateUseCase(
                _domainReloadDetectionService,
                _sessionFlagsRepository);

            // Act
            ServiceResult<string> result = useCase.ExecuteBeforeDomainReload(null);

            // Assert
            Assert.IsTrue(result.Success, "ExecuteBeforeDomainReload should succeed");
            Assert.IsTrue(_sessionFlagsRepository.GetIsAfterCompile(), "IsAfterCompile should be set to true");
        }

        [Test]
        public void ExecuteBeforeDomainReload_ShouldNotSaveState_WhenBothInstanceAndSessionAreNotRunning()
        {
            // Arrange
            _sessionFlagsRepository.SetIsServerRunning(false);
            _sessionFlagsRepository.SetIsAfterCompile(false);

            DomainReloadRecoveryUseCase useCase = CreateUseCase(
                _domainReloadDetectionService,
                _sessionFlagsRepository);

            // Act
            ServiceResult<string> result = useCase.ExecuteBeforeDomainReload(null);

            // Assert
            Assert.IsTrue(result.Success, "ExecuteBeforeDomainReload should succeed");
            Assert.IsFalse(_sessionFlagsRepository.GetIsAfterCompile(), "IsAfterCompile should remain false when server was not running");
        }

        [Test]
        public void ExecuteBeforeDomainReload_ShouldPreferInstanceState_WhenInstanceIsRunning()
        {
            // Arrange
            _sessionFlagsRepository.SetIsServerRunning(true);

            TestServerInstance server = new TestServerInstance();
            server.StartServer();

            DomainReloadRecoveryUseCase useCase = CreateUseCase(
                _domainReloadDetectionService,
                _sessionFlagsRepository);

            // Act
            ServiceResult<string> result = useCase.ExecuteBeforeDomainReload(server);

            // Assert
            Assert.IsTrue(result.Success, "ExecuteBeforeDomainReload should succeed");
            Assert.IsFalse(server.IsRunning, "Running server instance should be stopped before domain reload");
            Assert.That(server.StopCallCount, Is.EqualTo(1));
            Assert.That(server.DisposeCallCount, Is.EqualTo(0));
        }

        [Test]
        public async Task RestoreServerStateIfNeededAsync_WhenRecoveryDoesNotStartServer_ShouldFail()
        {
            // Verifies that recovery is only reported as successful after a running server instance exists.
            InMemorySessionFlagsRepository sessionFlagsRepository = new();
            TestRecoveryCoordinator recoveryCoordinator = new();
            SessionRecoveryService service = CreatePureSessionRecoveryService(sessionFlagsRepository);

            ValidationResult result = await service.RestoreServerStateIfNeededAsync(
                recoveryCoordinator,
                CancellationToken.None);

            Assert.That(result.IsValid, Is.False);
            Assert.That(
                result.ErrorMessage,
                Is.EqualTo("Unity CLI Loop server recovery finished, but no running server instance is available."));
        }

        [Test]
        public async Task RestoreServerStateIfNeededAsync_WhenNoServerWasRunning_ShouldStillStartRecovery()
        {
            // Verifies launch-time reload recovery starts the server even when no previous bridge session existed.
            InMemorySessionFlagsRepository sessionFlagsRepository = new();
            TestRecoveryCoordinator recoveryCoordinator = new(recoverServer: true);
            SessionRecoveryService service = CreatePureSessionRecoveryService(sessionFlagsRepository);

            ValidationResult result = await service.RestoreServerStateIfNeededAsync(
                recoveryCoordinator,
                CancellationToken.None);

            Assert.That(result.IsValid, Is.True);
            Assert.That(recoveryCoordinator.StartRecoveryCallCount, Is.EqualTo(1));
        }

        [Test]
        public async Task RestoreServerStateIfNeededAsync_WhenServerWasManuallyStopped_ShouldSkipRecovery()
        {
            // Verifies explicit Stop Server is preserved across Domain Reload.
            _sessionFlagsRepository.MarkServerManuallyStopped();
            TestRecoveryCoordinator recoveryCoordinator = new(recoverServer: true);
            SessionRecoveryService service = new(
                _domainReloadDetectionService,
                _sessionFlagsRepository);

            ValidationResult result = await service.RestoreServerStateIfNeededAsync(
                recoveryCoordinator,
                CancellationToken.None);

            Assert.That(result.IsValid, Is.True);
            Assert.That(recoveryCoordinator.StartRecoveryCallCount, Is.EqualTo(0));
        }

        [Test]
        public async Task RestoreServerStateIfNeededAsync_WhenCurrentServerIsRunningAndAfterCompile_ShouldClearAfterCompileWithoutRecovery()
        {
            // Verifies the running-server branch consumes AfterCompile without starting recovery.
            InMemorySessionFlagsRepository sessionFlagsRepository = new();
            sessionFlagsRepository.SetIsAfterCompile(true);
            TestRecoveryCoordinator recoveryCoordinator = new(currentServerRunning: true);
            SessionRecoveryService service = CreatePureSessionRecoveryService(sessionFlagsRepository);

            ValidationResult result = await service.RestoreServerStateIfNeededAsync(
                recoveryCoordinator,
                CancellationToken.None);

            Assert.That(result.IsValid, Is.True);
            Assert.That(sessionFlagsRepository.GetIsAfterCompile(), Is.False);
            Assert.That(recoveryCoordinator.StartRecoveryCallCount, Is.EqualTo(0));
        }

        [Test]
        public async Task RestoreServerStateIfNeededAsync_WhenAfterCompileAndServerMissing_ShouldClearFlagAndPassAfterCompileToRecovery()
        {
            // Verifies AfterCompile is cleared from storage while its original value is passed to recovery.
            InMemorySessionFlagsRepository sessionFlagsRepository = new();
            sessionFlagsRepository.SetIsAfterCompile(true);
            TestRecoveryCoordinator recoveryCoordinator = new(recoverServer: true);
            SessionRecoveryService service = CreatePureSessionRecoveryService(sessionFlagsRepository);

            ValidationResult result = await service.RestoreServerStateIfNeededAsync(
                recoveryCoordinator,
                CancellationToken.None);

            Assert.That(result.IsValid, Is.True);
            Assert.That(sessionFlagsRepository.GetIsAfterCompile(), Is.False);
            Assert.That(recoveryCoordinator.StartRecoveryCallCount, Is.EqualTo(1));
            Assert.That(recoveryCoordinator.LastIsAfterCompile, Is.True);
        }

        [Test]
        public async Task RestoreServerStateIfNeededAsync_WhenManuallyStoppedAndAfterCompile_ShouldClearFlagAndSkipRecovery()
        {
            // Verifies explicit manual stop wins after the AfterCompile flag is consumed.
            InMemorySessionFlagsRepository sessionFlagsRepository = new();
            sessionFlagsRepository.SetIsAfterCompile(true);
            sessionFlagsRepository.MarkServerManuallyStopped();
            TestRecoveryCoordinator recoveryCoordinator = new(recoverServer: true);
            SessionRecoveryService service = CreatePureSessionRecoveryService(sessionFlagsRepository);

            ValidationResult result = await service.RestoreServerStateIfNeededAsync(
                recoveryCoordinator,
                CancellationToken.None);

            Assert.That(result.IsValid, Is.True);
            Assert.That(sessionFlagsRepository.GetIsAfterCompile(), Is.False);
            Assert.That(recoveryCoordinator.StartRecoveryCallCount, Is.EqualTo(0));
        }

        private static DomainReloadRecoveryUseCase CreateUseCase(
            IDomainReloadDetectionService domainReloadDetectionService,
            ISessionFlagsRepository sessionFlagsRepository)
        {
            SessionRecoveryService sessionRecoveryService =
                new SessionRecoveryService(
                    domainReloadDetectionService,
                    sessionFlagsRepository);
            return new DomainReloadRecoveryUseCase(
                sessionRecoveryService,
                domainReloadDetectionService,
                sessionFlagsRepository);
        }

        private static SessionRecoveryService CreatePureSessionRecoveryService(
            ISessionFlagsRepository sessionFlagsRepository)
        {
            return new SessionRecoveryService(
                new NoOpDomainReloadDetectionService(),
                sessionFlagsRepository);
        }

        /// <summary>
        /// Test support type used by pure SessionRecoveryService tests.
        /// </summary>
        private sealed class NoOpDomainReloadDetectionService : IDomainReloadDetectionService
        {
            public void RegisterForEditorStartup()
            {
            }

            public void StartDomainReload(string correlationId, bool serverIsRunning)
            {
            }

            public void CompleteDomainReload(string correlationId)
            {
            }

            public void RollbackDomainReloadStart(string correlationId)
            {
            }

            public bool ShouldShowReconnectingUI()
            {
                return false;
            }
        }

        /// <summary>
        /// Test support type used by pure SessionRecoveryService tests.
        /// </summary>
        private sealed class InMemorySessionFlagsRepository : ISessionFlagsRepository
        {
            private bool _isServerRunning;
            private bool _isServerManuallyStopped;
            private bool _isAfterCompile;
            private bool _isDomainReloadInProgress;
            private bool _isReconnecting;
            private bool _showReconnectingUI;
            private bool _showPostCompileReconnectingUI;
            private bool _shouldAutoScanThirdPartyToolMigration;

            public bool GetIsServerRunning()
            {
                return _isServerRunning;
            }

            public bool GetIsServerManuallyStopped()
            {
                return _isServerManuallyStopped;
            }

            public bool GetIsAfterCompile()
            {
                return _isAfterCompile;
            }

            public bool GetIsDomainReloadInProgress()
            {
                return _isDomainReloadInProgress;
            }

            public bool GetShowReconnectingUI()
            {
                return _showReconnectingUI;
            }

            public void SetIsAfterCompile(bool isAfterCompile)
            {
                _isAfterCompile = isAfterCompile;
            }

            public void SetIsDomainReloadInProgress(bool isDomainReloadInProgress)
            {
                _isDomainReloadInProgress = isDomainReloadInProgress;
            }

            public void SetIsReconnecting(bool isReconnecting)
            {
                _isReconnecting = isReconnecting;
            }

            public void SetShowReconnectingUI(bool showReconnectingUI)
            {
                _showReconnectingUI = showReconnectingUI;
            }

            public void SetShowPostCompileReconnectingUI(bool showPostCompileReconnectingUI)
            {
                _showPostCompileReconnectingUI = showPostCompileReconnectingUI;
            }

            public void SetShouldAutoScanThirdPartyToolMigration(bool shouldAutoScanThirdPartyToolMigration)
            {
                _shouldAutoScanThirdPartyToolMigration = shouldAutoScanThirdPartyToolMigration;
            }

            public bool ConsumeShouldAutoScanThirdPartyToolMigration()
            {
                if (!_shouldAutoScanThirdPartyToolMigration)
                {
                    return false;
                }

                _shouldAutoScanThirdPartyToolMigration = false;
                return true;
            }

            public void MarkServerStarted()
            {
                _isServerRunning = true;
                _isServerManuallyStopped = false;
            }

            public void MarkServerManuallyStopped()
            {
                ClearServerSession();
                _isServerManuallyStopped = true;
            }

            public void ClearServerSession()
            {
                _isServerRunning = false;
            }

            public void ClearAfterCompileFlag()
            {
                _isAfterCompile = false;
            }

            public void ClearReconnectingFlags()
            {
                _isReconnecting = false;
                _showReconnectingUI = false;
            }

            public void ClearPostCompileReconnectingUI()
            {
                _showPostCompileReconnectingUI = false;
            }

            public void ClearDomainReloadFlag()
            {
                _isDomainReloadInProgress = false;
            }

            public void ClearDomainReloadRecoveryFlags()
            {
                _isDomainReloadInProgress = false;
                _isAfterCompile = false;
                _isReconnecting = false;
                _showReconnectingUI = false;
                _showPostCompileReconnectingUI = false;
            }
        }

        /// <summary>
        /// Test support type used by editor and play mode fixtures.
        /// </summary>
        private sealed class TestRecoveryCoordinator : IUnityCliLoopServerRecoveryCoordinator
        {
            private readonly bool _recoverServer;
            private readonly TestServerInstance _server = new();

            public TestRecoveryCoordinator(
                bool recoverServer = false,
                bool currentServerRunning = false)
            {
                _recoverServer = recoverServer;
                if (currentServerRunning)
                {
                    _server.StartServer();
                }
            }

            public int StartRecoveryCallCount { get; private set; }

            public bool? LastIsAfterCompile { get; private set; }

            public IUnityCliLoopServerInstance CurrentServer => _server.IsRunning ? _server : null;

            public Task StartRecoveryIfNeededAsync(bool isAfterCompile, CancellationToken cancellationToken)
            {
                StartRecoveryCallCount++;
                LastIsAfterCompile = isAfterCompile;
                if (_recoverServer)
                {
                    _server.StartServer();
                }

                return Task.CompletedTask;
            }
        }

        /// <summary>
        /// Test support type used by editor and play mode fixtures.
        /// </summary>
        private sealed class TestServerInstance : IUnityCliLoopServerInstance
        {
            public bool IsRunning { get; private set; }

            public int StopCallCount { get; private set; }

            public int DisposeCallCount { get; private set; }

            public string Endpoint => "test";

            public void StartServer()
            {
                IsRunning = true;
            }

            public void StopServer()
            {
                StopCallCount++;
                IsRunning = false;
            }

            public void Dispose()
            {
                DisposeCallCount++;
                IsRunning = false;
            }
        }
    }
}
