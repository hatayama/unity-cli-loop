namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Response schema for GetToolDetails tool
    /// Provides type-safe response structure for tool information
    /// </summary>
    public class GetToolDetailsResponse : BaseToolResponse
    {
        /// <summary>
        /// Array of detailed tool information
        /// </summary>
        public ToolInfo[] Tools { get; set; }
    }
} 