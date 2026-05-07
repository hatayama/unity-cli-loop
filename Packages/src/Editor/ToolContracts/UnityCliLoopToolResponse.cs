namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// Base response class for Unity CLI tool responses.
    /// </summary>
    public abstract class UnityCliLoopToolResponse
    {
        /// <summary>
        /// Unity package version for CLI compatibility checks.
        /// </summary>
        public string Ver { get; private set; } = string.Empty;

        public void SetVersion(string version)
        {
            System.Diagnostics.Debug.Assert(version != null, "version must not be null.");
            Ver = version;
        }
    }
}
