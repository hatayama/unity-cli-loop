namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Base response class for Unity CLI tool responses.
    /// </summary>
    public abstract class BaseToolResponse
    {
        /// <summary>
        /// Unity package version for CLI compatibility checks.
        /// </summary>
        public string Ver => McpVersion.VERSION;
    }
}
