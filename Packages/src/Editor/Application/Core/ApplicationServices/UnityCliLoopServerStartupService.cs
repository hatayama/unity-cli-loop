using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Application
{
    /// <summary>
    /// Starts and stops server instances behind the application-owned server handle.
    /// </summary>
    public class UnityCliLoopServerStartupService
    {
        private readonly IUnityCliLoopServerInstanceFactory _serverInstanceFactory;

        public UnityCliLoopServerStartupService(IUnityCliLoopServerInstanceFactory serverInstanceFactory)
        {
            System.Diagnostics.Debug.Assert(serverInstanceFactory != null, "serverInstanceFactory must not be null");

            _serverInstanceFactory = serverInstanceFactory ?? throw new System.ArgumentNullException(nameof(serverInstanceFactory));
        }

        public ServiceResult<IUnityCliLoopServerInstance> StartServer(
            bool clearServerStartingLockWhenReady = true)
        {
            try
            {
                IUnityCliLoopServerInstance server = _serverInstanceFactory.Create();
                server.StartServer(clearServerStartingLockWhenReady);
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
                UnityCliLoopEditorSettings.ClearServerSession();
                return ServiceResult<bool>.SuccessResult(true);
            }

            UnityCliLoopEditorSettings.SetIsServerRunning(true);
            return ServiceResult<bool>.SuccessResult(true);
        }
    }
}
