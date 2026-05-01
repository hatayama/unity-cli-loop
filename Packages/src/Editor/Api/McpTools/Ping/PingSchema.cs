using System.ComponentModel;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Schema for Ping command parameters
    /// Provides type-safe parameter access with default values
    /// </summary>
    public class PingSchema : BaseToolSchema
    {
        /// <summary>
        /// Message to send to Unity
        /// </summary>
        [Description("Message to send to Unity")]
        public string Message { get; set; } = "Hello from Unity CLI Loop";
    }
}
