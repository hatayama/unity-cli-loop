using System;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Context information about the hierarchy
    /// </summary>
    [Serializable]
    public class HierarchyContext
    {
        /// <summary>
        /// Type of scene context
        /// </summary>
        public readonly string sceneType;
        
        /// <summary>
        /// Name of the current scene or prefab
        /// </summary>
        public readonly string sceneName;
        
        /// <summary>
        /// Total number of nodes in the hierarchy
        /// </summary>
        public readonly int nodeCount;
        
        /// <summary>
        /// Maximum depth found in the hierarchy
        /// </summary>
        public readonly int maxDepth;
        
        /// <summary>
        /// Constructor for HierarchyContext
        /// </summary>
        public HierarchyContext(string sceneType, string sceneName, int nodeCount, int maxDepth)
        {
            this.sceneType = sceneType ?? "unknown";
            this.sceneName = sceneName ?? string.Empty;
            this.nodeCount = nodeCount;
            this.maxDepth = maxDepth;
        }
    }
}