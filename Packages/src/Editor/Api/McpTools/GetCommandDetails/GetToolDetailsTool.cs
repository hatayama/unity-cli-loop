using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// GetToolDetails tool handler - Type-safe implementation using Schema and Response
    /// Retrieves detailed information about all registered Unity CLI tools
    /// Related classes:
    /// - UnityToolRegistry: Source of tool information
    /// - ToolInfo: Data structure for tool details
    /// - GetToolDetailsResponse: Type-safe response structure
    /// </summary>
    [McpTool(
        Description = "Retrieve detailed information about all registered Unity CLI tools",
        DisplayDevelopmentOnly = true
    )]
    public class GetToolDetailsTool : AbstractUnityTool<GetToolDetailsSchema, GetToolDetailsResponse>
    {
        public override string ToolName => "get-tool-details";

        protected override Task<GetToolDetailsResponse> ExecuteAsync(GetToolDetailsSchema parameters, CancellationToken ct)
        {
            // Type-safe parameter access
            bool includeDevelopmentOnly = parameters.IncludeDevelopmentOnly;

            // Get tool registry and retrieve all registered tools
            UnityToolRegistry registry = CustomToolManager.GetRegistry();
            ToolInfo[] allTools = registry.GetRegisteredTools();

            // Filter tools based on development-only setting
            ToolInfo[] filteredTools = allTools;
            if (!includeDevelopmentOnly)
            {
                filteredTools = allTools
                    .Where(tool => !tool.DisplayDevelopmentOnly)
                    .ToArray();
            }

            // Create type-safe response
            GetToolDetailsResponse response = new GetToolDetailsResponse
            {
                Tools = filteredTools
            };

            return Task.FromResult(response);
        }
    }
}
