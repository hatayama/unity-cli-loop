using System;
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
            return new GetToolDetailsResponse
            {
                Tools = includeDevelopmentOnly ? allTools : ExcludeDevelopmentOnly(allTools)
            };
        }

        /// <summary>
        /// Lists the tools a get-tool-details request without IncludeDevelopmentOnly returns, before
        /// internal tools are dropped, so the caller can drop those later off the main thread.
        /// </summary>
        public static ToolInfo[] CreateCatalogSnapshotIncludingInternal(
            UnityCliLoopToolRegistrarService toolRegistrarService)
        {
            UnityCliLoopToolRegistry registry = toolRegistrarService.GetRegistry();
            return ExcludeDevelopmentOnly(registry.GetRegisteredToolsIncludingInternal());
        }

        private static ToolInfo[] ExcludeDevelopmentOnly(ToolInfo[] tools)
        {
            return tools
                .Where(tool => !tool.DisplayDevelopmentOnly)
                .ToArray();
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
                StringComparison.OrdinalIgnoreCase) ?? false;
        }
    }
}
