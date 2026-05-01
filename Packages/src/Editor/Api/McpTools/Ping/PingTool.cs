using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Development-only connection test tool.
    /// </summary>
    [McpTool(
        DisplayDevelopmentOnly = true,
        Description = "Connection test and message echo"
    )]
    public class PingTool : AbstractUnityTool<PingSchema, PingResponse>
    {
        public override string ToolName => "ping";

        protected override Task<PingResponse> ExecuteAsync(PingSchema parameters, CancellationToken ct)
        {
            string message = parameters.Message;
            string response = $"Unity CLI Loop bridge received: {message}";

            PingResponse pingResponse = new PingResponse(response);
            return Task.FromResult(pingResponse);
        }
    }
}
