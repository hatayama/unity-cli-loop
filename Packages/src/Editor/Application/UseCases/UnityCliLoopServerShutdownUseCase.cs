using System.Threading.Tasks;
using System.Threading;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Application
{
    /// <summary>
    /// Coordinates server shutdown while leaving lifecycle result semantics to the domain layer.
    /// </summary>
    public class UnityCliLoopServerShutdownUseCase
    {
        private readonly UnityCliLoopServerStartupService _startupService;

        public UnityCliLoopServerShutdownUseCase(UnityCliLoopServerStartupService startupService)
        {
            _startupService = startupService ?? throw new System.ArgumentNullException(nameof(startupService));
        }

        public Task<ServerShutdownResult> ExecuteAsync(
            IUnityCliLoopServerInstance currentServer,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            if (currentServer == null)
            {
                return Task.FromResult(ServerShutdownResult.AlreadyStopped());
            }

            ct.ThrowIfCancellationRequested();

            ServiceResult<bool> stopResult = _startupService.StopServer(currentServer);
            if (!stopResult.Success)
            {
                return Task.FromResult(ServerShutdownResult.Failed(stopResult.ErrorMessage));
            }

            ct.ThrowIfCancellationRequested();

            ServiceResult<bool> sessionUpdateResult = _startupService.UpdateSessionState(false);
            if (!sessionUpdateResult.Success)
            {
                return Task.FromResult(ServerShutdownResult.Failed(sessionUpdateResult.ErrorMessage));
            }

            return Task.FromResult(ServerShutdownResult.Stopped());
        }
    }
}
