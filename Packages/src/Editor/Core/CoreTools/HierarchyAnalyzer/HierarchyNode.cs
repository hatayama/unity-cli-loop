using System;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Represents a single node in the Unity Hierarchy
    /// Immutable data structure for AI-friendly JSON serialization
    /// </summary>
    [Serializable]
    public class HierarchyNode
    {
        /// <summary>
        /// Session-unique Unity object identifier
        /// </summary>
        public readonly string id;
        
        /// <summary>
        /// GameObject name
        /// </summary>
        public readonly string name;
        
        /// <summary>
        /// Parent node's session-unique Unity object identifier (null for root objects)
        /// </summary>
        public readonly string parent;
        
        /// <summary>
        /// Depth level in hierarchy (0 for root)
        /// </summary>
        public readonly int depth;
        
        /// <summary>
        /// Whether the GameObject is active
        /// </summary>
        public readonly bool isActive;
        
        /// <summary>
        /// List of component type names attached to this GameObject
        /// </summary>
        public readonly string[] components;
        
        // Additional optional metadata for grouping/ordering
        public readonly int? siblingIndex;
        public readonly string tag;
        public readonly int? layer;
        
        /// <summary>
        /// Scene name this GameObject belongs to
        /// </summary>
        public readonly string sceneName;
        
        /// <summary>
        /// Constructor for HierarchyNode
        /// </summary>
        public HierarchyNode(string id, string name, string parent, int depth, bool isActive, string[] components, string sceneName = "", int? siblingIndex = null, string tag = null, int? layer = null)
        {
            this.id = id ?? string.Empty;
            this.name = name ?? string.Empty;
            this.parent = parent;
            this.depth = depth;
            this.isActive = isActive;
            this.components = components ?? Array.Empty<string>();
            this.sceneName = sceneName ?? string.Empty;
            this.siblingIndex = siblingIndex;
            this.tag = tag;
            this.layer = layer;
        }
    }
}
