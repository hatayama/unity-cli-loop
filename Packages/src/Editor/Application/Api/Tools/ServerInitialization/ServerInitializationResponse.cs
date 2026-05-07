using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Application
{
    /// <summary>
    /// Server initialization response
    /// </summary>
    public class ServerInitializationResponse : UnityCliLoopToolResponse
    {
        /// <summary>
        /// Whether the operation was successful
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Whether the server started successfully
        /// </summary>
        public bool IsRunning { get; set; }

        /// <summary>
        /// Result message
        /// </summary>
        public string Message { get; set; }

        public IUnityCliLoopServerInstance ServerInstance { get; set; }
    }
}
