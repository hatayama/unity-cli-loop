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
        /// Writes the tool catalog to the project tools cache when its content changed. Internal tools
        /// stay in the file; the CLI drops them whenever it reads it.
        /// </summary>
        public static void Publish(UnityCliLoopToolRegistrarService toolRegistrarService)
        {
            Debug.Assert(toolRegistrarService != null, "toolRegistrarService must not be null");

            GetToolDetailsResponse response = GetToolDetailsBridgeCommand.CreateCatalogCacheResponse(toolRegistrarService);
            // Why reuse JsonRpcResponseSerializer: the CLI reads tools.json with one decoder whether
            // uloop sync wrote the IPC result there or this publisher did.
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
