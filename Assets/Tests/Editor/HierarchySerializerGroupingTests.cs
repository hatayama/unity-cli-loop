using System.Collections.Generic;
using System.Linq;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the hierarchy serializer's scene grouping, orphan handling, component lookup table
    /// selection, and path assignment.
    /// </summary>
    public sealed class HierarchySerializerGroupingTests
    {
        private static readonly HierarchyContext Context = new HierarchyContext("editor", "SampleScene", 0, 0);

        private HierarchySerializer _serializer;

        [SetUp]
        public void SetUp()
        {
            _serializer = new HierarchySerializer();
        }

        /// <summary>
        /// Verifies null inputs are treated as no nodes and default options.
        /// </summary>
        [Test]
        public void BuildGroups_WithNullNodesAndOptions_ReturnsAnEmptyResultWithTheContextNames()
        {
            HierarchySerializationResult result = _serializer.BuildGroups(null, Context, null);

            Assert.That(result.Groups, Is.Empty);
            Assert.That(result.Context.sceneType, Is.EqualTo("editor"));
            Assert.That(result.Context.sceneName, Is.EqualTo("SampleScene"));
            Assert.That(result.Context.nodeCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies nodes are split by scene and each group counts its own roots, nodes, and depth.
        /// </summary>
        [Test]
        public void BuildGroups_WithNodesFromTwoScenes_GroupsThemPerSceneWithStats()
        {
            List<HierarchyNode> nodes = new List<HierarchyNode>
            {
                new HierarchyNode("1", "RootA", null, 0, true, null, "SceneA"),
                new HierarchyNode("2", "ChildA", "1", 1, true, null, "SceneA"),
                new HierarchyNode("3", "GrandChildA", "2", 2, false, null, "SceneA"),
                new HierarchyNode("4", "RootB", null, 0, true, null, "SceneB")
            };

            HierarchySerializationResult result = _serializer.BuildGroups(nodes, Context, new HierarchySerializationOptions());

            Assert.That(result.Groups.Select(group => group.sceneName).ToArray(), Is.EqualTo(new[] { "SceneA", "SceneB" }));
            SceneHierarchyGroup sceneA = result.Groups[0];
            Assert.That(sceneA.stats.rootCount, Is.EqualTo(1));
            Assert.That(sceneA.stats.nodeCount, Is.EqualTo(3));
            Assert.That(sceneA.stats.maxDepth, Is.EqualTo(2));
            Assert.That(sceneA.roots[0].children[0].children[0].name, Is.EqualTo("GrandChildA"));
            Assert.That(result.Groups[1].stats.maxDepth, Is.EqualTo(0));
            Assert.That(result.Context.nodeCount, Is.EqualTo(4));
            Assert.That(result.Context.maxDepth, Is.EqualTo(2));
        }

        /// <summary>
        /// Verifies a node whose parent is not in the same scene group becomes a root instead of being dropped.
        /// </summary>
        [Test]
        public void BuildGroups_WithAParentOutsideTheScene_PromotesTheNodeToARoot()
        {
            List<HierarchyNode> nodes = new List<HierarchyNode>
            {
                new HierarchyNode("1", "Root", null, 0, true, null, "SceneA"),
                new HierarchyNode("2", "Orphan", "missing", 1, true, null, "SceneA")
            };

            HierarchySerializationResult result = _serializer.BuildGroups(nodes, Context, new HierarchySerializationOptions());

            Assert.That(result.Groups[0].roots.Select(root => root.name).ToArray(), Is.EqualTo(new[] { "Root", "Orphan" }));
            Assert.That(result.Groups[0].stats.rootCount, Is.EqualTo(2));
        }

        /// <summary>
        /// Verifies a forced lookup table lists each component once in first-seen order and swaps each node's
        /// names for indexes into it.
        /// </summary>
        [Test]
        public void BuildGroups_WithTheLookupTableForced_ReplacesComponentNamesWithIndexes()
        {
            List<HierarchyNode> nodes = new List<HierarchyNode>
            {
                new HierarchyNode("1", "Root", null, 0, true, new[] { "Transform", "Camera" }, "SceneA"),
                new HierarchyNode("2", "Child", "1", 1, true, new[] { "Transform", "Light" }, "SceneA")
            };

            HierarchySerializationResult result = _serializer.BuildGroups(
                nodes,
                Context,
                new HierarchySerializationOptions { UseComponentsLut = "true" });

            SceneHierarchyGroup group = result.Groups[0];
            Assert.That(group.componentsLut, Is.EqualTo(new List<string> { "Transform", "Camera", "Light" }));
            Assert.That(group.roots[0].componentsIdx, Is.EqualTo(new[] { 0, 1 }));
            Assert.That(group.roots[0].children[0].componentsIdx, Is.EqualTo(new[] { 0, 2 }));
            Assert.That(group.roots[0].components, Is.Null);
        }

        /// <summary>
        /// Verifies a disabled lookup table keeps names even when they are heavily duplicated.
        /// </summary>
        [Test]
        public void BuildGroups_WithTheLookupTableDisabled_KeepsComponentNames()
        {
            HierarchySerializationResult result = _serializer.BuildGroups(
                CreateNodesWithComponents(30, new[] { "Transform", "MeshRenderer" }),
                Context,
                new HierarchySerializationOptions { UseComponentsLut = "false" });

            Assert.That(result.Groups[0].componentsLut, Is.Null);
            Assert.That(result.Groups[0].roots[0].components, Is.EqualTo(new[] { "Transform", "MeshRenderer" }));
        }

        /// <summary>
        /// Verifies the automatic rule turns the lookup table on for at least 50 names that are mostly duplicates.
        /// </summary>
        [Test]
        public void BuildGroups_AutoWithManyDuplicatedComponents_UsesTheLookupTable()
        {
            HierarchySerializationResult result = _serializer.BuildGroups(
                CreateNodesWithComponents(25, new[] { "Transform", "MeshRenderer" }),
                Context,
                new HierarchySerializationOptions());

            Assert.That(result.Groups[0].componentsLut, Is.EqualTo(new List<string> { "Transform", "MeshRenderer" }));
        }

        /// <summary>
        /// Verifies the automatic rule stays off at 49 component names even when all are duplicates.
        /// </summary>
        [Test]
        public void BuildGroups_AutoWithFewerThanFiftyNames_KeepsComponentNames()
        {
            HierarchySerializationResult result = _serializer.BuildGroups(
                CreateNodesWithComponents(49, new[] { "Transform" }),
                Context,
                new HierarchySerializationOptions());

            Assert.That(result.Groups[0].componentsLut, Is.Null);
        }

        /// <summary>
        /// Verifies the automatic rule stays off when half or more of the names are unique.
        /// </summary>
        [Test]
        public void BuildGroups_AutoWithMostlyUniqueNames_KeepsComponentNames()
        {
            List<HierarchyNode> nodes = new List<HierarchyNode>();
            for (int i = 0; i < 25; i++)
            {
                nodes.Add(new HierarchyNode(i.ToString(), "Node" + i, null, 0, true, new[] { "Transform", "Unique" + i }, "SceneA"));
            }

            HierarchySerializationResult result = _serializer.BuildGroups(nodes, Context, new HierarchySerializationOptions());

            Assert.That(result.Groups[0].componentsLut, Is.Null);
        }

        /// <summary>
        /// Verifies requested paths join each node's ancestors with slashes from its scene root.
        /// </summary>
        [Test]
        public void BuildGroups_WithPathsRequested_AssignsSlashSeparatedPaths()
        {
            List<HierarchyNode> nodes = new List<HierarchyNode>
            {
                new HierarchyNode("1", "Root", null, 0, true, null, "SceneA"),
                new HierarchyNode("2", "Child", "1", 1, true, null, "SceneA"),
                new HierarchyNode("3", "Leaf", "2", 2, true, null, "SceneA")
            };

            HierarchySerializationResult result = _serializer.BuildGroups(
                nodes,
                Context,
                new HierarchySerializationOptions { IncludePaths = true });

            HierarchyNodeNested root = result.Groups[0].roots[0];
            Assert.That(root.path, Is.EqualTo("Root"));
            Assert.That(root.children[0].path, Is.EqualTo("Root/Child"));
            Assert.That(root.children[0].children[0].path, Is.EqualTo("Root/Child/Leaf"));
        }

        /// <summary>
        /// Verifies paths stay unset unless requested.
        /// </summary>
        [Test]
        public void BuildGroups_WithoutPathsRequested_LeavesPathsUnset()
        {
            List<HierarchyNode> nodes = new List<HierarchyNode>
            {
                new HierarchyNode("1", "Root", null, 0, true, null, "SceneA")
            };

            HierarchySerializationResult result = _serializer.BuildGroups(nodes, Context, new HierarchySerializationOptions());

            Assert.That(result.Groups[0].roots[0].path, Is.Null);
        }

        private static List<HierarchyNode> CreateNodesWithComponents(int count, string[] components)
        {
            List<HierarchyNode> nodes = new List<HierarchyNode>();
            for (int i = 0; i < count; i++)
            {
                nodes.Add(new HierarchyNode(i.ToString(), "Node" + i, null, 0, true, components, "SceneA"));
            }

            return nodes;
        }
    }
}
