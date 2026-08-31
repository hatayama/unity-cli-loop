using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Samples
{
    /// <summary>
    /// Response schema for HelloWorld tool
    /// Provides type-safe response structure
    /// </summary>
    public class HelloWorldResponse : UnityCliLoopToolResponse
    {
        /// <summary>
        /// The greeting message
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// Timestamp when the greeting was generated (optional)
        /// </summary>
        public string Timestamp { get; set; }

        /// <summary>
        /// Language used for the greeting
        /// </summary>
        public string Language { get; set; }

        /// <summary>
        /// Create a new HelloWorldResponse
        /// </summary>
        /// <param name="message">Greeting message</param>
        /// <param name="language">Language used</param>
        /// <param name="timestamp">Optional timestamp</param>
        public HelloWorldResponse(string message, string language, string timestamp = null)
        {
            Message = message;
            Language = language;
            Timestamp = timestamp;
        }

        /// <summary>
        /// Parameterless constructor for JSON deserialization
        /// </summary>
        public HelloWorldResponse()
        {
        }
    }
} 