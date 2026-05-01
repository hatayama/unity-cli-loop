using NUnit.Framework;

namespace io.github.hatayama.uLoopMCP
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
            _originalIsServerRunning = McpEditorSettings.GetIsServerRunning();
        }

        [TearDown]
        public void TearDown()
        {
            // Restore original session state
            McpEditorSettings.SetIsServerRunning(_originalIsServerRunning);
            McpEditorSettings.SetIsAfterCompile(false);
            McpEditorSettings.SetIsDomainReloadInProgress(false);
            McpEditorSettings.SetIsReconnecting(false);
            McpEditorSettings.SetShowReconnectingUI(false);
            McpEditorSettings.SetShowPostCompileReconnectingUI(false);

            // Clean up lock file created by ExecuteBeforeDomainReload
            DomainReloadDetectionService.DeleteLockFile();
        }

        [Test]
        public void ExecuteBeforeDomainReload_ShouldUseSessionState_WhenServerInstanceIsNull()
        {
            // Arrange
            McpEditorSettings.SetIsServerRunning(true);

            DomainReloadRecoveryUseCase useCase = new();

            // Act
            ServiceResult<string> result = useCase.ExecuteBeforeDomainReload(null);

            // Assert
            Assert.IsTrue(result.Success, "ExecuteBeforeDomainReload should succeed");
            Assert.IsTrue(McpEditorSettings.GetIsAfterCompile(), "IsAfterCompile should be set to true");
        }

        [Test]
        public void ExecuteBeforeDomainReload_ShouldNotSaveState_WhenBothInstanceAndSessionAreNotRunning()
        {
            // Arrange
            McpEditorSettings.SetIsServerRunning(false);
            McpEditorSettings.SetIsAfterCompile(false);

            DomainReloadRecoveryUseCase useCase = new();

            // Act
            ServiceResult<string> result = useCase.ExecuteBeforeDomainReload(null);

            // Assert
            Assert.IsTrue(result.Success, "ExecuteBeforeDomainReload should succeed");
            Assert.IsFalse(McpEditorSettings.GetIsAfterCompile(), "IsAfterCompile should remain false when server was not running");
        }

        [Test]
        public void ExecuteBeforeDomainReload_ShouldPreferInstanceState_WhenInstanceIsRunning()
        {
            // Arrange
            McpEditorSettings.SetIsServerRunning(true);

            // Create a running server instance
            McpBridgeServer server = null;
            try
            {
                server = new McpBridgeServer();
                server.StartServer();

                DomainReloadRecoveryUseCase useCase = new();

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
    }
}
