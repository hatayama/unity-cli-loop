using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Core service for finding GameObjects and extracting component information
    /// Related classes:
    /// - ComponentSerializer: Serializes component properties to ComponentInfo
    /// - FindGameObjectsCommand: API command for advanced search
    /// - GameObjectSearchFilters: Filtering logic for search operations
    /// </summary>
    public class GameObjectFinderService
    {
        
        private string GetFullPath(GameObject gameObject)
        {
            if (gameObject.transform.parent == null)
            {
                return gameObject.name;
            }
            
            return GetFullPath(gameObject.transform.parent.gameObject) + "/" + gameObject.name;
        }
        
        public GameObjectDetails[] FindGameObjectsAdvanced(GameObjectSearchOptions options)
        {
            List<GameObjectDetails> results = new();
            
            // Handle hierarchy path search separately
            if (options.SearchMode == SearchMode.Path && !string.IsNullOrEmpty(options.NamePattern))
            {
                GameObject found = FindGameObjectByPath(options.NamePattern);
                if (found != null)
                {
                    GameObjectDetails details = new()                    {
                        Found = true,
                        GameObject = found,
                        Name = found.name,
                        Path = GetFullPath(found),
                        IsActive = found.activeSelf
                    };
                    results.Add(details);
                }
                return results.ToArray();
            }
            
            // Get all root GameObjects from all loaded scenes
            List<GameObject> allGameObjects = GetAllGameObjects(options.IncludeInactive);
            
            foreach (GameObject gameObject in allGameObjects)
            {
                if (MatchesAllCriteria(gameObject, options))
                {
                    GameObjectDetails details = new()                    {
                        Found = true,
                        GameObject = gameObject,
                        Name = gameObject.name,
                        Path = GetFullPath(gameObject),
                        IsActive = gameObject.activeSelf
                    };
                    
                    results.Add(details);
                    
                    if (results.Count >= options.MaxResults)
                        break;
                }
            }
            
            return results.ToArray();
        }
        
        private GameObject FindGameObjectByPath(string path)
        {
            // GameObject.Find() does not work in Prefab Stage; Transform.Find() intentionally
            // returns inactive children too — active/inactive filtering is handled by the caller.
            PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage == null)
            {
                return GameObject.Find(path);
            }

            GameObject prefabRoot = prefabStage.prefabContentsRoot;
            string trimmedPath = path.TrimStart('/');

            if (trimmedPath == prefabRoot.name)
            {
                return prefabRoot;
            }

            if (trimmedPath.StartsWith(prefabRoot.name + "/"))
            {
                string relativePath = trimmedPath.Substring(prefabRoot.name.Length + 1);
                Transform found = prefabRoot.transform.Find(relativePath);
                return found != null ? found.gameObject : null;
            }

            Transform directFind = prefabRoot.transform.Find(trimmedPath);
            return directFind != null ? directFind.gameObject : null;
        }

        private List<GameObject> GetAllGameObjects(bool includeInactive)
        {
            List<GameObject> allGameObjects = new();

            // Prefab Stage has its own scene not visible via SceneManager
            PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null)
            {
                GameObject prefabRoot = prefabStage.prefabContentsRoot;
                AddGameObjectAndChildren(prefabRoot, allGameObjects, includeInactive);
                return allGameObjects;
            }

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded)
                {
                    GameObject[] rootObjects = scene.GetRootGameObjects();
                    foreach (GameObject root in rootObjects)
                    {
                        AddGameObjectAndChildren(root, allGameObjects, includeInactive);
                    }
                }
            }

            return allGameObjects;
        }
        
        private void AddGameObjectAndChildren(GameObject gameObject, List<GameObject> list, bool includeInactive)
        {
            if (!includeInactive && !gameObject.activeInHierarchy)
                return;
                
            list.Add(gameObject);
            
            foreach (Transform child in gameObject.transform)
            {
                AddGameObjectAndChildren(child.gameObject, list, includeInactive);
            }
        }
        
        private bool MatchesAllCriteria(GameObject gameObject, GameObjectSearchOptions options)
        {
            // Check name pattern
            if (!GameObjectSearchFilters.MatchesNamePattern(gameObject, options.NamePattern, options.SearchMode))
                return false;

            // Check required components
            if (!GameObjectSearchFilters.HasRequiredComponents(gameObject, options.RequiredComponents))
                return false;

            // Check tag
            if (!GameObjectSearchFilters.MatchesTag(gameObject, options.Tag))
                return false;

            // Check layer
            if (!GameObjectSearchFilters.MatchesLayer(gameObject, options.Layer))
                return false;

            // Check active state
            if (!GameObjectSearchFilters.MatchesActiveState(gameObject, options.IncludeInactive))
                return false;

            return true;
        }

        /// <summary>
        /// Get currently selected GameObjects in Unity Editor
        /// </summary>
        /// <param name="includeInactive">Whether to include inactive GameObjects in results</param>
        /// <returns>Array of GameObjectDetails for selected objects</returns>
        public GameObjectDetails[] FindSelectedGameObjects(bool includeInactive)
        {
            GameObject[] selectedGameObjects = Selection.gameObjects;

            if (selectedGameObjects == null || selectedGameObjects.Length == 0)
            {
                return new GameObjectDetails[0];
            }

            List<GameObjectDetails> results = new();

            foreach (GameObject gameObject in selectedGameObjects)
            {
                if (gameObject == null)
                {
                    continue;
                }

                if (!includeInactive && !gameObject.activeInHierarchy)
                {
                    continue;
                }

                GameObjectDetails details = new()                {
                    Found = true,
                    GameObject = gameObject,
                    Name = gameObject.name,
                    Path = GetFullPath(gameObject),
                    IsActive = gameObject.activeSelf
                };

                results.Add(details);
            }

            return results.ToArray();
        }
    }
}