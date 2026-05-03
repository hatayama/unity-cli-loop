using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Newtonsoft.Json;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Tool for exporting hierarchy results to external files
    /// Related classes:
    /// - HierarchyNodeNested: Data structure for hierarchy nodes
    /// - GetHierarchyResponse: Response structure containing hierarchy data
    /// - HierarchyContext: Context information for hierarchy
    /// </summary>
    public static class HierarchyResultExporter
    {
        private static readonly string EXPORT_DIR = Path.Combine(McpConstants.OUTPUT_ROOT_DIR, McpConstants.HIERARCHY_RESULTS_DIR);
        private const string FILE_PREFIX = "hierarchy";
        
        /// <summary>
        /// Export hierarchy results to JSON file
        /// </summary>
        /// <param name="groups">Scene-grouped hierarchy nodes to export</param>
        /// <param name="context">Context information about the hierarchy</param>
        /// <returns>Relative path to the exported file</returns>
        public static string ExportHierarchyResults(List<SceneHierarchyGroup> groups, HierarchyContext context)
        {
            if (groups == null)
            {
                groups = new List<SceneHierarchyGroup>();
            }
            
            // Create export directory if it doesn't exist
            string exportDir = Path.Combine(Application.dataPath, "..", EXPORT_DIR);
            Directory.CreateDirectory(exportDir);
            
            // Generate filename with timestamp
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string filename = $"{FILE_PREFIX}_{timestamp}.json";
            string filePath = Path.Combine(exportDir, filename);
            
            // Create export data structure
            // Ensure property order: ExportTimestamp -> Context -> Hierarchy
            var exportData = new HierarchyExportData
            {
                ExportTimestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Context = context,
                Hierarchy = groups
            };
            
            // Export to JSON using Newtonsoft.Json for proper serialization
            var settings = new JsonSerializerSettings
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
        /// Data structure for hierarchy export
        /// </summary>
        [Serializable]
        public class HierarchyExportData
        {
            public string ExportTimestamp;
            public HierarchyContext Context;
            public List<SceneHierarchyGroup> Hierarchy;
        }
    }
}