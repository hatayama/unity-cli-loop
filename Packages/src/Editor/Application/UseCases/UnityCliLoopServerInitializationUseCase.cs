using System.Threading.Tasks;
using System.Threading;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Application
{
    /// <summary>
    /// Coordinates server initialization without owning the server lifecycle contract.
    /// </summary>
    public class UnityCliLoopServerInitializationUseCase
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

        public Task<ServerInitializationResult<IUnityCliLoopServerInstance>> ExecuteAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            ValidationResult editorStateValidation = _securityService.ValidateEditorState();
            if (!editorStateValidation.IsValid)
            {
                ServerInitializationResult<IUnityCliLoopServerInstance> validationFailure =
                    ServerInitializationResult<IUnityCliLoopServerInstance>.Failed(editorStateValidation.ErrorMessage);
                return Task.FromResult(validationFailure);
            }

            ct.ThrowIfCancellationRequested();

            ServiceResult<IUnityCliLoopServerInstance> serverResult =
                _startupService.StartServer();
            if (!serverResult.Success)
            {
                ServerInitializationResult<IUnityCliLoopServerInstance> startupFailure =
                    ServerInitializationResult<IUnityCliLoopServerInstance>.Failed(serverResult.ErrorMessage);
                return Task.FromResult(startupFailure);
            }
            IUnityCliLoopServerInstance serverInstance = serverResult.Data;

            ct.ThrowIfCancellationRequested();

            ServiceResult<bool> sessionUpdateResult =
                _startupService.UpdateSessionState(true);
            if (!sessionUpdateResult.Success)
            {
                ServerInitializationResult<IUnityCliLoopServerInstance> sessionFailure =
                    ServerInitializationResult<IUnityCliLoopServerInstance>.Failed(sessionUpdateResult.ErrorMessage);
                return Task.FromResult(sessionFailure);
            }

            ServerInitializationResult<IUnityCliLoopServerInstance> response =
                ServerInitializationResult<IUnityCliLoopServerInstance>.Running(serverInstance);
            return Task.FromResult(response);
        }
    }
}
