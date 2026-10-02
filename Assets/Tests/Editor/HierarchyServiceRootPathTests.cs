using System.Collections.Generic;
using System.Linq;

using NUnit.Framework;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies how the hierarchy service resolves a root path against the loaded scene roots and skips
    /// inactive roots, on temporary GameObjects with names no other object uses.
    /// </summary>
    public sealed class HierarchyServiceRootPathTests
    {
        private const string RootName = "HierarchyRootPathFixtureRoot";
        private const string ChildName = "HierarchyRootPathFixtureChild";
        private const string LeafName = "HierarchyRootPathFixtureLeaf";

        private GameObject _root;
        private HierarchyService _service;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject(RootName);
            GameObject child = new GameObject(ChildName);
            child.transform.SetParent(_root.transform);
            GameObject leaf = new GameObject(LeafName);
            leaf.transform.SetParent(child.transform);
            _service = new HierarchyService();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
        }

        /// <summary>
        /// Verifies a slash-prefixed path that names the root starts the traversal at the named descendant.
        /// </summary>
        [Test]
        public void GetHierarchyNodes_WithAnAbsolutePathThroughTheRoot_StartsAtThatDescendant()
        {
            List<HierarchyNode> nodes = GetNodes($"/{RootName}/{ChildName}");

            Assert.That(nodes.Select(node => node.name).ToArray(), Is.EqualTo(new[] { ChildName, LeafName }));
            Assert.That(nodes[0].parent, Is.Null);
        }

        /// <summary>
        /// Verifies a path that omits the root name is looked up below each scene root.
        /// </summary>
        [Test]
        public void GetHierarchyNodes_WithAPathBelowARoot_FindsItUnderThatRoot()
        {
            List<HierarchyNode> nodes = GetNodes($"{ChildName}/{LeafName}");

            Assert.That(nodes.Select(node => node.name).ToArray(), Is.EqualTo(new[] { LeafName }));
        }

        /// <summary>
        /// Verifies a slash-prefixed root name resolves to that root itself.
        /// </summary>
        [Test]
        public void GetHierarchyNodes_WithASlashPrefixedRootName_StartsAtTheRoot()
        {
            List<HierarchyNode> nodes = GetNodes("/" + RootName);

            Assert.That(nodes.Select(node => node.name).ToArray(), Is.EqualTo(new[] { RootName, ChildName, LeafName }));
        }

        /// <summary>
        /// Verifies a bare slash selects every scene root, including the fixture root and its children.
        /// </summary>
        [Test]
        public void GetHierarchyNodes_WithOnlyASlash_IncludesEverySceneRoot()
        {
            List<HierarchyNode> nodes = GetNodes("/");

            HierarchyNode root = nodes.Single(node => node.name == RootName);
            Assert.That(root.parent, Is.Null);
            Assert.That(nodes.Any(node => node.name == LeafName), Is.True);
        }

        /// <summary>
        /// Verifies a path that matches nothing yields no fixture nodes.
        /// </summary>
        [Test]
        public void GetHierarchyNodes_WithAnUnknownPath_ReturnsNothingFromTheFixture()
        {
            List<HierarchyNode> nodes = GetNodes($"/{RootName}/MissingChild");

            Assert.That(nodes.Any(node => node.name.StartsWith("HierarchyRootPathFixture")), Is.False);
        }

        /// <summary>
        /// Verifies an inactive root is skipped unless inactive objects are requested.
        /// </summary>
        [Test]
        public void GetHierarchyNodes_WithoutInactiveObjects_SkipsAnInactiveRoot()
        {
            _root.SetActive(false);

            List<HierarchyNode> activeOnly = _service.GetHierarchyNodes(
                new HierarchyOptions { RootPath = "/" + RootName, IncludeInactive = false });
            List<HierarchyNode> withInactive = _service.GetHierarchyNodes(
                new HierarchyOptions { RootPath = "/" + RootName, IncludeInactive = true });

            Assert.That(activeOnly, Is.Empty);
            Assert.That(withInactive.Select(node => node.name).First(), Is.EqualTo(RootName));
        }

        private List<HierarchyNode> GetNodes(string rootPath)
        {
            return _service.GetHierarchyNodes(new HierarchyOptions { RootPath = rootPath });
        }
    }
}
