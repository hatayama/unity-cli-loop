namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Schema for server initialization request
    /// </summary>
    public class ServerInitializationSchema : BaseToolSchema
    {
        /// <summary>
        /// Port number to start server (-1 to use default)
        /// </summary>
        public int Port { get; set; } = -1;

        public bool PreserveStartupLockUntilExplicitRelease { get; set; }
    }
}
