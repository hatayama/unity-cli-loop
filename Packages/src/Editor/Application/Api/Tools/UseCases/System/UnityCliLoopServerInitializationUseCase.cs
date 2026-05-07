using System.Threading.Tasks;
using System.Threading;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Application
{
    /// <summary>
    /// UseCase responsible for temporal cohesion of server initialization processing.
    /// Processing sequence: 1. Editor state validation, 2. Server startup, 3. State update.
    /// Related classes: UnityCliLoopServerStartupService, ISecurityValidationService.
    /// Design reference: @Packages/docs/ARCHITECTURE_Unity.md - UseCase + Tool Pattern (DDD Integration)
    /// </summary>
    public class UnityCliLoopServerInitializationUseCase : AbstractUseCase<ServerInitializationSchema, ServerInitializationResponse>
    {
        private readonly ISecurityValidationService _securityService;
        private readonly UnityCliLoopServerStartupService _startupService;

        public UnityCliLoopServerInitializationUseCase(
            ISecurityValidationService securityService,
            UnityCliLoopServerStartupService startupService)
        {
            _securityService = securityService ?? throw new System.ArgumentNullException(nameof(securityService));
            _startupService = startupService ?? throw new System.ArgumentNullException(nameof(startupService));
        }
        /// <summary>
        /// Execute server initialization processing
        /// </summary>
        /// <param name="parameters">Initialization parameters</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Initialization result</returns>
        public override Task<ServerInitializationResponse> ExecuteAsync(ServerInitializationSchema parameters, CancellationToken cancellationToken)
        {
            ServerInitializationResponse response = new();

            try
            {
                ValidationResult editorStateValidation = _securityService.ValidateEditorState();
                if (!editorStateValidation.IsValid)
                {
                    response.Success = false;
                    response.Message = editorStateValidation.ErrorMessage;
                    return Task.FromResult(response);
                }

                // 4. Server startup - UnityCliLoopServerStartupService
                ServiceResult<IUnityCliLoopServerInstance> serverResult = _startupService.StartServer(
                    !parameters.PreserveStartupLockUntilExplicitRelease);
                if (!serverResult.Success)
                {
                    response.Success = false;
                    response.Message = serverResult.ErrorMessage;
                    return Task.FromResult(response);
                }
                IUnityCliLoopServerInstance serverInstance = serverResult.Data;

                // 5. Session state update
                ServiceResult<bool> sessionUpdateResult =
                    _startupService.UpdateSessionState(true);
                if (!sessionUpdateResult.Success)
                {
                    response.Success = false;
                    response.Message = sessionUpdateResult.ErrorMessage;
                    return Task.FromResult(response);
                }

                // Success response
                response.Success = true;
                response.IsRunning = true;
                response.ServerInstance = serverInstance;
                response.Message = "Server initialization completed successfully";

                return Task.FromResult(response);
            }
            catch (System.Exception ex)
            {
                // Log the full exception for debugging
                UnityEngine.Debug.LogError($"Server initialization failed: {ex}");
                
                response.Success = false;
                response.Message = "Server initialization failed. Please check the logs for details.";
                return Task.FromResult(response);
            }
        }
    }
}
