namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Schema for server initialization request
    /// </summary>
    public class ServerInitializationSchema : BaseToolSchema
    {
        public bool PreserveStartupLockUntilExplicitRelease { get; set; }
    }
}
