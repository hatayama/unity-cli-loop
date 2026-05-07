using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Catalog payload returned by the internal CLI tool-details bridge command.
    /// </summary>
    public class GetToolDetailsResponse : UnityCliLoopToolResponse
    {
        public ToolInfo[] Tools { get; set; }
    }
}
