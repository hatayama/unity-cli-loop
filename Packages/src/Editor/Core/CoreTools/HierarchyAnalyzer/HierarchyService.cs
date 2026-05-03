using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Service for retrieving Unity Hierarchy information
    /// Reusable logic separated from command implementation
    /// </summary>
    public class HierarchyService
    {
        /// <summary>
        /// Get all hierarchy nodes based on options
        /// </summary>
        public List<HierarchyNode> GetHierarchyNodes(HierarchyOptions options)
        {
            List<HierarchyNode> nodes = new List<HierarchyNode>();
            GameObject[] rootObjects = options.UseSelection
                ? GetSelectedRootGameObjects()
                : GetRootGameObjects(options.RootPath);

            foreach (GameObject root in rootObjects)
            {
                if (!options.IncludeInactive && !root.activeInHierarchy)
                    continue;

                TraverseHierarchy(root, null, 0, options, nodes);
            }

            return nodes;
        }

        /// <summary>
        /// Get selected GameObjects as roots, filtering out descendants
        /// to avoid duplicate traversal when parent and child are both selected.
        /// </summary>
        private GameObject[] GetSelectedRootGameObjects()
        {
            GameObject[] selected = Selection.gameObjects;
            if (selected == null || selected.Length == 0)
            {
                return System.Array.Empty<GameObject>();
            }

            List<GameObject> roots = new List<GameObject>();
            foreach (GameObject obj in selected)
            {
                if (obj == null)
                {
                    continue;
                }

                bool isDescendantOfAnotherSelected = false;
                foreach (GameObject other in selected)
                {
                    if (other == null || other == obj)
                    {
                        continue;
                    }

                    if (obj.transform.IsChildOf(other.transform))
                    {
                        isDescendantOfAnotherSelected = true;
                        break;
                    }
                }

                if (!isDescendantOfAnotherSelected)
                {
                    roots.Add(obj);
                }
            }

            return roots.ToArray();
        }
        
        /// <summary>
        /// Get current scene context information
        /// </summary>
        public HierarchyContext GetCurrentContext()
        {
            string sceneType = "editor";
            string sceneName = "Unknown";
            
            // Check if in Prefab Edit Mode
            PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null)
            {
                sceneType = "prefab";
                sceneName = prefabStage.assetPath;
            }
            else if (Application.isPlaying)
            {
                sceneType = "runtime";
                sceneName = BuildSceneNameSummary();
            }
            else
            {
                sceneType = "editor";
                sceneName = BuildSceneNameSummary();
            }
            
            return new HierarchyContext(sceneType, sceneName, 0, 0);
        }

        private string BuildSceneNameSummary()
        {
            int count = SceneManager.sceneCount;
            if (count <= 0)
            {
                return string.Empty;
            }

            System.Collections.Generic.List<string> names = new System.Collections.Generic.List<string>();
            for (int i = 0; i < count; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded)
                {
                    names.Add(scene.name);
                }
            }

            GameObject[] ddolRoots = GetDontDestroyOnLoadRootObjects();
            if (ddolRoots.Length > 0 && !names.Contains("DontDestroyOnLoad"))
            {
                names.Add("DontDestroyOnLoad");
            }

            if (names.Count <= 1)
            {
                return names.Count == 1 ? names[0] : string.Empty;
            }

            string joined = string.Join(", ", names.ToArray());
            return $"Multiple({names.Count}): {joined}";
        }
        
        private GameObject[] GetRootGameObjects(string rootPath)
        {
            // Check if in Prefab Edit Mode
            PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null)
            {
                GameObject prefabRoot = prefabStage.prefabContentsRoot;
                if (!string.IsNullOrEmpty(rootPath))
                {
                    if (prefabRoot.name == rootPath)
                    {
                        return new[] { prefabRoot };
                    }

                    string localPath = NormalizeRootRelativePath(rootPath, prefabRoot.name);
                    Transform found = string.IsNullOrEmpty(localPath)
                        ? prefabRoot.transform
                        : prefabRoot.transform.Find(localPath);
                    if (found != null)
                    {
                        return new[] { found.gameObject };
                    }

                    return System.Array.Empty<GameObject>();
                }
                return new[] { prefabRoot };
            }
            
            // Normal scene mode: iterate all loaded scenes (additive included)
            List<GameObject> results = new List<GameObject>();
            GameObject[] ddolRoots = GetDontDestroyOnLoadRootObjects();

            int sceneCount = SceneManager.sceneCount;
            if (!string.IsNullOrEmpty(rootPath))
            {
                for (int i = 0; i < sceneCount; i++)
                {
                    Scene scene = SceneManager.GetSceneAt(i);
                    if (!scene.isLoaded)
                    {
                        continue;
                    }

                    GameObject[] roots = scene.GetRootGameObjects();
                    AppendMatchesForSceneRoot(results, roots, rootPath);
                }

                if (ddolRoots.Length > 0)
                {
                    AppendMatchesForSceneRoot(results, ddolRoots, rootPath);
                }

                return results.ToArray();
            }

            for (int i = 0; i < sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                {
                    continue;
                }

                GameObject[] roots = scene.GetRootGameObjects();
                results.AddRange(roots);
            }

            if (ddolRoots.Length > 0)
            {
                results.AddRange(ddolRoots);
            }

            return results.ToArray();
        }

        private static string NormalizeRootRelativePath(string rootPath, string rootName)
        {
            if (string.IsNullOrEmpty(rootPath))
            {
                return string.Empty;
            }

            string trimmed = rootPath.TrimStart('/');
            if (string.IsNullOrEmpty(trimmed))
            {
                return string.Empty;
            }

            if (trimmed.StartsWith(rootName + "/"))
            {
                return trimmed.Substring(rootName.Length + 1);
            }

            if (trimmed == rootName)
            {
                return string.Empty;
            }

            return trimmed;
        }
        
        private static void AppendMatchesForSceneRoot(System.Collections.Generic.List<GameObject> results, GameObject[] roots, string rootPath)
        {
            if (roots == null)
            {
                return;
            }

            for (int i = 0; i < roots.Length; i++)
            {
                GameObject root = roots[i];
                if (root == null)
                {
                    continue;
                }

                if (root.name == rootPath)
                {
                    results.Add(root);
                    continue;
                }

                string localPath = NormalizeRootRelativePath(rootPath, root.name);
                if (string.IsNullOrEmpty(localPath))
                {
                    results.Add(root);
                    continue;
                }

                Transform found = root.transform.Find(localPath);
                if (found != null)
                {
                    results.Add(found.gameObject);
                }
            }
        }

        private static GameObject[] GetDontDestroyOnLoadRootObjects()
        {
            if (!Application.isPlaying)
            {
                return System.Array.Empty<GameObject>();
            }

            GameObject probe = null;
            try
            {
                probe = new GameObject("__mcp_ddol_probe__");
                UnityEngine.Object.DontDestroyOnLoad(probe);

                Scene ddolScene = probe.scene;
                if (!ddolScene.IsValid())
                {
                    return System.Array.Empty<GameObject>();
                }

                GameObject[] roots = ddolScene.GetRootGameObjects();
                if (roots == null || roots.Length == 0)
                {
                    return System.Array.Empty<GameObject>();
                }

                System.Collections.Generic.List<GameObject> filtered = new System.Collections.Generic.List<GameObject>();
                for (int i = 0; i < roots.Length; i++)
                {
                    GameObject root = roots[i];
                    if (root == null || root == probe)
                    {
                        continue;
                    }

                    filtered.Add(root);
                }

                return filtered.ToArray();
            }
            finally
            {
#if UNITY_EDITOR
                if (probe != null)
                {
                    if (Application.isPlaying)
                    {
                        UnityEngine.Object.Destroy(probe);
                    }
                    else
                    {
                        UnityEngine.Object.DestroyImmediate(probe);
                    }
                }
#endif
            }
        }

        private void TraverseHierarchy(GameObject obj, int? parentId, int depth, HierarchyOptions options, List<HierarchyNode> nodes)
        {
            // Check depth limit
            if (options.MaxDepth >= 0 && depth > options.MaxDepth)
                return;
                
            // Get components
            string[] componentNames = new string[0];
            if (options.IncludeComponents)
            {
                Component[] components = obj.GetComponents<Component>();
                componentNames = components
                    .Where(c => c != null)
                    .Select(c => c.GetType().Name)
                    .ToArray();
            }
            
            // Create node
            HierarchyNode node = new HierarchyNode(
                id: obj.GetInstanceID(),
                name: obj.name,
                parent: parentId,
                depth: depth,
                isActive: obj.activeSelf,
                components: componentNames,
                sceneName: obj.scene.name,
                siblingIndex: obj.transform.GetSiblingIndex(),
                tag: obj.tag,
                layer: obj.layer
            );
            
            nodes.Add(node);
            
            // Traverse children
            int currentId = obj.GetInstanceID();
            foreach (Transform child in obj.transform)
            {
                if (!options.IncludeInactive && !child.gameObject.activeInHierarchy)
                    continue;
                    
                TraverseHierarchy(child.gameObject, currentId, depth + 1, options, nodes);
            }
        }
    }
}