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
        private bool _originalIsServerRunning;
        private UnityCliLoopEditorSettingsService _editorSettingsService;
        private IDomainReloadDetectionService _domainReloadDetectionService;
        private ServerReadinessStateStore _stateStore;

        [SetUp]
        public void SetUp()
        {
            // Save original session state
            _editorSettingsService = UnityCliLoopEditorSettingsTestFactory.CreateService();
            _originalIsServerRunning = _editorSettingsService.GetIsServerRunning();
            _stateStore = CreateTestStateStore();
            _domainReloadDetectionService = new DomainReloadDetectionFileService(_editorSettingsService, _stateStore);
        }

        [TearDown]
        public void TearDown()
        {
            // Restore original session state
            _editorSettingsService.UpdateSettings(s => s with
            {
                isServerRunning = _originalIsServerRunning,
                isAfterCompile = false,
                isDomainReloadInProgress = false,
                isReconnecting = false,
                showReconnectingUI = false,
                showPostCompileReconnectingUI = false
            });

            // Clean up lock file created by ExecuteBeforeDomainReload
            _domainReloadDetectionService.DeleteLockFile();
            _stateStore.Delete();
        }

        [Test]
        public void ExecuteBeforeDomainReload_ShouldUseSessionState_WhenServerInstanceIsNull()
        {
            // Arrange
            _editorSettingsService.SetIsServerRunning(true);

            DomainReloadRecoveryUseCase useCase = CreateUseCase(
                _domainReloadDetectionService,
                _editorSettingsService);

            // Act
            ServiceResult<string> result = useCase.ExecuteBeforeDomainReload(null);

            // Assert
            Assert.IsTrue(result.Success, "ExecuteBeforeDomainReload should succeed");
            Assert.IsTrue(_editorSettingsService.GetIsAfterCompile(), "IsAfterCompile should be set to true");
        }

        [Test]
        public void ExecuteBeforeDomainReload_ShouldNotSaveState_WhenBothInstanceAndSessionAreNotRunning()
        {
            // Arrange
            _editorSettingsService.SetIsServerRunning(false);
            _editorSettingsService.UpdateSettings(s => s with { isAfterCompile = false });

            DomainReloadRecoveryUseCase useCase = CreateUseCase(
                _domainReloadDetectionService,
                _editorSettingsService);

            // Act
            ServiceResult<string> result = useCase.ExecuteBeforeDomainReload(null);

            // Assert
            Assert.IsTrue(result.Success, "ExecuteBeforeDomainReload should succeed");
            Assert.IsFalse(_editorSettingsService.GetIsAfterCompile(), "IsAfterCompile should remain false when server was not running");
        }

        [Test]
        public void ExecuteBeforeDomainReload_ShouldPreferInstanceState_WhenInstanceIsRunning()
        {
            // Arrange
            _editorSettingsService.SetIsServerRunning(true);

            TestServerInstance server = new TestServerInstance();
            server.StartServer();

            DomainReloadRecoveryUseCase useCase = CreateUseCase(
                _domainReloadDetectionService,
                _editorSettingsService);

            // Act
            ServiceResult<string> result = useCase.ExecuteBeforeDomainReload(server);

            // Assert
            Assert.IsTrue(result.Success, "ExecuteBeforeDomainReload should succeed");
            Assert.IsFalse(server.IsRunning, "Running server instance should be stopped before domain reload");
        }

        [Test]
        public void CompleteDomainReload_WhenServerWasNotRunning_ShouldPublishStoppedState()
        {
            // Verifies that a domain reload with no server to recover does not leave CLI waiters in recovering state.
            _editorSettingsService.SetIsServerRunning(false);
            _domainReloadDetectionService.StartDomainReload("test-correlation", serverIsRunning: false);

            _domainReloadDetectionService.CompleteDomainReload("test-correlation");

            ServerReadinessState state = _stateStore.Read();
            Assert.That(state.Phase, Is.EqualTo("stopped"));
            Assert.That(_domainReloadDetectionService.IsLockFilePresent(), Is.False);
        }

        [Test]
        public async Task RestoreServerStateIfNeededAsync_WhenRecoveryDoesNotStartServer_ShouldFail()
        {
            // Verifies that recovery is only reported as successful after a running server instance exists.
            _editorSettingsService.SetIsServerRunning(true);
            _editorSettingsService.UpdateSettings(s => s with { isAfterCompile = false });
            TestRecoveryCoordinator recoveryCoordinator = new();
            SessionRecoveryService service = new(
                recoveryCoordinator,
                _domainReloadDetectionService,
                _editorSettingsService);

            ValidationResult result = await service.RestoreServerStateIfNeededAsync(CancellationToken.None);

            Assert.That(result.IsValid, Is.False);
            Assert.That(
                result.ErrorMessage,
                Is.EqualTo("Unity CLI Loop server recovery finished, but no running server instance is available."));
        }

        private static ServerReadinessStateStore CreateTestStateStore()
        {
            string projectRoot = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "unity-cli-loop-tests",
                System.Guid.NewGuid().ToString("N"));
            return new ServerReadinessStateStore(projectRoot);
        }

        private static DomainReloadRecoveryUseCase CreateUseCase(
            IDomainReloadDetectionService domainReloadDetectionService,
            UnityCliLoopEditorSettingsService editorSettingsService)
        {
            TestRecoveryCoordinator recoveryCoordinator = new();
            SessionRecoveryService sessionRecoveryService =
                new SessionRecoveryService(
                    recoveryCoordinator,
                    domainReloadDetectionService,
                    editorSettingsService);
            return new DomainReloadRecoveryUseCase(
                sessionRecoveryService,
                domainReloadDetectionService,
                editorSettingsService);
        }

        /// <summary>
        /// Test support type used by editor and play mode fixtures.
        /// </summary>
        private sealed class TestRecoveryCoordinator : IUnityCliLoopServerRecoveryCoordinator
        {
            public IUnityCliLoopServerInstance CurrentServer => null;

            public Task StartRecoveryIfNeededAsync(bool isAfterCompile, CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }
        }

        /// <summary>
        /// Test support type used by editor and play mode fixtures.
        /// </summary>
        private sealed class TestServerInstance : IUnityCliLoopServerInstance
        {
            public bool IsRunning { get; private set; }

            public string Endpoint => "test";

            public void StartServer(bool clearServerStartingLockWhenReady = true)
            {
                IsRunning = true;
            }

            public void StopServer()
            {
                IsRunning = false;
            }

            public void Dispose()
            {
                IsRunning = false;
            }
        }
    }
}
