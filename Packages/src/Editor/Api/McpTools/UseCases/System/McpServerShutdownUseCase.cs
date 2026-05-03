using System.Threading.Tasks;
using System.Threading;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// UseCase responsible for temporal cohesion of server shutdown processing
    /// Processing sequence: 1. Server stop, 2. Session state clear, 3. Resource disposal
    /// Related classes: McpServerStartupService, McpSessionManager
    /// Design reference: @Packages/docs/ARCHITECTURE_Unity.md - UseCase + Tool Pattern (DDD Integration)
    /// </summary>
    public class McpServerShutdownUseCase : AbstractUseCase<ServerShutdownSchema, ServerShutdownResponse>
    {
        private readonly McpServerStartupService _startupService;

        public McpServerShutdownUseCase(McpServerStartupService startupService)
        {
            _startupService = startupService ?? throw new System.ArgumentNullException(nameof(startupService));
        }
        /// <summary>
        /// Execute server shutdown processing
        /// </summary>
        /// <param name="parameters">Shutdown parameters</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Shutdown result</returns>
        public override Task<ServerShutdownResponse> ExecuteAsync(ServerShutdownSchema parameters, CancellationToken cancellationToken)
        {
            ServerShutdownResponse response = new ServerShutdownResponse();

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                // 1. Get current server instance
                McpBridgeServer currentServer = McpServerController.CurrentServer;
                if (currentServer == null)
                {
                    response.Success = true;
                    response.Message = "Server was not running";
                    return Task.FromResult(response);
                }

                cancellationToken.ThrowIfCancellationRequested();
                
                // 2. Server stop processing - McpServerStartupService
                var stopResult = _startupService.StopServer(currentServer);
                if (!stopResult.Success)
                {
                    response.Success = false;
                    response.Message = stopResult.ErrorMessage;
                    return Task.FromResult(response);
                }

                cancellationToken.ThrowIfCancellationRequested();
                
                // 3. Session state clear
                var sessionUpdateResult = _startupService.UpdateSessionState(false);
                if (!sessionUpdateResult.Success)
                {
                    response.Success = false;
                    response.Message = sessionUpdateResult.ErrorMessage;
                    return Task.FromResult(response);  
                }

                cancellationToken.ThrowIfCancellationRequested();
                
                // 4. Session clear with SessionManager
                try
                {
                    McpEditorSettings.ClearServerSession();
                }
                catch (System.Exception sessionEx)
                {
                    response.Success = false;
                    response.Message = $"Failed to clear session: {sessionEx.Message}";
                    return Task.FromResult(response);
                }

                // Success response
                response.Success = true;
                response.Message = "Server shutdown completed successfully";
            }
            catch (System.OperationCanceledException)
            {
                // Propagate cancellation exceptions
                throw;
            }
            catch (System.Exception ex)
            {
                // Log the full exception for debugging
                UnityEngine.Debug.LogError($"Server shutdown failed: {ex}");
                
                response.Success = false;
                response.Message = "Server shutdown failed. Please check the logs for details.";
            }

            return Task.FromResult(response);
        }
    }
}