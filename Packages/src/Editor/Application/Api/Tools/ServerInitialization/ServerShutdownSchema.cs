using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Application
{
    /// <summary>
    /// Schema for server shutdown request
    /// </summary>
    public class ServerShutdownSchema : UnityCliLoopToolSchema
    {
        /// <summary>
        /// Force shutdown flag
        /// </summary>
        public bool ForceShutdown { get; set; } = false;
    }
}