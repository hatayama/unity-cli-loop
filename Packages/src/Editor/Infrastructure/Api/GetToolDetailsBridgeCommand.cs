using System.Linq;
using Newtonsoft.Json.Linq;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Serves the CLI-only catalog request without publishing the catalog command as a tool.
    /// </summary>
    internal static class GetToolDetailsBridgeCommand
    {
        private const string IncludeDevelopmentOnlyPropertyName = "IncludeDevelopmentOnly";

        public static GetToolDetailsResponse Execute(
            JToken paramsToken,
            UnityCliLoopToolRegistrarService toolRegistrarService)
        {
            bool includeDevelopmentOnly = ReadIncludeDevelopmentOnly(paramsToken);

            UnityCliLoopToolRegistry registry = toolRegistrarService.GetRegistry();
            ToolInfo[] allTools = registry.GetRegisteredTools();

            ToolInfo[] filteredTools = allTools;
            if (!includeDevelopmentOnly)
            {
                filteredTools = allTools
                    .Where(tool => !tool.DisplayDevelopmentOnly)
                    .ToArray();
            }

            return new GetToolDetailsResponse
            {
                Tools = filteredTools
            };
        }

        private static bool ReadIncludeDevelopmentOnly(JToken paramsToken)
        {
            JObject parameters = paramsToken as JObject;
            if (parameters == null)
            {
                return false;
            }

            return StrictJsonBooleanMetadataReader.ReadOptionalBoolean(
                parameters,
                IncludeDevelopmentOnlyPropertyName,
                System.StringComparison.OrdinalIgnoreCase) ?? false;
        }
    }
}
