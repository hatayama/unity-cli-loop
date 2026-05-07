using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies Hierarchy Service behavior.
    /// </summary>
    public class HierarchyServiceTests
    {
        private GameObject testRoot;
        private HierarchyService service;
        private Object[] originalSelection;

        [SetUp]
        public void SetUp()
        {
            service = new HierarchyService();
            testRoot = new GameObject("TestRoot");
            originalSelection = Selection.objects;
        }

        [TearDown]
        public void TearDown()
        {
            Selection.objects = originalSelection;

            if (testRoot != null)
            {
                Object.DestroyImmediate(testRoot);
            }
        }
        
        [Test]
        public void GetHierarchyNodes_WithSingleObject_ReturnsOneNode()
        {
            // Arrange
            HierarchyOptions options = new();
            
            // Act
            List<HierarchyNode> nodes = service.GetHierarchyNodes(options);
            
            // Assert
            Assert.That(nodes, Is.Not.Null);
            Assert.That(nodes.Count, Is.GreaterThan(0));
        }
        
        [Test]
        public void GetHierarchyNodes_WithNestedObjects_ReturnsCorrectDepth()
        {
            // Arrange
            GameObject child = new("Child");
            GameObject grandChild = new("GrandChild");
            child.transform.SetParent(testRoot.transform);
            grandChild.transform.SetParent(child.transform);
            
            HierarchyOptions options = new();
            
            // Act
            List<HierarchyNode> nodes = service.GetHierarchyNodes(options);
            
            // Assert
            HierarchyNode grandChildNode = nodes.Find(n => n.name == "GrandChild");
            Assert.That(grandChildNode, Is.Not.Null);
            Assert.That(grandChildNode.depth, Is.EqualTo(2));
        }
        
        [Test]
        public void GetHierarchyNodes_WithMaxDepth_LimitsDepth()
        {
            // Arrange
            GameObject child = new("Child");
            GameObject grandChild = new("GrandChild");
            GameObject greatGrandChild = new("GreatGrandChild");
            child.transform.SetParent(testRoot.transform);
            grandChild.transform.SetParent(child.transform);
            greatGrandChild.transform.SetParent(grandChild.transform);
            
            HierarchyOptions options = new() { MaxDepth = 1 };
            
            // Act
            List<HierarchyNode> nodes = service.GetHierarchyNodes(options);
            
            // Assert
            Assert.That(nodes.Find(n => n.name == "Child"), Is.Not.Null);
            Assert.That(nodes.Find(n => n.name == "GrandChild"), Is.Null);
            Assert.That(nodes.Find(n => n.name == "GreatGrandChild"), Is.Null);
        }
        
        [Test]
        public void GetHierarchyNodes_WithInactiveFilter_ExcludesInactive()
        {
            // Arrange
            GameObject activeChild = new("ActiveChild");
            GameObject inactiveChild = new("InactiveChild");
            activeChild.transform.SetParent(testRoot.transform);
            inactiveChild.transform.SetParent(testRoot.transform);
            inactiveChild.SetActive(false);
            
            HierarchyOptions options = new() { IncludeInactive = false };
            
            // Act
            List<HierarchyNode> nodes = service.GetHierarchyNodes(options);
            
            // Assert
            Assert.That(nodes.Find(n => n.name == "ActiveChild"), Is.Not.Null);
            Assert.That(nodes.Find(n => n.name == "InactiveChild"), Is.Null);
        }
        
        [Test]
        public void GetHierarchyNodes_WithComponents_IncludesComponentNames()
        {
            // Arrange
            testRoot.AddComponent<BoxCollider>();
            testRoot.AddComponent<Rigidbody>();
            
            HierarchyOptions options = new() { IncludeComponents = true };
            
            // Act
            List<HierarchyNode> nodes = service.GetHierarchyNodes(options);
            
            // Assert
            HierarchyNode rootNode = nodes.Find(n => n.name == "TestRoot");
            Assert.That(rootNode, Is.Not.Null);
            Assert.That(rootNode.components, Contains.Item("BoxCollider"));
            Assert.That(rootNode.components, Contains.Item("Rigidbody"));
            Assert.That(rootNode.components, Contains.Item("Transform"));
        }
        
        [Test]
        public void GetHierarchyNodes_WithRootPathIncludingRootName_ReturnsChild()
        {
            // Arrange
            GameObject child = new("ChildForRootPath");
            child.transform.SetParent(testRoot.transform);
            HierarchyOptions options = new()            {
                RootPath = testRoot.name + "/" + child.name
            };

            // Act
            List<HierarchyNode> nodes = service.GetHierarchyNodes(options);

            // Assert
            HierarchyNode childNode = nodes.Find(n => n.name == child.name);
            Assert.That(childNode, Is.Not.Null);
            Assert.That(childNode.parent, Is.Null);
        }

        [Test]
        public void GetCurrentContext_InEditor_ReturnsEditorContext()
        {
            // Act
            HierarchyContext context = service.GetCurrentContext();

            // Assert
            Assert.That(context, Is.Not.Null);
            Assert.That(context.sceneType, Is.EqualTo("editor").Or.EqualTo("runtime"));
            Assert.That(context.sceneName, Is.Not.Null);
        }

        [Test]
        public void GetHierarchyNodes_WithUseSelection_ReturnsSelectedHierarchy()
        {
            // Arrange
            GameObject child = new("ChildForSelection");
            child.transform.SetParent(testRoot.transform);
            Selection.objects = new Object[] { testRoot };

            HierarchyOptions options = new() { UseSelection = true };

            // Act
            List<HierarchyNode> nodes = service.GetHierarchyNodes(options);

            // Assert
            Assert.That(nodes.Find(n => n.name == testRoot.name), Is.Not.Null);
            Assert.That(nodes.Find(n => n.name == child.name), Is.Not.Null);
        }

        [Test]
        public void GetHierarchyNodes_WithUseSelectionAndNoSelection_ReturnsEmpty()
        {
            // Arrange
            Selection.objects = new Object[0];
            HierarchyOptions options = new() { UseSelection = true };

            // Act
            List<HierarchyNode> nodes = service.GetHierarchyNodes(options);

            // Assert
            Assert.That(nodes.Count, Is.EqualTo(0));
        }

        [Test]
        public void GetHierarchyNodes_WithUseSelectionAndParentChildSelection_FiltersDescendants()
        {
            // Arrange
            GameObject child = new("ChildFiltered");
            child.transform.SetParent(testRoot.transform);
            Selection.objects = new Object[] { testRoot, child };

            HierarchyOptions options = new() { UseSelection = true };

            // Act
            List<HierarchyNode> nodes = service.GetHierarchyNodes(options);

            // Assert
            HierarchyNode rootNode = nodes.Find(n => n.name == testRoot.name);
            HierarchyNode childNode = nodes.Find(n => n.name == child.name);
            Assert.That(rootNode, Is.Not.Null);
            Assert.That(childNode, Is.Not.Null);
            Assert.That(rootNode.parent, Is.Null, "Root should have no parent in result");
            Assert.That(childNode.parent, Is.EqualTo(rootNode.id), "Child should be traversed as descendant of root, not as separate root");
        }
    }
}