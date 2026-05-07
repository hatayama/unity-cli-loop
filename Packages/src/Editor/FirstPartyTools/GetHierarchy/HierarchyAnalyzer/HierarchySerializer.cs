using System.Collections.Generic;
using System.Linq;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Serializer for converting flat hierarchy nodes to scene-grouped nested structures
    /// Related classes:
    /// - HierarchyNode: Flat hierarchy structure (collected by service)
    /// - HierarchyNodeNested: Nested hierarchy structure (export model)
    /// - SceneHierarchyGroup: Scene-grouped container with stats and LUTs (export model)
    /// </summary>
    public class HierarchySerializer
    {
        public HierarchySerializationResult BuildGroups(List<HierarchyNode> nodes, HierarchyContext context, HierarchySerializationOptions options)
        {
            if (nodes == null)
            {
                nodes = new List<HierarchyNode>();
            }

            if (options == null)
            {
                options = new HierarchySerializationOptions();
            }

            int nodeCount = nodes.Count;
            int maxDepth = nodes.Any() ? nodes.Max(n => n.depth) : 0;

            HierarchyContext updatedContext = new(
                context.sceneType,
                context.sceneName,
                nodeCount,
                maxDepth
            );

            // Group by scene
            var groups = nodes
                .GroupBy(n => n.sceneName)
                .Select(g => BuildGroupForScene(g.Key ?? string.Empty, g.ToList(), options))
                .ToList();

            return new HierarchySerializationResult(groups, updatedContext);
        }

        private SceneHierarchyGroup BuildGroupForScene(string sceneName, List<HierarchyNode> sceneNodes, HierarchySerializationOptions options)
        {
            // Build nested structure per scene
            Dictionary<int, HierarchyNodeNested> nodeDict = new();

            foreach (HierarchyNode flat in sceneNodes)
            {
                HierarchyNodeNested nested = new(
                    name: flat.name,
                    isActive: flat.isActive,
                    components: flat.components,
                    children: null,
                    siblingIndex: flat.siblingIndex,
                    tag: flat.tag,
                    layer: flat.layer
                );
                nodeDict[flat.id] = nested;
            }

            List<HierarchyNodeNested> roots = new();
            foreach (HierarchyNode flat in sceneNodes)
            {
                HierarchyNodeNested nested = nodeDict[flat.id];
                if (flat.parent == null)
                {
                    roots.Add(nested);
                }
                else if (nodeDict.TryGetValue(flat.parent.Value, out var parentNested))
                {
                    parentNested.children.Add(nested);
                }
                else
                {
                    roots.Add(nested);
                }
            }

            // Stats
            int rootCount = roots.Count;
            int groupNodeCount = sceneNodes.Count;
            int groupMaxDepth = sceneNodes.Any() ? sceneNodes.Max(n => n.depth) : 0;
            SceneHierarchyStats stats = new(rootCount, groupNodeCount, groupMaxDepth);

            // Optional LUTs
            List<string> componentsLut = null;
            if (ShouldUseComponentsLut(options, sceneNodes))
            {
                componentsLut = BuildComponentsLutAndApply(sceneNodes, nodeDict);
            }

            if (options.IncludePaths)
            {
                // Assign direct string paths per node; no LUT
                AssignStringPaths(roots);
            }

            return new SceneHierarchyGroup(sceneName, stats, roots, componentsLut);
        }

        private static bool ShouldUseComponentsLut(HierarchySerializationOptions options, List<HierarchyNode> sceneNodes)
        {
            if (options == null) return false;
            if (options.UseComponentsLut == "true") return true;
            if (options.UseComponentsLut == "false") return false;
            // auto heuristic: high duplication of component names
            List<string> all = new();
            foreach (var n in sceneNodes)
            {
                if (n.components != null && n.components.Length > 0)
                {
                    all.AddRange(n.components);
                }
            }
            if (all.Count < 50) return false;
            int unique = all.Distinct().Count();
            return unique * 2 < all.Count; // more than 50% duplicates
        }

        private static List<string> BuildComponentsLutAndApply(List<HierarchyNode> sceneNodes, Dictionary<int, HierarchyNodeNested> nodeDict)
        {
            Dictionary<string, int> lutIndex = new();
            List<string> lut = new();

            foreach (HierarchyNode flat in sceneNodes)
            {
                if (flat.components == null) continue;
                int[] idx = new int[flat.components.Length];
                for (int i = 0; i < flat.components.Length; i++)
                {
                    string comp = flat.components[i];
                    if (!lutIndex.TryGetValue(comp, out int index))
                    {
                        index = lut.Count;
                        lutIndex[comp] = index;
                        lut.Add(comp);
                    }
                    idx[i] = index;
                }

                HierarchyNodeNested nested = nodeDict[flat.id];
                nested.componentsIdx = idx;
                nested.components = null;
            }

            return lut;
        }

        private static void AssignStringPaths(List<HierarchyNodeNested> roots)
        {
            void Traverse(HierarchyNodeNested node, string parentPath)
            {
                string path = string.IsNullOrEmpty(parentPath) ? node.name : parentPath + "/" + node.name;
                node.path = path;

                foreach (HierarchyNodeNested child in node.children)
                {
                    Traverse(child, path);
                }
            }

            foreach (HierarchyNodeNested root in roots)
            {
                Traverse(root, string.Empty);
            }
        }
    }
    
        /// <summary>
        /// Describes optional settings that tune Hierarchy Serialization behavior.
        /// </summary>
        public class HierarchySerializationOptions
    {
        public bool IncludePaths { get; set; }
            public string UseComponentsLut { get; set; } = "auto"; // auto|true|false
    }

    /// <summary>
    /// Carries the result data produced by Hierarchy Serialization behavior.
    /// </summary>
    public class HierarchySerializationResult
    {
        public readonly List<SceneHierarchyGroup> Groups;
        public readonly HierarchyContext Context;

        public HierarchySerializationResult(List<SceneHierarchyGroup> groups, HierarchyContext context)
        {
            this.Groups = groups ?? new List<SceneHierarchyGroup>();
            this.Context = context;
        }
    }
}