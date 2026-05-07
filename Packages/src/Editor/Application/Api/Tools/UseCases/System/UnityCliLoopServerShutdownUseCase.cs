using System.Threading.Tasks;
using System.Threading;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Application
{
    /// <summary>
    /// Defines read access to Unity CLI Loop Server State state for callers that should not own it.
    /// </summary>
    public interface IUnityCliLoopServerStateReader
    {
        IUnityCliLoopServerInstance CurrentServer { get; }
    }

    /// <summary>
    /// UseCase responsible for temporal cohesion of server shutdown processing
    /// Processing sequence: 1. Server stop, 2. Session state clear, 3. Resource disposal
    /// Related classes: UnityCliLoopServerStartupService, UnityCliLoopEditorSettings
    /// Design reference: @Packages/docs/ARCHITECTURE_Unity.md - UseCase + Tool Pattern (DDD Integration)
    /// </summary>
    public class UnityCliLoopServerShutdownUseCase : AbstractUseCase<ServerShutdownSchema, ServerShutdownResponse>
    {
        private readonly UnityCliLoopServerStartupService _startupService;
        private readonly IUnityCliLoopServerStateReader _serverStateReader;

        public UnityCliLoopServerShutdownUseCase(
            UnityCliLoopServerStartupService startupService,
            IUnityCliLoopServerStateReader serverStateReader)
        {
            _startupService = startupService ?? throw new System.ArgumentNullException(nameof(startupService));
            _serverStateReader = serverStateReader ?? throw new System.ArgumentNullException(nameof(serverStateReader));
        }
        /// <summary>
        /// Execute server shutdown processing
        /// </summary>
        /// <param name="parameters">Shutdown parameters</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Shutdown result</returns>
        public override Task<ServerShutdownResponse> ExecuteAsync(ServerShutdownSchema parameters, CancellationToken cancellationToken)
        {
            ServerShutdownResponse response = new();

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                // 1. Get current server instance
                IUnityCliLoopServerInstance currentServer = _serverStateReader.CurrentServer;
                if (currentServer == null)
                {
                    response.Success = true;
                    response.Message = "Server was not running";
                    return Task.FromResult(response);
                }

                cancellationToken.ThrowIfCancellationRequested();
                
                // 2. Server stop processing - UnityCliLoopServerStartupService
                ServiceResult<bool> stopResult = _startupService.StopServer(currentServer);
                if (!stopResult.Success)
                {
                    response.Success = false;
                    response.Message = stopResult.ErrorMessage;
                    return Task.FromResult(response);
                }

                cancellationToken.ThrowIfCancellationRequested();
                
                // 3. Session state clear
                ServiceResult<bool> sessionUpdateResult = _startupService.UpdateSessionState(false);
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
                    UnityCliLoopEditorSettings.ClearServerSession();
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
