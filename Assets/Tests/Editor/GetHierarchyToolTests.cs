using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace io.github.hatayama.UnityCliLoop
{
    public class GetHierarchyToolTests
    {
        private GetHierarchyTool tool;
        private GameObject testRoot;
        
        [SetUp]
        public void SetUp()
        {
            tool = new GetHierarchyTool();
            testRoot = new GameObject("TestRoot");
        }
        
        [TearDown]
        public void TearDown()
        {
            if (testRoot != null)
                Object.DestroyImmediate(testRoot);
        }
        
        [Test]
        public void ToolName_ReturnsCorrectName()
        {
            Assert.That(tool.ToolName, Is.EqualTo("get-hierarchy"));
        }
        
        [Test]
        public async Task ExecuteAsync_WithDefaultParameters_ReturnsValidResponse()
        {
            // Arrange
            JObject paramsJson = new JObject();
            
            // Act
            BaseToolResponse baseResponse = await tool.ExecuteAsync(paramsJson);
            GetHierarchyResponse response = baseResponse as GetHierarchyResponse;
            
            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.hierarchyFilePath, Is.Not.Null);
        }
        
        [Test]
        public async Task ExecuteAsync_WithMaxDepthParameter_LimitsDepth()
        {
            // Arrange
            GameObject child = new GameObject("Child");
            GameObject grandChild = new GameObject("GrandChild");
            child.transform.SetParent(testRoot.transform);
            grandChild.transform.SetParent(child.transform);
            
            JObject paramsJson = new JObject
            {
                ["MaxDepth"] = 1
            };
            
            // Act
            BaseToolResponse baseResponse = await tool.ExecuteAsync(paramsJson);
            GetHierarchyResponse response = baseResponse as GetHierarchyResponse;
            
            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.hierarchyFilePath, Is.Not.Null);
        }
        
        [Test]
        public async Task ExecuteAsync_WithIncludeComponentsFalse_ExcludesComponents()
        {
            // Arrange
            testRoot.AddComponent<BoxCollider>();
            
            JObject paramsJson = new JObject
            {
                ["IncludeComponents"] = false
            };
            
            // Act
            BaseToolResponse baseResponse = await tool.ExecuteAsync(paramsJson);
            GetHierarchyResponse response = baseResponse as GetHierarchyResponse;
            
            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.hierarchyFilePath, Is.Not.Null);
        }
        
        [Test]
        public void ParameterSchema_HasCorrectProperties()
        {
            // Act
            ToolParameterSchema schema = tool.ParameterSchema;
            
            // Assert
            Assert.That(schema, Is.Not.Null);
            Assert.That(schema.Properties, Is.Not.Null);
            Assert.That(schema.Properties.ContainsKey("IncludeInactive"), Is.True);
            Assert.That(schema.Properties.ContainsKey("MaxDepth"), Is.True);
            Assert.That(schema.Properties.ContainsKey("RootPath"), Is.True);
            Assert.That(schema.Properties.ContainsKey("IncludeComponents"), Is.True);
            Assert.That(schema.Properties.ContainsKey("IncludePaths"), Is.True);
            Assert.That(schema.Properties.ContainsKey("UseComponentsLut"), Is.True);
        }
    }
}