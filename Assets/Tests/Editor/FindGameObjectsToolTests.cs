using System.Globalization;
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
            UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(paramsJson, System.Threading.CancellationToken.None);
            FindGameObjectsResponse response = baseResponse as FindGameObjectsResponse;
            
            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results, Is.Not.Null);
            Assert.That(response.TotalFound, Is.EqualTo(2));
            Assert.That(response.Results.Length, Is.EqualTo(2));
            
            // Check that both TestObject1 and TestObject2 are found
            string[] foundNames = System.Array.ConvertAll(response.Results, r => r.Name);
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
            UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(paramsJson, System.Threading.CancellationToken.None);
            FindGameObjectsResponse response = baseResponse as FindGameObjectsResponse;
            
            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.TotalFound, Is.EqualTo(0));
            Assert.That(response.ErrorMessage, Is.Not.Null);
            Assert.That(response.ErrorMessage, Does.Contain("At least one search criterion"));
        }

        [Test]
        public async Task ExecuteAsync_WithExactModeZeroHits_ReturnsPartialMatchModeHint()
        {
            // Verifies a zero-hit Exact-mode name search returns a hint pointing at
            // Contains/Regex, since Exact is the default and silently misses partial matches.
            JObject paramsJson = new()            {
                ["NamePattern"] = "Camera",
                ["SearchMode"] = "Exact"
            };

            UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(paramsJson, System.Threading.CancellationToken.None);
            FindGameObjectsResponse response = baseResponse as FindGameObjectsResponse;

            Assert.That(response, Is.Not.Null);
            Assert.That(response.TotalFound, Is.EqualTo(0));
            Assert.That(response.Message, Is.Not.Null);
            Assert.That(response.Message, Does.Contain("Exact match found nothing"));
            Assert.That(response.Message, Does.Contain("--search-mode Contains"));
        }

        [Test]
        public async Task ExecuteAsync_WithContainsModeZeroHits_DoesNotReturnExactModeHint()
        {
            // Verifies the Exact-mode hint is scoped to Exact mode only, since Contains/Regex
            // already support partial matching and have no equivalent trap to warn about.
            JObject paramsJson = new()            {
                ["NamePattern"] = "NoSuchObjectNameAtAll",
                ["SearchMode"] = "Contains"
            };

            UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(paramsJson, System.Threading.CancellationToken.None);
            FindGameObjectsResponse response = baseResponse as FindGameObjectsResponse;

            Assert.That(response, Is.Not.Null);
            Assert.That(response.TotalFound, Is.EqualTo(0));
            Assert.That(response.Message, Is.Null);
        }

        [Test]
        public async Task ExecuteAsync_WithExactModeNonZeroHits_DoesNotReturnHint()
        {
            // Verifies the hint only appears on a zero-hit result, not alongside real matches.
            JObject paramsJson = new()            {
                ["NamePattern"] = "TestObject1",
                ["SearchMode"] = "Exact"
            };

            UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(paramsJson, System.Threading.CancellationToken.None);
            FindGameObjectsResponse response = baseResponse as FindGameObjectsResponse;

            Assert.That(response, Is.Not.Null);
            Assert.That(response.TotalFound, Is.EqualTo(1));
            Assert.That(response.Message, Is.Null);
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
            UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(paramsJson, System.Threading.CancellationToken.None);
            FindGameObjectsResponse response = baseResponse as FindGameObjectsResponse;
            
            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.TotalFound, Is.GreaterThanOrEqualTo(2)); // Scene might have other objects with BoxCollider
            
            string[] foundNames = System.Array.ConvertAll(response.Results, r => r.Name);
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
            UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(paramsJson, System.Threading.CancellationToken.None);
            FindGameObjectsResponse response = baseResponse as FindGameObjectsResponse;
            
            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.TotalFound, Is.EqualTo(1));
            Assert.That(response.Results[0].Name, Is.EqualTo("TestObject1"));
            
            // Verify components are returned
            ComponentInfo boxCollider = System.Array.Find(response.Results[0].Components, c => c.Type == "BoxCollider");
            ComponentInfo rigidbody = System.Array.Find(response.Results[0].Components, c => c.Type == "Rigidbody");
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
            UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(paramsJson, System.Threading.CancellationToken.None);
            FindGameObjectsResponse response = baseResponse as FindGameObjectsResponse;
            
            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.TotalFound, Is.GreaterThanOrEqualTo(3)); // At least our 3 test objects
            
            string[] foundNames = System.Array.ConvertAll(response.Results, r => r.Name);
            Assert.That(foundNames, Does.Contain("TestObject1"));
            Assert.That(foundNames, Does.Contain("TestObject2"));
            Assert.That(foundNames, Does.Contain("AnotherObject"));
            
            // Verify tag is returned in results
            foreach (var result in response.Results)
            {
                if (result.Name == "TestObject1" || result.Name == "TestObject2" || result.Name == "AnotherObject")
                {
                    Assert.That(result.Tag, Is.EqualTo("Untagged"));
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
            UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(paramsJson, System.Threading.CancellationToken.None);
            FindGameObjectsResponse response = baseResponse as FindGameObjectsResponse;
            
            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.TotalFound, Is.EqualTo(2));
            
            string[] foundNames = System.Array.ConvertAll(response.Results, r => r.Name);
            Assert.That(foundNames, Does.Contain("TestObject2"));
            Assert.That(foundNames, Does.Contain("AnotherObject"));
            Assert.That(foundNames, Does.Not.Contain("TestObject1"));
            
            // Verify layer is returned in results
            Assert.That(response.Results[0].Layer, Is.EqualTo(enemyLayer));
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
                UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(paramsJson, System.Threading.CancellationToken.None);
                FindGameObjectsResponse response = baseResponse as FindGameObjectsResponse;
                
                // Assert
                Assert.That(response, Is.Not.Null);
                Assert.That(response.TotalFound, Is.GreaterThanOrEqualTo(2));
                
                string[] foundNames = System.Array.ConvertAll(response.Results, r => r.Name);
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
            UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(paramsJson, System.Threading.CancellationToken.None);
            FindGameObjectsResponse response = baseResponse as FindGameObjectsResponse;
            
            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.TotalFound, Is.EqualTo(3)); // Should find all 3 objects including inactive
            
            string[] foundNames = System.Array.ConvertAll(response.Results, r => r.Name);
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
            UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(paramsJson, System.Threading.CancellationToken.None);
            FindGameObjectsResponse response = baseResponse as FindGameObjectsResponse;
            
            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.TotalFound, Is.EqualTo(1)); // Should only find active object
            Assert.That(response.Results[0].Name, Is.EqualTo("TestObject1"));
            Assert.That(response.Results[0].IsActive, Is.True);
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
            UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(paramsJson, System.Threading.CancellationToken.None);
            FindGameObjectsResponse response = baseResponse as FindGameObjectsResponse;
            
            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.TotalFound, Is.EqualTo(1)); // Only TestObject2 matches all criteria
            Assert.That(response.Results[0].Name, Is.EqualTo("TestObject2"));
            
            // Verify all criteria are met
            ComponentInfo boxCollider = System.Array.Find(response.Results[0].Components, c => c.Type == "BoxCollider");
            Assert.That(boxCollider, Is.Not.Null);
            Assert.That(response.Results[0].Layer, Is.EqualTo(8));
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
                UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(paramsJson, System.Threading.CancellationToken.None);
                FindGameObjectsResponse response = baseResponse as FindGameObjectsResponse;
                
                // Assert
                Assert.That(response, Is.Not.Null);
                Assert.That(response.Results.Length, Is.EqualTo(5)); // Should be limited to 5
                Assert.That(response.TotalFound, Is.EqualTo(5)); // Total found should also be 5
                
                // Verify all results match the pattern
                foreach (var result in response.Results)
                {
                    Assert.That(result.Name, Does.StartWith("ManyObject"));
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
                UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(paramsJson, System.Threading.CancellationToken.None);
                FindGameObjectsResponse response = baseResponse as FindGameObjectsResponse;
                
                // Assert
                Assert.That(response, Is.Not.Null);
                Assert.That(response.TotalFound, Is.EqualTo(1));
                Assert.That(response.Results[0].Name, Is.EqualTo("Grandchild"));
                Assert.That(response.Results[0].Path, Is.EqualTo("Parent/Child/Grandchild"));
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
                UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(paramsJson, System.Threading.CancellationToken.None);
                FindGameObjectsResponse response = baseResponse as FindGameObjectsResponse;
                
                // Assert
                Assert.That(response, Is.Not.Null);
                Assert.That(response.TotalFound, Is.EqualTo(1));
                Assert.That(response.Results[0].Name, Is.EqualTo("ExactName"));
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
                UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(paramsJson, System.Threading.CancellationToken.None);
                FindGameObjectsResponse response = baseResponse as FindGameObjectsResponse;

                // Assert
                Assert.That(response, Is.Not.Null);
                Assert.That(response.TotalFound, Is.EqualTo(4)); // Includes SetUp objects (TestObject1, TestObject2)

                string[] foundNames = System.Array.ConvertAll(response.Results, r => r.Name);
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
            UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(paramsJson, System.Threading.CancellationToken.None);
            FindGameObjectsResponse response = baseResponse as FindGameObjectsResponse;

            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.TotalFound, Is.EqualTo(0));
            Assert.That(response.Results, Is.Empty);
            Assert.That(response.Message, Does.Contain("No GameObjects"));
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
                UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(paramsJson, System.Threading.CancellationToken.None);
                FindGameObjectsResponse response = baseResponse as FindGameObjectsResponse;

                // Assert
                Assert.That(response, Is.Not.Null);
                Assert.That(response.TotalFound, Is.EqualTo(1));
                Assert.That(response.Results, Is.Not.Null);
                Assert.That(response.Results.Length, Is.EqualTo(1));
                Assert.That(response.Results[0].Name, Is.EqualTo("TestObject1"));
                Assert.That(response.ResultsFilePath, Is.Null);
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
                UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(paramsJson, System.Threading.CancellationToken.None);
                FindGameObjectsResponse response = baseResponse as FindGameObjectsResponse;

                // Assert
                Assert.That(response, Is.Not.Null);
                Assert.That(response.TotalFound, Is.EqualTo(2));
                Assert.That(response.ResultsFilePath, Is.Not.Null);
                Assert.That(response.ResultsFilePath, Does.Contain("FindGameObjectsResults"));
                Assert.That(response.Message, Does.Contain("Multiple objects selected"));

                // Verify file exists
                string fullPath = Path.Combine(UnityEngine.Application.dataPath, "..", response.ResultsFilePath);
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
                UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(paramsJson, System.Threading.CancellationToken.None);
                FindGameObjectsResponse response = baseResponse as FindGameObjectsResponse;

                // Assert
                Assert.That(response, Is.Not.Null);
                Assert.That(response.TotalFound, Is.EqualTo(1));
                Assert.That(response.Results, Is.Not.Null);
                Assert.That(response.Results.Length, Is.EqualTo(1));
                Assert.That(response.Results[0].Name, Is.EqualTo("TestObject1"));
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
                UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(paramsJson, System.Threading.CancellationToken.None);
                FindGameObjectsResponse response = baseResponse as FindGameObjectsResponse;

                // Assert
                Assert.That(response, Is.Not.Null);
                Assert.That(response.TotalFound, Is.EqualTo(1));

                ComponentInfo meshRenderer = System.Array.Find(
                    response.Results[0].Components, c => c.Type == "MeshRenderer");
                Assert.That(meshRenderer, Is.Not.Null);

                ComponentPropertyInfo probeAnchor = System.Array.Find(
                    meshRenderer.Properties, p => p.Name == "Probe Anchor");
                Assert.That(probeAnchor, Is.Not.Null, "MeshRenderer should have Probe Anchor property");
                Assert.That(probeAnchor.Type, Is.EqualTo("ObjectReference"));

                string expectedEntityId = GetExpectedObjectId(anchorTarget.transform);

                // Value should be a structured object with name, type, entityId
                JObject valueObj = JObject.FromObject(probeAnchor.Value);
                Assert.That(valueObj["name"].ToString(), Is.EqualTo("AnchorTarget"));
                Assert.That(valueObj["type"].ToString(), Is.EqualTo("Transform"));
                Assert.That(valueObj["entityId"].ToString(), Is.EqualTo(expectedEntityId));
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
            UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(paramsJson, System.Threading.CancellationToken.None);
            FindGameObjectsResponse response = baseResponse as FindGameObjectsResponse;

            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.TotalFound, Is.EqualTo(1));

            ComponentInfo meshRenderer = System.Array.Find(
                response.Results[0].Components, c => c.Type == "MeshRenderer");
            Assert.That(meshRenderer, Is.Not.Null);

            ComponentPropertyInfo probeAnchor = System.Array.Find(
                meshRenderer.Properties, p => p.Name == "Probe Anchor");
            Assert.That(probeAnchor, Is.Not.Null, "MeshRenderer should have Probe Anchor property");

            JObject valueObj = JObject.FromObject(probeAnchor.Value);
            Assert.That(valueObj["name"].ToString(), Is.EqualTo("None"));
            Assert.That(valueObj["type"].ToString(), Is.EqualTo("None"));
            Assert.That(valueObj["entityId"].ToString(), Is.EqualTo("0"));
        }

        private static string GetExpectedObjectId(Object obj)
        {
            UnityEngine.Debug.Assert(obj != null, "Unity Object must exist before reading its identifier.");

#if UNITY_6000_4_OR_NEWER
            return obj.GetEntityId().ToString();
#else
            int instanceId = obj.GetInstanceID();
            return instanceId.ToString(CultureInfo.InvariantCulture);
#endif
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
                UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(paramsJson, System.Threading.CancellationToken.None);
                FindGameObjectsResponse response = baseResponse as FindGameObjectsResponse;

                // Assert
                Assert.That(response, Is.Not.Null);
                Assert.That(response.TotalFound, Is.EqualTo(2));
                Assert.That(response.ResultsFilePath, Is.Not.Null); // Multiple selection exports to file

                // Cleanup exported file
                string fullPath = Path.Combine(UnityEngine.Application.dataPath, "..", response.ResultsFilePath);
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
