using System.IO;
using System.Text;

using Newtonsoft.Json;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Publishes the tool catalog to .uloop/tools.json so package updates refresh the CLI cache without a manual sync.
    /// </summary>
    internal static class ToolCatalogCachePublisher
    {
        /// <summary>
        /// Writes get-tool-details JSON to the project tools cache when the catalog content changed.
        /// </summary>
        public static void Publish(UnityCliLoopToolRegistrarService toolRegistrarService)
        {
            Debug.Assert(toolRegistrarService != null, "toolRegistrarService must not be null");

            GetToolDetailsResponse response = GetToolDetailsBridgeCommand.Execute(null, toolRegistrarService);
            // Why reuse JsonRpcResponseSerializer: tools.json must match the IPC result bytes that uloop sync writes.
            string json = JsonConvert.SerializeObject(
                response,
                Formatting.None,
                JsonRpcResponseSerializer.Settings);

            string cachePath = Path.Combine(
                UnityCliLoopConstants.ULOOP_DIR,
                UnityCliLoopConstants.ULOOP_TOOLS_CACHE_FILE_NAME);

            if (File.Exists(cachePath))
            {
                string existingJson = File.ReadAllText(cachePath);
                if (existingJson == json)
                {
                    return;
                }
            }

            string directory = Path.GetDirectoryName(cachePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(cachePath, json, new UTF8Encoding(false));
        }
    }
}
