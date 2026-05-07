using System;
using System.IO;
using UnityEngine;
using Newtonsoft.Json;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Exports FindGameObjects results to external JSON files when multiple objects are selected.
    /// Related classes:
    /// - FindGameObjectsUseCase: Uses this exporter for multiple selection results
    /// - HierarchyResultExporter: Similar pattern for hierarchy export
    /// </summary>
    public static class FindGameObjectsResultExporter
    {
        private static readonly string EXPORT_DIR = Path.Combine(UnityCliLoopConstants.OUTPUT_ROOT_DIR, UnityCliLoopConstants.FIND_GAMEOBJECTS_RESULTS_DIR);
        private const string FILE_PREFIX = "find-game-objects";

        /// <summary>
        /// Export search results to JSON file
        /// </summary>
        /// <param name="results">GameObject result array to export</param>
        /// <returns>Relative path to the exported file</returns>
        public static string ExportResults(UnityCliLoopGameObjectResult[] results)
        {
            if (results == null)
            {
                results = new UnityCliLoopGameObjectResult[0];
            }

            // Create export directory if it doesn't exist
            string exportDir = Path.Combine(UnityEngine.Application.dataPath, "..", EXPORT_DIR);
            Directory.CreateDirectory(exportDir);

            // Generate filename with timestamp
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string filename = $"{FILE_PREFIX}_{timestamp}.json";
            string filePath = Path.Combine(exportDir, filename);

            // Create export data structure
            FindGameObjectsExportData exportData = new()            {
                ExportTimestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                TotalCount = results.Length,
                Results = results
            };

            // Export to JSON using Newtonsoft.Json for proper serialization
            JsonSerializerSettings settings = new()            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                MaxDepth = UnityCliLoopServerConfig.DEFAULT_JSON_MAX_DEPTH,
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
            public UnityCliLoopGameObjectResult[] Results;
        }
    }
}
