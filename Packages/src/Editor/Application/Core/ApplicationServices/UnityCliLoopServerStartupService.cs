using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Application
{
    /// <summary>
    /// Starts and stops server instances behind the application-owned server handle.
    /// </summary>
    public class UnityCliLoopServerStartupService
    {
        private readonly IUnityCliLoopServerInstanceFactory _serverInstanceFactory;
        private readonly UnityCliLoopEditorSettingsService _editorSettingsService;

        public UnityCliLoopServerStartupService(
            IUnityCliLoopServerInstanceFactory serverInstanceFactory,
            UnityCliLoopEditorSettingsService editorSettingsService)
        {
            System.Diagnostics.Debug.Assert(serverInstanceFactory != null, "serverInstanceFactory must not be null");
            System.Diagnostics.Debug.Assert(editorSettingsService != null, "editorSettingsService must not be null");

            _serverInstanceFactory = serverInstanceFactory ?? throw new System.ArgumentNullException(nameof(serverInstanceFactory));
            _editorSettingsService = editorSettingsService ?? throw new System.ArgumentNullException(nameof(editorSettingsService));
        }

        public ServiceResult<IUnityCliLoopServerInstance> StartServer(
            ServerInitializationRequest request)
        {
            try
            {
                IUnityCliLoopServerInstance server = _serverInstanceFactory.Create();
                server.StartServer(request.ClearStartupLockWhenReady);
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
                _editorSettingsService.ClearServerSession();
                return ServiceResult<bool>.SuccessResult(true);
            }

            _editorSettingsService.SetIsServerRunning(true);
            return ServiceResult<bool>.SuccessResult(true);
        }
    }
}
