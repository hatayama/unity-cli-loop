using System.Collections.Generic;
using NUnit.Framework;

namespace io.github.hatayama.UnityCliLoop
{
    public class HierarchySerializerTests
    {
        private HierarchySerializer serializer;
        
        [SetUp]
        public void SetUp()
        {
            serializer = new HierarchySerializer();
        }
        
        [Test]
        public void BuildGroups_WithValidNodes_ReturnsCorrectGroups()
        {
            // Arrange
            List<HierarchyNode> nodes = new()
            {
                new(1, "Root", null, 0, true, new[] { "Transform" }, "SceneA"),
                new(2, "Child", 1, 1, true, new[] { "Transform", "MeshRenderer" }, "SceneA")
            };
            
            HierarchyContext context = new HierarchyContext("editor", "TestScene", 0, 0);
            
            // Act
            HierarchySerializationResult result = serializer.BuildGroups(nodes, context, new HierarchySerializationOptions());
            
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Groups, Is.Not.Null);
            Assert.That(result.Groups.Count, Is.EqualTo(1));

            SceneHierarchyGroup group = result.Groups[0];
            Assert.That(group.sceneName, Is.EqualTo("SceneA"));
            HierarchyNodeNested rootNode = group.roots[0];
            Assert.That(rootNode.name, Is.EqualTo("Root"));
            Assert.That(rootNode.children.Count, Is.EqualTo(1));

            HierarchyNodeNested childNode = rootNode.children[0];
            Assert.That(childNode.name, Is.EqualTo("Child"));
            Assert.That(childNode.children.Count, Is.EqualTo(0));

            Assert.That(result.Context, Is.Not.Null);
            Assert.That(result.Context.nodeCount, Is.EqualTo(2));
            Assert.That(result.Context.maxDepth, Is.EqualTo(1));
        }
        
        [Test]
        public void BuildGroups_WithEmptyNodes_ReturnsEmptyGroups()
        {
            // Arrange
            List<HierarchyNode> nodes = new List<HierarchyNode>();
            HierarchyContext context = new HierarchyContext("editor", "EmptyScene", 0, 0);
            
            // Act
            HierarchySerializationResult result = serializer.BuildGroups(nodes, context, new HierarchySerializationOptions());
            
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Groups, Is.Not.Null);
            Assert.That(result.Groups.Count, Is.EqualTo(0));
            Assert.That(result.Context.nodeCount, Is.EqualTo(0));
            Assert.That(result.Context.maxDepth, Is.EqualTo(0));
        }
        
        [Test]
        public void BuildGroups_CalculatesCorrectMaxDepth()
        {
            // Arrange
            List<HierarchyNode> nodes = new()
            {
                new(1, "Root", null, 0, true, new string[0]),
                new(2, "Level1", 1, 1, true, new string[0]),
                new(3, "Level2", 2, 2, true, new string[0]),
                new(4, "Level3", 3, 3, true, new string[0])
            };
            
            HierarchyContext context = new HierarchyContext("editor", "DeepScene", 0, 0);
            
            // Act
            HierarchySerializationResult result = serializer.BuildGroups(nodes, context, new HierarchySerializationOptions());
            
            // Assert
            Assert.That(result.Context.maxDepth, Is.EqualTo(3));
        }
    }
}