using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Application
{
    /// <summary>
    /// Starts and stops server instances behind the application-owned server handle.
    /// </summary>
    public class UnityCliLoopServerStartupService
    {
        private readonly IUnityCliLoopServerInstanceFactory _serverInstanceFactory;
        private readonly ISessionFlagsRepository _sessionFlagsRepository;

        public UnityCliLoopServerStartupService(
            IUnityCliLoopServerInstanceFactory serverInstanceFactory,
            ISessionFlagsRepository sessionFlagsRepository)
        {
            System.Diagnostics.Debug.Assert(serverInstanceFactory != null, "serverInstanceFactory must not be null");
            System.Diagnostics.Debug.Assert(sessionFlagsRepository != null, "sessionFlagsRepository must not be null");

            _serverInstanceFactory = serverInstanceFactory ?? throw new System.ArgumentNullException(nameof(serverInstanceFactory));
            _sessionFlagsRepository = sessionFlagsRepository ?? throw new System.ArgumentNullException(nameof(sessionFlagsRepository));
        }

        public ServiceResult<IUnityCliLoopServerInstance> StartServer()
        {
            try
            {
                IUnityCliLoopServerInstance server = _serverInstanceFactory.Create();
                server.StartServer();
                return ServiceResult<IUnityCliLoopServerInstance>.SuccessResult(server);
            }
            catch (System.Exception ex)
            {
                return ServiceResult<IUnityCliLoopServerInstance>.FailureResult($"Failed to start server: {ex.Message}");
            }
        }

        public ServiceResult<bool> StopServer(IUnityCliLoopServerInstance server)
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
                _sessionFlagsRepository.ClearServerSession();
                return ServiceResult<bool>.SuccessResult(true);
            }

            _sessionFlagsRepository.MarkServerStarted();
            return ServiceResult<bool>.SuccessResult(true);
        }
    }
}
