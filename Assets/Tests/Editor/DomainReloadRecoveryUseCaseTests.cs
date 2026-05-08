using NUnit.Framework;
using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;

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

        [SetUp]
        public void SetUp()
        {
            // Save original session state
            _editorSettingsService = UnityCliLoopEditorSettingsTestFactory.CreateService();
            _originalIsServerRunning = _editorSettingsService.GetIsServerRunning();
            _domainReloadDetectionService = new DomainReloadDetectionFileService(_editorSettingsService);
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

            // Create a running server instance
            UnityCliLoopBridgeServer server = null;
            try
            {
                server = new UnityCliLoopBridgeServer(
                    _domainReloadDetectionService,
                    _editorSettingsService);
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
            finally
            {
                server?.Dispose();
            }
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
    }
}
