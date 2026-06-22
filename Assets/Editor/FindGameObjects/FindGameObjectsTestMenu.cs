using UnityEditor;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Dev
{
    /// <summary>
    /// Provides Find Game Objects Test Menu behavior for Unity CLI Loop.
    /// </summary>
    public static class FindGameObjectsTestMenu
    {
        [MenuItem("UnityCliLoop/Debug/FindGameObjects Tests/Test Camera Search")]
        public static async void TestFindGameObjectsCamera()
        {
            FindGameObjectsTool tool = CreateTool();
            
            JObject parameters = new()            {
                ["RequiredComponents"] = new JArray { "Camera" },
                ["MaxResults"] = 1,
                ["IncludeInheritedProperties"] = true
            };
            
            try
            {
                UnityCliLoopToolResponse response = await tool.ExecuteAsync(parameters, System.Threading.CancellationToken.None);
                
                if (response is FindGameObjectsResponse findResponse)
                {
                    Debug.Log($"Found {findResponse.TotalFound} objects with Camera");
                    
                    foreach (FindGameObjectResult result in findResponse.Results)
                    {
                        Debug.Log($"- {result.Name}: {result.Components.Length} components");
                        
                        foreach (ComponentInfo component in result.Components)
                        {
                            if (component.Type == "Camera")
                            {
                                Debug.Log($"  Camera: {component.Properties?.Length ?? 0} properties");
                            }
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Error: {ex.Message}");
            }
        }
        
        [MenuItem("UnityCliLoop/Debug/FindGameObjects Tests/Test Main Camera by Path")]
        public static async void TestFindMainCameraByPath()
        {
            Debug.Log("[FindGameObjectsTestMenu] Starting Main Camera path search test...");
            
            FindGameObjectsTool tool = CreateTool();
            
            // Search for Main Camera by path
            JObject parameters = new()            {
                ["NamePattern"] = "Main Camera",
                ["SearchMode"] = "Path",
                ["MaxResults"] = 1
            };
            
            try
            {
                Debug.Log("[FindGameObjectsTestMenu] Executing search for Main Camera...");
                UnityCliLoopToolResponse response = await tool.ExecuteAsync(parameters, System.Threading.CancellationToken.None);
                
                if (response is FindGameObjectsResponse findResponse)
                {
                    Debug.Log($"[FindGameObjectsTestMenu] Found {findResponse.TotalFound} objects");
                    
                    foreach (FindGameObjectResult result in findResponse.Results)
                    {
                        Debug.Log($"[FindGameObjectsTestMenu] - {result.Name} at {result.Path}");
                        Debug.Log($"[FindGameObjectsTestMenu]   Components: {result.Components.Length}");
                        
                        foreach (ComponentInfo component in result.Components)
                        {
                            Debug.Log($"[FindGameObjectsTestMenu]   - {component.Type}: {component.Properties?.Length ?? 0} properties");
                        }
                    }
                }
                else
                {
                    Debug.LogError("[FindGameObjectsTestMenu] Unexpected response type");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[FindGameObjectsTestMenu] Error: {ex.Message}");
                Debug.LogError($"[FindGameObjectsTestMenu] StackTrace: {ex.StackTrace}");
            }
            
            Debug.Log("[FindGameObjectsTestMenu] Test completed");
        }

        private static FindGameObjectsTool CreateTool()
        {
            return new FindGameObjectsTool();
        }
    }
}
