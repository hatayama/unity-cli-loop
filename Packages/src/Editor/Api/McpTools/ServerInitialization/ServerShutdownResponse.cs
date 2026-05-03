namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Server shutdown response
    /// </summary>
    public class ServerShutdownResponse : BaseToolResponse
    {
        /// <summary>
        /// Whether the operation was successful
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Result message
        /// </summary>
        public string Message { get; set; }
    }
}