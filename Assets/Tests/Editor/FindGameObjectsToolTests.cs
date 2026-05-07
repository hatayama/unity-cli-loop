using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json.Linq;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies Find Game Objects Tool behavior.
    /// </summary>
    public class FindGameObjectsToolTests
    {
        private FindGameObjectsTool tool;
        private GameObject testObject1;
        private GameObject testObject2;
        private GameObject testObject3;
        
        [SetUp]
        public void SetUp()
        {
            tool = new FindGameObjectsTool();
            
            // Create test GameObjects
            testObject1 = new GameObject("TestObject1");
            testObject2 = new GameObject("TestObject2");
            testObject3 = new GameObject("AnotherObject");
        }
        
        [TearDown]
        public void TearDown()
        {
            if (testObject1 != null) Object.DestroyImmediate(testObject1);
            if (testObject2 != null) Object.DestroyImmediate(testObject2);
            if (testObject3 != null) Object.DestroyImmediate(testObject3);
        }
        
        [Test]
        public void ToolName_ReturnsCorrectName()
        {
            Assert.That(tool.ToolName, Is.EqualTo("find-game-objects"));
        }
        
        
        [Test]
        public async Task ExecuteAsync_WithNamePattern_FindsMatchingObjects()
        {
            // Arrange
            JObject paramsJson = new()            {
                ["NamePattern"] = "TestObject",
                ["SearchMode"] = "Contains"
            };
            
            // Act
            UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(paramsJson);
            FindGameObjectsResponse response = baseResponse as FindGameObjectsResponse;
            
            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.results, Is.Not.Null);
            Assert.That(response.totalFound, Is.EqualTo(2));
            Assert.That(response.results.Length, Is.EqualTo(2));
            
            // Check that both TestObject1 and TestObject2 are found
            string[] foundNames = System.Array.ConvertAll(response.results, r => r.name);
            Assert.That(foundNames, Does.Contain("TestObject1"));
            Assert.That(foundNames, Does.Contain("TestObject2"));
            Assert.That(foundNames, Does.Not.Contain("AnotherObject"));
        }
        
        [Test]
        public async Task ExecuteAsync_WithEmptyParameters_ReturnsError()
        {
            // Arrange
            JObject paramsJson = new();
            
            // Act
            UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(paramsJson);
            FindGameObjectsResponse response = baseResponse as FindGameObjectsResponse;
            
            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.totalFound, Is.EqualTo(0));
            Assert.That(response.errorMessage, Is.Not.Null);
            Assert.That(response.errorMessage, Does.Contain("At least one search criterion"));
        }
        
        [Test]
        public async Task ExecuteAsync_WithComponentSearch_FindsObjectsWithSpecificComponent()
        {
            // Arrange
            testObject1.AddComponent<BoxCollider>();
            testObject2.AddComponent<Rigidbody>();
            testObject3.AddComponent<BoxCollider>();
            
            JObject paramsJson = new()            {
                ["RequiredComponents"] = new JArray { "BoxCollider" }
            };
            
            // Act
            UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(paramsJson);
            FindGameObjectsResponse response = baseResponse as FindGameObjectsResponse;
            
            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.totalFound, Is.GreaterThanOrEqualTo(2)); // Scene might have other objects with BoxCollider
            
            string[] foundNames = System.Array.ConvertAll(response.results, r => r.name);
            Assert.That(foundNames, Does.Contain("TestObject1"));
            Assert.That(foundNames, Does.Contain("AnotherObject"));
            Assert.That(foundNames, Does.Not.Contain("TestObject2"));
        }
        
        [Test]
        public async Task ExecuteAsync_WithMultipleComponentSearch_FindsObjectsWithAllComponents()
        {
            // Arrange
            testObject1.AddComponent<BoxCollider>();
            testObject1.AddComponent<Rigidbody>();
            testObject2.AddComponent<BoxCollider>();
            testObject3.AddComponent<Rigidbody>();
            
            JObject paramsJson = new()            {
                ["RequiredComponents"] = new JArray { "BoxCollider", "Rigidbody" }
            };
            
            // Act
            UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(paramsJson);
            FindGameObjectsResponse response = baseResponse as FindGameObjectsResponse;
            
            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.totalFound, Is.EqualTo(1));
            Assert.That(response.results[0].name, Is.EqualTo("TestObject1"));
            
            // Verify components are returned
            ComponentInfo boxCollider = System.Array.Find(response.results[0].components, c => c.type == "BoxCollider");
            ComponentInfo rigidbody = System.Array.Find(response.results[0].components, c => c.type == "Rigidbody");
            Assert.That(boxCollider, Is.Not.Null);
            Assert.That(rigidbody, Is.Not.Null);
        }
        
        [Test]
        public async Task ExecuteAsync_WithTagSearch_FindsObjectsWithSpecificTag()
        {
            // Arrange
            // Using tags that don't require pre-definition in Unity
            // All GameObjects start with "Untagged" by default
            testObject1.tag = "Untagged";
            testObject2.tag = "Untagged";
            testObject3.tag = "Untagged";
            
            JObject paramsJson = new()            {
                ["NamePattern"] = "TestObject|AnotherObject",
                ["SearchMode"] = "Regex",
                ["Tag"] = "Untagged"
            };
            
            // Act
            UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(paramsJson);
            FindGameObjectsResponse response = baseResponse as FindGameObjectsResponse;
            
            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.totalFound, Is.GreaterThanOrEqualTo(3)); // At least our 3 test objects
            
            string[] foundNames = System.Array.ConvertAll(response.results, r => r.name);
            Assert.That(foundNames, Does.Contain("TestObject1"));
            Assert.That(foundNames, Does.Contain("TestObject2"));
            Assert.That(foundNames, Does.Contain("AnotherObject"));
            
            // Verify tag is returned in results
            foreach (var result in response.results)
            {
                if (result.name == "TestObject1" || result.name == "TestObject2" || result.name == "AnotherObject")
                {
                    Assert.That(result.tag, Is.EqualTo("Untagged"));
                }
            }
        }
        
        [Test]
        public async Task ExecuteAsync_WithLayerSearch_FindsObjectsOnSpecificLayer()
        {
            // Arrange
            int enemyLayer = 8; // Assuming layer 8 is "Enemy" layer
            testObject1.layer = 0; // Default layer
            testObject2.layer = enemyLayer;
            testObject3.layer = enemyLayer;
            
            JObject paramsJson = new()            {
                ["Layer"] = enemyLayer
            };
            
            // Act
            UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(paramsJson);
            FindGameObjectsResponse response = baseResponse as FindGameObjectsResponse;
            
            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.totalFound, Is.EqualTo(2));
            
            string[] foundNames = System.Array.ConvertAll(response.results, r => r.name);
            Assert.That(foundNames, Does.Contain("TestObject2"));
            Assert.That(foundNames, Does.Contain("AnotherObject"));
            Assert.That(foundNames, Does.Not.Contain("TestObject1"));
            
            // Verify layer is returned in results
            Assert.That(response.results[0].layer, Is.EqualTo(enemyLayer));
        }
        
        [Test]
        public async Task ExecuteAsync_WithRegexSearch_FindsObjectsMatchingPattern()
        {
            // Arrange
            GameObject enemy1 = new("Enemy1");
            GameObject enemy2 = new("Enemy2");
            GameObject player = new("Player1");
            
            JObject paramsJson = new()            {
                ["NamePattern"] = "Enemy\\d+",
                ["SearchMode"] = "Regex"
            };
            
            try
            {
                // Act
                UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(paramsJson);
                FindGameObjectsResponse response = baseResponse as FindGameObjectsResponse;
                
                // Assert
                Assert.That(response, Is.Not.Null);
                Assert.That(response.totalFound, Is.GreaterThanOrEqualTo(2));
                
                string[] foundNames = System.Array.ConvertAll(response.results, r => r.name);
                Assert.That(foundNames, Does.Contain("Enemy1"));
                Assert.That(foundNames, Does.Contain("Enemy2"));
                Assert.That(foundNames, Does.Not.Contain("Player1"));
            }
            finally
            {
                // Cleanup
                Object.DestroyImmediate(enemy1);
                Object.DestroyImmediate(enemy2);
                Object.DestroyImmediate(player);
            }
        }
        
        [Test]
        public async Task ExecuteAsync_WithIncludeInactive_FindsInactiveObjects()
        {
            // Arrange
            testObject1.SetActive(true);
            testObject2.SetActive(false);
            testObject3.SetActive(false);
            
            JObject paramsJson = new()            {
                ["NamePattern"] = "Object",
                ["SearchMode"] = "Contains",
                ["IncludeInactive"] = true
            };
            
            // Act
            UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(paramsJson);
            FindGameObjectsResponse response = baseResponse as FindGameObjectsResponse;
            
            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.totalFound, Is.EqualTo(3)); // Should find all 3 objects including inactive
            
            string[] foundNames = System.Array.ConvertAll(response.results, r => r.name);
            Assert.That(foundNames, Does.Contain("TestObject1"));
            Assert.That(foundNames, Does.Contain("TestObject2"));
            Assert.That(foundNames, Does.Contain("AnotherObject"));
        }
        
        [Test]
        public async Task ExecuteAsync_WithoutIncludeInactive_ExcludesInactiveObjects()
        {
            // Arrange
            testObject1.SetActive(true);
            testObject2.SetActive(false);
            testObject3.SetActive(false);
            
            JObject paramsJson = new()            {
                ["NamePattern"] = "Object",
                ["SearchMode"] = "Contains",
                ["IncludeInactive"] = false
            };
            
            // Act
            UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(paramsJson);
            FindGameObjectsResponse response = baseResponse as FindGameObjectsResponse;
            
            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.totalFound, Is.EqualTo(1)); // Should only find active object
            Assert.That(response.results[0].name, Is.EqualTo("TestObject1"));
            Assert.That(response.results[0].isActive, Is.True);
        }
        
        [Test]
        public async Task ExecuteAsync_WithComplexSearch_CombinesMultipleCriteria()
        {
            // Arrange
            testObject1.AddComponent<BoxCollider>();
            testObject1.layer = 0;
            testObject2.AddComponent<BoxCollider>();
            testObject2.layer = 8;
            testObject3.layer = 8;
            
            JObject paramsJson = new()            {
                ["NamePattern"] = "Object",
                ["SearchMode"] = "Contains",
                ["RequiredComponents"] = new JArray { "BoxCollider" },
                ["Layer"] = 8
            };
            
            // Act
            UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(paramsJson);
            FindGameObjectsResponse response = baseResponse as FindGameObjectsResponse;
            
            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.totalFound, Is.EqualTo(1)); // Only TestObject2 matches all criteria
            Assert.That(response.results[0].name, Is.EqualTo("TestObject2"));
            
            // Verify all criteria are met
            ComponentInfo boxCollider = System.Array.Find(response.results[0].components, c => c.type == "BoxCollider");
            Assert.That(boxCollider, Is.Not.Null);
            Assert.That(response.results[0].layer, Is.EqualTo(8));
        }
        
        [Test]
        public async Task ExecuteAsync_WithMaxResults_LimitsReturnedObjects()
        {
            // Arrange
            // Create many GameObjects
            GameObject[] manyObjects = new GameObject[20];
            for (int i = 0; i < 20; i++)
            {
                manyObjects[i] = new GameObject($"ManyObject{i}");
            }
            
            JObject paramsJson = new()            {
                ["NamePattern"] = "ManyObject",
                ["SearchMode"] = "Contains",
                ["MaxResults"] = 5
            };
            
            try
            {
                // Act
                UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(paramsJson);
                FindGameObjectsResponse response = baseResponse as FindGameObjectsResponse;
                
                // Assert
                Assert.That(response, Is.Not.Null);
                Assert.That(response.results.Length, Is.EqualTo(5)); // Should be limited to 5
                Assert.That(response.totalFound, Is.EqualTo(5)); // Total found should also be 5
                
                // Verify all results match the pattern
                foreach (var result in response.results)
                {
                    Assert.That(result.name, Does.StartWith("ManyObject"));
                }
            }
            finally
            {
                // Cleanup
                foreach (var obj in manyObjects)
                {
                    if (obj != null) Object.DestroyImmediate(obj);
                }
            }
        }
        
        [Test]
        public async Task ExecuteAsync_WithPathSearchMode_FindsObjectByHierarchyPath()
        {
            // Arrange
            GameObject parent = new("Parent");
            GameObject child = new("Child");
            GameObject grandchild = new("Grandchild");
            child.transform.SetParent(parent.transform);
            grandchild.transform.SetParent(child.transform);
            
            JObject paramsJson = new()            {
                ["NamePattern"] = "Parent/Child/Grandchild",
                ["SearchMode"] = "Path"
            };
            
            try
            {
                // Act
                UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(paramsJson);
                FindGameObjectsResponse response = baseResponse as FindGameObjectsResponse;
                
                // Assert
                Assert.That(response, Is.Not.Null);
                Assert.That(response.totalFound, Is.EqualTo(1));
                Assert.That(response.results[0].name, Is.EqualTo("Grandchild"));
                Assert.That(response.results[0].path, Is.EqualTo("Parent/Child/Grandchild"));
            }
            finally
            {
                // Cleanup
                Object.DestroyImmediate(parent);
            }
        }
        
        [Test]
        public async Task ExecuteAsync_WithExactSearchMode_FindsExactNameMatch()
        {
            // Arrange
            GameObject exact = new("ExactName");
            GameObject partial = new("ExactNamePart");
            GameObject different = new("DifferentName");
            
            JObject paramsJson = new()            {
                ["NamePattern"] = "ExactName",
                ["SearchMode"] = "Exact"
            };
            
            try
            {
                // Act
                UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(paramsJson);
                FindGameObjectsResponse response = baseResponse as FindGameObjectsResponse;
                
                // Assert
                Assert.That(response, Is.Not.Null);
                Assert.That(response.totalFound, Is.EqualTo(1));
                Assert.That(response.results[0].name, Is.EqualTo("ExactName"));
            }
            finally
            {
                // Cleanup
                Object.DestroyImmediate(exact);
                Object.DestroyImmediate(partial);
                Object.DestroyImmediate(different);
            }
        }
        
        [Test]
        public async Task ExecuteAsync_WithContainsSearchMode_FindsPartialMatch()
        {
            // Arrange
            GameObject obj1 = new("TestObjectOne");
            GameObject obj2 = new("AnotherTestObjectTwo");
            GameObject obj3 = new("DifferentName");

            JObject paramsJson = new()            {
                ["NamePattern"] = "TestObject",
                ["SearchMode"] = "Contains"
            };

            try
            {
                // Act
                UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(paramsJson);
                FindGameObjectsResponse response = baseResponse as FindGameObjectsResponse;

                // Assert
                Assert.That(response, Is.Not.Null);
                Assert.That(response.totalFound, Is.EqualTo(4)); // Includes SetUp objects (TestObject1, TestObject2)

                string[] foundNames = System.Array.ConvertAll(response.results, r => r.name);
                Assert.That(foundNames, Does.Contain("TestObjectOne"));
                Assert.That(foundNames, Does.Contain("AnotherTestObjectTwo"));
                Assert.That(foundNames, Does.Not.Contain("DifferentName"));
            }
            finally
            {
                // Cleanup
                Object.DestroyImmediate(obj1);
                Object.DestroyImmediate(obj2);
                Object.DestroyImmediate(obj3);
            }
        }

        [Test]
        public async Task ExecuteAsync_WithSelectedMode_NoSelection_ReturnsEmptyResult()
        {
            // Arrange
            Selection.objects = new Object[0];

            JObject paramsJson = new()            {
                ["SearchMode"] = "Selected"
            };

            // Act
            UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(paramsJson);
            FindGameObjectsResponse response = baseResponse as FindGameObjectsResponse;

            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.totalFound, Is.EqualTo(0));
            Assert.That(response.results, Is.Empty);
            Assert.That(response.message, Does.Contain("No GameObjects"));
        }

        [Test]
        public async Task ExecuteAsync_WithSelectedMode_SingleSelection_ReturnsJsonDirectly()
        {
            // Arrange
            Object[] previousSelection = Selection.objects;
            Selection.objects = new Object[] { testObject1 };

            JObject paramsJson = new()            {
                ["SearchMode"] = "Selected"
            };

            try
            {
                // Act
                UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(paramsJson);
                FindGameObjectsResponse response = baseResponse as FindGameObjectsResponse;

                // Assert
                Assert.That(response, Is.Not.Null);
                Assert.That(response.totalFound, Is.EqualTo(1));
                Assert.That(response.results, Is.Not.Null);
                Assert.That(response.results.Length, Is.EqualTo(1));
                Assert.That(response.results[0].name, Is.EqualTo("TestObject1"));
                Assert.That(response.resultsFilePath, Is.Null);
            }
            finally
            {
                Selection.objects = previousSelection;
            }
        }

        [Test]
        public async Task ExecuteAsync_WithSelectedMode_MultipleSelection_ExportsToFile()
        {
            // Arrange
            Selection.objects = new Object[] { testObject1, testObject2 };

            JObject paramsJson = new()            {
                ["SearchMode"] = "Selected"
            };

            try
            {
                // Act
                UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(paramsJson);
                FindGameObjectsResponse response = baseResponse as FindGameObjectsResponse;

                // Assert
                Assert.That(response, Is.Not.Null);
                Assert.That(response.totalFound, Is.EqualTo(2));
                Assert.That(response.resultsFilePath, Is.Not.Null);
                Assert.That(response.resultsFilePath, Does.Contain("FindGameObjectsResults"));
                Assert.That(response.message, Does.Contain("Multiple objects selected"));

                // Verify file exists
                string fullPath = Path.Combine(UnityEngine.Application.dataPath, "..", response.resultsFilePath);
                Assert.That(File.Exists(fullPath), Is.True, $"Export file should exist at {fullPath}");

                // Cleanup exported file
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                }
            }
            finally
            {
                Selection.objects = new Object[0];
            }
        }

        [Test]
        public async Task ExecuteAsync_WithSelectedMode_IncludeInactiveFalse_ExcludesInactiveObjects()
        {
            // Arrange
            testObject1.SetActive(true);
            testObject2.SetActive(false);
            Selection.objects = new Object[] { testObject1, testObject2 };

            JObject paramsJson = new()            {
                ["SearchMode"] = "Selected",
                ["IncludeInactive"] = false
            };

            try
            {
                // Act
                UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(paramsJson);
                FindGameObjectsResponse response = baseResponse as FindGameObjectsResponse;

                // Assert
                Assert.That(response, Is.Not.Null);
                Assert.That(response.totalFound, Is.EqualTo(1));
                Assert.That(response.results, Is.Not.Null);
                Assert.That(response.results.Length, Is.EqualTo(1));
                Assert.That(response.results[0].name, Is.EqualTo("TestObject1"));
            }
            finally
            {
                testObject1.SetActive(true);
                testObject2.SetActive(true);
                Selection.objects = new Object[0];
            }
        }

        [Test]
        public async Task ExecuteAsync_ReturnsObjectReferenceProperties()
        {
            // Arrange
            GameObject anchorTarget = new("AnchorTarget");
            MeshRenderer renderer = testObject1.AddComponent<MeshRenderer>();
            renderer.probeAnchor = anchorTarget.transform;

            JObject paramsJson = new()            {
                ["NamePattern"] = "TestObject1",
                ["SearchMode"] = "Exact"
            };

            try
            {
                // Act
                UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(paramsJson);
                FindGameObjectsResponse response = baseResponse as FindGameObjectsResponse;

                // Assert
                Assert.That(response, Is.Not.Null);
                Assert.That(response.totalFound, Is.EqualTo(1));

                ComponentInfo meshRenderer = System.Array.Find(
                    response.results[0].components, c => c.type == "MeshRenderer");
                Assert.That(meshRenderer, Is.Not.Null);

                ComponentPropertyInfo probeAnchor = System.Array.Find(
                    meshRenderer.properties, p => p.name == "Probe Anchor");
                Assert.That(probeAnchor, Is.Not.Null, "MeshRenderer should have Probe Anchor property");
                Assert.That(probeAnchor.type, Is.EqualTo("ObjectReference"));

                // Value should be a structured object with name, type, instanceId
                JObject valueObj = JObject.FromObject(probeAnchor.value);
                Assert.That(valueObj["name"].ToString(), Is.EqualTo("AnchorTarget"));
                Assert.That(valueObj["type"].ToString(), Is.EqualTo("Transform"));
                Assert.That(valueObj["instanceId"].Value<int>(), Is.EqualTo(anchorTarget.transform.GetInstanceID()));
            }
            finally
            {
                Object.DestroyImmediate(anchorTarget);
            }
        }

        [Test]
        public async Task ExecuteAsync_ReturnsNoneForUnsetObjectReference()
        {
            // Arrange
            testObject1.AddComponent<MeshRenderer>();

            JObject paramsJson = new()            {
                ["NamePattern"] = "TestObject1",
                ["SearchMode"] = "Exact"
            };

            // Act
            UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(paramsJson);
            FindGameObjectsResponse response = baseResponse as FindGameObjectsResponse;

            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.totalFound, Is.EqualTo(1));

            ComponentInfo meshRenderer = System.Array.Find(
                response.results[0].components, c => c.type == "MeshRenderer");
            Assert.That(meshRenderer, Is.Not.Null);

            ComponentPropertyInfo probeAnchor = System.Array.Find(
                meshRenderer.properties, p => p.name == "Probe Anchor");
            Assert.That(probeAnchor, Is.Not.Null, "MeshRenderer should have Probe Anchor property");

            JObject valueObj = JObject.FromObject(probeAnchor.value);
            Assert.That(valueObj["name"].ToString(), Is.EqualTo("None"));
            Assert.That(valueObj["type"].ToString(), Is.EqualTo("None"));
            Assert.That(valueObj["instanceId"].Value<int>(), Is.EqualTo(0));
        }

        [Test]
        public async Task ExecuteAsync_WithSelectedMode_IncludeInactiveTrue_IncludesInactiveObjects()
        {
            // Arrange
            testObject1.SetActive(true);
            testObject2.SetActive(false);
            Selection.objects = new Object[] { testObject1, testObject2 };

            JObject paramsJson = new()            {
                ["SearchMode"] = "Selected",
                ["IncludeInactive"] = true
            };

            try
            {
                // Act
                UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(paramsJson);
                FindGameObjectsResponse response = baseResponse as FindGameObjectsResponse;

                // Assert
                Assert.That(response, Is.Not.Null);
                Assert.That(response.totalFound, Is.EqualTo(2));
                Assert.That(response.resultsFilePath, Is.Not.Null); // Multiple selection exports to file

                // Cleanup exported file
                string fullPath = Path.Combine(UnityEngine.Application.dataPath, "..", response.resultsFilePath);
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                }
            }
            finally
            {
                testObject1.SetActive(true);
                testObject2.SetActive(true);
                Selection.objects = new Object[0];
            }
        }
    }
}
