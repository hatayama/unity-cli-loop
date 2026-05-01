namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Centralizes TCP port rules used by the Unity bridge server.
    /// </summary>
    public static class McpPortValidator
    {
        private const int MinPort = 1;
        private const int MaxPort = 65535;
        private const int ReservedPortThreshold = 1024;
        private static readonly int[] CommonPorts = { 80, 443, 21, 22, 23, 25, 53, 110, 143, 993, 995, 3389 };

        /// <summary>
        /// Validates port number and logs warnings for potential issues
        /// </summary>
        /// <param name="port">Port number to validate</param>
        /// <param name="context">Additional context for logging (optional)</param>
        /// <returns>True if port is valid, false if port is outside valid range</returns>
        public static bool ValidatePort(int port, string context = "")
        {
            if (port <= 0 || port > MaxPort)
            {
                return false;
            }

            if (port < ReservedPortThreshold)
            {
                return false;
            }
            
            if (System.Array.IndexOf(CommonPorts, port) != -1)
            {
                return false;
            }

            return true;
        }
    }
}
