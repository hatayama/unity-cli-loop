using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Represents a single node in the Unity Hierarchy with nested structure
    /// Immutable data structure for AI-friendly JSON serialization with direct parent-child relationships
    /// </summary>
    [Serializable]
    public class HierarchyNodeNested
    {
        /// <summary>
        /// GameObject name
        /// </summary>
        public readonly string name;
        
        /// <summary>
        /// Whether the GameObject is active
        /// </summary>
        public readonly bool isActive;
        
        /// <summary>
        /// List of component type names attached to this GameObject
        /// </summary>
        public string[] components;
        public int[] componentsIdx;
        
        /// <summary>
        /// Child nodes directly nested under this node
        /// </summary>
        [JsonProperty(Order = 1000)]
        public readonly List<HierarchyNodeNested> children;

        // Optional compact metadata
        public readonly int? siblingIndex;
        public readonly string tag;
        public readonly int? layer;
        public string path;
        
        /// <summary>
        /// Constructor for HierarchyNodeNested
        /// </summary>
        public HierarchyNodeNested(string name, bool isActive, string[] components, List<HierarchyNodeNested> children = null, int? siblingIndex = null, string tag = null, int? layer = null, string path = null, int[] componentsIdx = null)
        {
            this.name = name ?? string.Empty;
            this.isActive = isActive;
            this.components = components ?? Array.Empty<string>();
            this.componentsIdx = componentsIdx;
            this.children = children ?? new List<HierarchyNodeNested>();
            this.siblingIndex = siblingIndex;
            this.tag = tag;
            this.layer = layer;
            this.path = path;
        }
    }
}