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
        private UnityCliLoopEditorSessionStateService _sessionStateService;
        private UnityCliLoopEditorSessionStateSnapshot _originalSessionState;
        private IDomainReloadDetectionService _domainReloadDetectionService;

        [SetUp]
        public void SetUp()
        {
            _sessionFlagsRepository = UnityCliLoopEditorSessionStateTestFactory.CreateSessionFlagsRepository();
            _sessionStateService = UnityCliLoopEditorSessionStateTestFactory.CreateService();
            _originalSessionState = UnityCliLoopEditorSessionStateTestFactory.CaptureSnapshot(_sessionStateService);
            UnityCliLoopEditorSessionStateTestFactory.ClearAll();
            _domainReloadDetectionService = new DomainReloadDetectionFileService(
                _sessionFlagsRepository,
                new UnityCliLoopPendingCompileSessionRepository(),
                _sessionStateService);
        }

        [TearDown]
        public void TearDown()
        {
            _originalSessionState.Restore(_sessionStateService);
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
            _sessionFlagsRepository.SetIsServerRunning(true);
            _sessionFlagsRepository.SetIsAfterCompile(false);
            TestRecoveryCoordinator recoveryCoordinator = new();
            SessionRecoveryService service = new(
                recoveryCoordinator,
                _domainReloadDetectionService,
                _sessionFlagsRepository);

            ValidationResult result = await service.RestoreServerStateIfNeededAsync(CancellationToken.None);

            Assert.That(result.IsValid, Is.False);
            Assert.That(
                result.ErrorMessage,
                Is.EqualTo("Unity CLI Loop server recovery finished, but no running server instance is available."));
        }

        [Test]
        public async Task RestoreServerStateIfNeededAsync_WhenNoServerWasRunning_ShouldStillStartRecovery()
        {
            // Verifies launch-time reload recovery starts the server even when no previous bridge session existed.
            _sessionFlagsRepository.SetIsServerRunning(false);
            _sessionFlagsRepository.SetIsAfterCompile(false);
            TestRecoveryCoordinator recoveryCoordinator = new(recoverServer: true);
            SessionRecoveryService service = new(
                recoveryCoordinator,
                _domainReloadDetectionService,
                _sessionFlagsRepository);

            ValidationResult result = await service.RestoreServerStateIfNeededAsync(CancellationToken.None);

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
                recoveryCoordinator,
                _domainReloadDetectionService,
                _sessionFlagsRepository);

            ValidationResult result = await service.RestoreServerStateIfNeededAsync(CancellationToken.None);

            Assert.That(result.IsValid, Is.True);
            Assert.That(recoveryCoordinator.StartRecoveryCallCount, Is.EqualTo(0));
        }

        private static DomainReloadRecoveryUseCase CreateUseCase(
            IDomainReloadDetectionService domainReloadDetectionService,
            ISessionFlagsRepository sessionFlagsRepository)
        {
            TestRecoveryCoordinator recoveryCoordinator = new();
            SessionRecoveryService sessionRecoveryService =
                new SessionRecoveryService(
                    recoveryCoordinator,
                    domainReloadDetectionService,
                    sessionFlagsRepository);
            return new DomainReloadRecoveryUseCase(
                sessionRecoveryService,
                domainReloadDetectionService,
                sessionFlagsRepository);
        }

        /// <summary>
        /// Test support type used by editor and play mode fixtures.
        /// </summary>
        private sealed class TestRecoveryCoordinator : IUnityCliLoopServerRecoveryCoordinator
        {
            private readonly bool _recoverServer;
            private readonly TestServerInstance _server = new();

            public TestRecoveryCoordinator(bool recoverServer = false)
            {
                _recoverServer = recoverServer;
            }

            public int StartRecoveryCallCount { get; private set; }

            public IUnityCliLoopServerInstance CurrentServer => _server.IsRunning ? _server : null;

            public Task StartRecoveryIfNeededAsync(bool isAfterCompile, CancellationToken cancellationToken)
            {
                StartRecoveryCallCount++;
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
