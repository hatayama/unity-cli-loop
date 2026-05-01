namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Application service responsible for port allocation and conflict resolution.
    /// Handles port finding, validation, and conflict management.
    /// 
    /// Related classes:
    /// - NetworkUtility: Provides port availability checking
    /// - McpPortValidator: Validates port configurations
    /// </summary>
    public class PortAllocationService
    {
        /// <summary>
        /// Finds an available port starting from the given port number.
        /// </summary>
        /// <param name="startPort">The starting port number to check</param>
        /// <returns>The first available port number</returns>
        public ServiceResult<int> FindAvailablePort(int startPort)
        {
            try
            {
                int availablePort = NetworkUtility.FindAvailablePort(startPort);
                if (availablePort <= 0 || availablePort > 65535)
                {
                    return ServiceResult<int>.FailureResult($"Invalid port number returned: {availablePort}");
                }
                return ServiceResult<int>.SuccessResult(availablePort);
            }
            catch (System.Exception ex)
            {
                return ServiceResult<int>.FailureResult($"Failed to find available port: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles port conflict resolution with user confirmation.
        /// </summary>
        /// <param name="requestedPort">Originally requested port</param>
        /// <param name="availablePort">Available alternative port</param>
        /// <returns>True if user accepted the alternative port</returns>
        public ServiceResult<bool> HandlePortConflict(int requestedPort, int availablePort)
        {
            bool userConfirmed = UnityEditor.EditorUtility.DisplayDialog(
                "uLoopMCP - Port Conflict",
                $"Port {requestedPort} is already in use.\n\nWould you like to use port {availablePort} instead?",
                "OK",
                "Cancel"
            );

            return ServiceResult<bool>.SuccessResult(userConfirmed);
        }
    }
}
