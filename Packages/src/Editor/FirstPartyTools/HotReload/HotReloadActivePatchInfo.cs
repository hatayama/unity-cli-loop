namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// One active hot-reload patch for --status: method key plus the source file path
    /// that was applied (project-relative when applied through the orchestrator).
    /// </summary>
    internal sealed class HotReloadActivePatchInfo
    {
        public string MethodKey { get; }
        public string FilePath { get; }

        public HotReloadActivePatchInfo(string methodKey, string filePath)
        {
            MethodKey = methodKey ?? string.Empty;
            FilePath = filePath ?? string.Empty;
        }
    }
}
