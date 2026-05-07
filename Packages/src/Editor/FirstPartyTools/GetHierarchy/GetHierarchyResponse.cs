using System;
using System.Collections.Generic;
using Newtonsoft.Json;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Response structure for GetHierarchy tool
    /// Always returns the JSON file path of exported hierarchy data.
    /// </summary>
    [Serializable]
    public class GetHierarchyResponse : UnityCliLoopToolResponse
    {
        /// <summary>
        /// Human-readable guidance for clients to locate and read the JSON file
        /// </summary>
        [JsonProperty(Order = -2)]
        public string message { get; }

        /// <summary>
        /// File path where hierarchy data was saved
        /// </summary>
        [JsonProperty(Order = -1)]
        public string hierarchyFilePath { get; }

        public GetHierarchyResponse(string filePath, string message = null)
        {
            this.hierarchyFilePath = filePath ?? string.Empty;
            this.message = string.IsNullOrEmpty(message)
                ? "Hierarchy data saved below. Open the JSON to read 'Context' and 'Hierarchy'."
                : message;
        }
    }
}