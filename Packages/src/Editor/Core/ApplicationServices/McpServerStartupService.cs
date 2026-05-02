namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Application service responsible for Unity CLI bridge startup operations.
    /// Handles server instance creation, startup, and lifecycle management.
    /// 
    /// Related classes:
    /// - McpBridgeServer: The actual server instance being managed
    /// - McpSessionManager: Manages server session state
    /// - McpServerController: Coordinates overall server management
    /// </summary>
    public class McpServerStartupService
    {
        /// <summary>
        /// Creates and starts a new Unity CLI bridge instance.
        /// </summary>
        /// <returns>The created server instance</returns>
        public ServiceResult<McpBridgeServer> StartServer(
            bool clearServerStartingLockWhenReady = true)
        {
            try
            {
                McpBridgeServer server = new();
                server.StartServer(clearServerStartingLockWhenReady);
                return ServiceResult<McpBridgeServer>.SuccessResult(server);
            }
            catch (System.Exception ex)
            {
                return ServiceResult<McpBridgeServer>.FailureResult($"Failed to start server: {ex.Message}");
            }
        }

        /// <summary>
        /// Stops and disposes of the given server instance.
        /// </summary>
        /// <param name="server">Server instance to stop</param>
        /// <returns>Success indicator</returns>
        public ServiceResult<bool> StopServer(McpBridgeServer server)
        {
            try
            {
                if (server != null)
                {
                    server.Dispose();
                }
                return ServiceResult<bool>.SuccessResult(true);
            }
            catch (System.Exception ex)
            {
                return ServiceResult<bool>.FailureResult($"Failed to stop server: {ex.Message}");
            }
        }

        /// <summary>
        /// Updates session manager with server state.
        /// </summary>
        /// <param name="isRunning">Whether the server is running</param>
        /// <returns>Success indicator</returns>
        public ServiceResult<bool> UpdateSessionState(bool isRunning)
        {
            if (!isRunning)
            {
                McpEditorSettings.ClearServerSession();
                return ServiceResult<bool>.SuccessResult(true);
            }

            McpEditorSettings.SetIsServerRunning(true);
            return ServiceResult<bool>.SuccessResult(true);
        }
    }
}
