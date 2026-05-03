using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Scene-level grouping of hierarchy nodes for AI-friendly JSON export
    /// </summary>
    [Serializable]
    public class SceneHierarchyGroup
    {
        public readonly string sceneName;

        public readonly SceneHierarchyStats stats;

        // Optional: lookup table for component names to reduce redundancy
        public readonly List<string> componentsLut;

        public readonly List<HierarchyNodeNested> roots;

        public SceneHierarchyGroup(
            string sceneName,
            SceneHierarchyStats stats,
            List<HierarchyNodeNested> roots,
            List<string> componentsLut = null)
        {
            this.sceneName = sceneName ?? string.Empty;
            this.stats = stats ?? new SceneHierarchyStats(0, 0, 0);
            this.roots = roots ?? new List<HierarchyNodeNested>();
            this.componentsLut = componentsLut; // may be null when not used
        }
    }

    [Serializable]
    public class SceneHierarchyStats
    {
        public readonly int rootCount;
        public readonly int nodeCount;
        public readonly int maxDepth;

        public SceneHierarchyStats(int rootCount, int nodeCount, int maxDepth)
        {
            this.rootCount = rootCount;
            this.nodeCount = nodeCount;
            this.maxDepth = maxDepth;
        }
    }
}


