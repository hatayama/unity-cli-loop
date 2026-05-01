namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Application service responsible for Unity CLI bridge configuration management.
    /// Handles configuration validation, port resolution, and configuration updating.
    /// 
    /// Related classes:
    /// - McpServerController: Uses this service for server configuration
    /// - McpEditorSettings: Provides configuration storage
    /// - McpPortValidator: Validates port configurations
    /// </summary>
    public class McpServerConfigurationService
    {
        /// <summary>
        /// Validates server configuration settings.
        /// </summary>
        /// <param name="port">Port number to validate</param>
        /// <returns>Validation result with details</returns>
        public ServiceResult<bool> ValidateConfiguration(int port)
        {
            // Validate port range
            if (port < 1 || port > 65535)
            {
                return ServiceResult<bool>.FailureResult($"Port {port} is outside valid range (1-65535)");
            }
            
            // Check for reserved ports
            if (port < 1024)
            {
                return ServiceResult<bool>.FailureResult($"Port {port} is reserved and may require administrator privileges");
            }
            
            // Additional validation could include:
            // - Port availability check
            // - Firewall restrictions
            // - OS-specific limitations
            
            return ServiceResult<bool>.SuccessResult(true);
        }

        /// <summary>
        /// Resolves the actual port to use for server startup.
        /// </summary>
        /// <param name="requestedPort">Requested port number (-1 for default)</param>
        /// <returns>Resolved port number</returns>
        public ServiceResult<int> ResolvePort(int requestedPort)
        {
            try
            {
                int actualPort = requestedPort == -1 ? McpEditorSettings.GetCustomPort() : requestedPort;
                
                // Validate the resolved port
                var validation = ValidateConfiguration(actualPort);
                if (!validation.Success)
                {
                    return ServiceResult<int>.FailureResult($"Resolved port {actualPort} is invalid: {validation.ErrorMessage}");
                }
                
                return ServiceResult<int>.SuccessResult(actualPort);
            }
            catch (System.Exception ex)
            {
                return ServiceResult<int>.FailureResult($"Failed to resolve port: {ex.Message}");
            }
        }
    }
}
