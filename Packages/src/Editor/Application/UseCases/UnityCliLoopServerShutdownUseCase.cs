using System.Threading.Tasks;
using System.Threading;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

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
    /// Coordinates server shutdown while leaving lifecycle result semantics to the domain layer.
    /// </summary>
    public class UnityCliLoopServerShutdownUseCase
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

        public Task<ServerShutdownResult> ExecuteAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            IUnityCliLoopServerInstance currentServer = _serverStateReader.CurrentServer;
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
