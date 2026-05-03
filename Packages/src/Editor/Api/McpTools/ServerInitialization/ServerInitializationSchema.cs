namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Schema for server initialization request
    /// </summary>
    public class ServerInitializationSchema : BaseToolSchema
    {
        public bool PreserveStartupLockUntilExplicitRelease { get; set; }
    }
}
