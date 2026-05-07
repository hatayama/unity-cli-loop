using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies Get Hierarchy Tool behavior.
    /// </summary>
    public class GetHierarchyToolTests
    {
        private GetHierarchyTool _tool;
        
        [SetUp]
        public void SetUp()
        {
            _tool = new GetHierarchyTool();
        }
        
        [Test]
        public void ToolName_ReturnsCorrectName()
        {
            // Tests that the bundled hierarchy tool keeps the CLI command name stable.
            Assert.That(_tool.ToolName, Is.EqualTo("get-hierarchy"));
        }
        
        [Test]
        public async Task ExecuteAsync_WithDefaultParameters_ReturnsHierarchyExport()
        {
            // Tests that the bundled hierarchy tool executes without host-service injection.
            JObject parameters = new();
            
            UnityCliLoopToolResponse baseResponse = await _tool.ExecuteAsync(parameters);
            GetHierarchyResponse response = baseResponse as GetHierarchyResponse;
            
            Assert.That(response, Is.Not.Null);
            Assert.That(response.hierarchyFilePath, Is.Not.Empty);
            Assert.That(response.message, Does.Contain("Hierarchy data saved"));
            DeleteExportedFile(response.hierarchyFilePath);
        }
        
        [Test]
        public async Task ExecuteAsync_WithMaxDepthParameter_MapsRequest()
        {
            // Tests that MaxDepth is accepted by the self-contained first-party tool.
            JObject parameters = new()            {
                ["MaxDepth"] = 1
            };
            
            UnityCliLoopToolResponse baseResponse = await _tool.ExecuteAsync(parameters);
            GetHierarchyResponse response = baseResponse as GetHierarchyResponse;
            
            Assert.That(response, Is.Not.Null);
            DeleteExportedFile(response.hierarchyFilePath);
        }
        
        [Test]
        public async Task ExecuteAsync_WithIncludeComponentsFalse_MapsRequest()
        {
            // Tests that component inclusion is accepted by the self-contained first-party tool.
            JObject parameters = new()            {
                ["IncludeComponents"] = false
            };
            
            UnityCliLoopToolResponse baseResponse = await _tool.ExecuteAsync(parameters);
            GetHierarchyResponse response = baseResponse as GetHierarchyResponse;
            
            Assert.That(response, Is.Not.Null);
            DeleteExportedFile(response.hierarchyFilePath);
        }
        
        [Test]
        public void ParameterSchema_HasCorrectProperties()
        {
            // Tests that moving the tool assembly does not change the public parameter schema.
            ToolParameterSchema schema = _tool.ParameterSchema;
            
            Assert.That(schema, Is.Not.Null);
            Assert.That(schema.Properties, Is.Not.Null);
            Assert.That(schema.Properties.ContainsKey("IncludeInactive"), Is.True);
            Assert.That(schema.Properties.ContainsKey("MaxDepth"), Is.True);
            Assert.That(schema.Properties.ContainsKey("RootPath"), Is.True);
            Assert.That(schema.Properties.ContainsKey("IncludeComponents"), Is.True);
            Assert.That(schema.Properties.ContainsKey("IncludePaths"), Is.True);
            Assert.That(schema.Properties.ContainsKey("UseComponentsLut"), Is.True);
        }

        private static void DeleteExportedFile(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
            {
                return;
            }

            string absolutePath = Path.Combine(UnityCliLoopPathResolver.GetProjectRoot(), relativePath);
            if (File.Exists(absolutePath))
            {
                File.Delete(absolutePath);
            }
        }
    }
}
