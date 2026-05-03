namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Response schema for Ping command
    /// Provides type-safe response structure
    /// </summary>
    public class PingResponse : BaseToolResponse
    {
        /// <summary>
        /// The response message from Unity
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// Create a new PingResponse
        /// </summary>
        /// <param name="message">Response message</param>
        public PingResponse(string message)
        {
            Message = message;
        }

        /// <summary>
        /// Parameterless constructor for JSON deserialization
        /// </summary>
        public PingResponse()
        {
            Message = string.Empty;
        }
    }
} 