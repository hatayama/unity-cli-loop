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

        [SetUp]
        public void SetUp()
        {
            // Save original session state
            _originalIsServerRunning = UnityCliLoopEditorSettings.GetIsServerRunning();
        }

        [TearDown]
        public void TearDown()
        {
            // Restore original session state
            UnityCliLoopEditorSettings.SetIsServerRunning(_originalIsServerRunning);
            UnityCliLoopEditorSettings.SetIsAfterCompile(false);
            UnityCliLoopEditorSettings.SetIsDomainReloadInProgress(false);
            UnityCliLoopEditorSettings.SetIsReconnecting(false);
            UnityCliLoopEditorSettings.SetShowReconnectingUI(false);
            UnityCliLoopEditorSettings.SetShowPostCompileReconnectingUI(false);

            // Clean up lock file created by ExecuteBeforeDomainReload
            DomainReloadDetectionService.DeleteLockFile();
        }

        [Test]
        public void ExecuteBeforeDomainReload_ShouldUseSessionState_WhenServerInstanceIsNull()
        {
            // Arrange
            UnityCliLoopEditorSettings.SetIsServerRunning(true);

            DomainReloadRecoveryUseCase useCase = CreateUseCase();

            // Act
            ServiceResult<string> result = useCase.ExecuteBeforeDomainReload(null);

            // Assert
            Assert.IsTrue(result.Success, "ExecuteBeforeDomainReload should succeed");
            Assert.IsTrue(UnityCliLoopEditorSettings.GetIsAfterCompile(), "IsAfterCompile should be set to true");
        }

        [Test]
        public void ExecuteBeforeDomainReload_ShouldNotSaveState_WhenBothInstanceAndSessionAreNotRunning()
        {
            // Arrange
            UnityCliLoopEditorSettings.SetIsServerRunning(false);
            UnityCliLoopEditorSettings.SetIsAfterCompile(false);

            DomainReloadRecoveryUseCase useCase = CreateUseCase();

            // Act
            ServiceResult<string> result = useCase.ExecuteBeforeDomainReload(null);

            // Assert
            Assert.IsTrue(result.Success, "ExecuteBeforeDomainReload should succeed");
            Assert.IsFalse(UnityCliLoopEditorSettings.GetIsAfterCompile(), "IsAfterCompile should remain false when server was not running");
        }

        [Test]
        public void ExecuteBeforeDomainReload_ShouldPreferInstanceState_WhenInstanceIsRunning()
        {
            // Arrange
            UnityCliLoopEditorSettings.SetIsServerRunning(true);

            // Create a running server instance
            UnityCliLoopBridgeServer server = null;
            try
            {
                server = new UnityCliLoopBridgeServer();
                server.StartServer();

                DomainReloadRecoveryUseCase useCase = CreateUseCase();

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

        private static DomainReloadRecoveryUseCase CreateUseCase()
        {
            TestRecoveryCoordinator recoveryCoordinator = new();
            SessionRecoveryService sessionRecoveryService =
                new SessionRecoveryService(recoveryCoordinator);
            return new DomainReloadRecoveryUseCase(sessionRecoveryService);
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
