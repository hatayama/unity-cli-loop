using System;
using System.IO;
using UnityEngine;
using Newtonsoft.Json;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Exports FindGameObjects results to external JSON files when multiple objects are selected.
    /// Related classes:
    /// - FindGameObjectsUseCase: Uses this exporter for multiple selection results
    /// - HierarchyResultExporter: Similar pattern for hierarchy export
    /// </summary>
    public static class FindGameObjectsResultExporter
    {
        private static readonly string EXPORT_DIR = Path.Combine(McpConstants.OUTPUT_ROOT_DIR, McpConstants.FIND_GAMEOBJECTS_RESULTS_DIR);
        private const string FILE_PREFIX = "find-game-objects";

        /// <summary>
        /// Export search results to JSON file
        /// </summary>
        /// <param name="results">FindGameObjectResult array to export</param>
        /// <returns>Relative path to the exported file</returns>
        public static string ExportResults(FindGameObjectResult[] results)
        {
            if (results == null)
            {
                results = new FindGameObjectResult[0];
            }

            // Create export directory if it doesn't exist
            string exportDir = Path.Combine(Application.dataPath, "..", EXPORT_DIR);
            Directory.CreateDirectory(exportDir);

            // Generate filename with timestamp
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string filename = $"{FILE_PREFIX}_{timestamp}.json";
            string filePath = Path.Combine(exportDir, filename);

            // Create export data structure
            FindGameObjectsExportData exportData = new FindGameObjectsExportData
            {
                ExportTimestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                TotalCount = results.Length,
                Results = results
            };

            // Export to JSON using Newtonsoft.Json for proper serialization
            JsonSerializerSettings settings = new JsonSerializerSettings
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                MaxDepth = McpServerConfig.DEFAULT_JSON_MAX_DEPTH,
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore
            };
            string jsonContent = JsonConvert.SerializeObject(exportData, settings);
            File.WriteAllText(filePath, jsonContent);

            // Return relative path
            return Path.Combine(EXPORT_DIR, filename);
        }

        /// <summary>
        /// Data structure for FindGameObjects export
        /// </summary>
        [Serializable]
        public class FindGameObjectsExportData
        {
            public string ExportTimestamp;
            public int TotalCount;
            public FindGameObjectResult[] Results;
        }
    }
}
