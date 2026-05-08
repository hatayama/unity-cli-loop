using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Version payload returned by the internal CLI readiness bridge command.
    /// </summary>
    public class GetVersionResponse : UnityCliLoopToolResponse
    {
        public string UnityVersion { get; set; } = string.Empty;
    }
}
