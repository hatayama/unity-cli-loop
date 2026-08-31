using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Quietly saves dirty loaded Scenes and the current Prefab Stage without prompting the user.
    /// Shared by run-tests and control-play-mode so Play Mode start and test runs use the same path.
    /// </summary>
    public sealed class EditorUnsavedChangesQuietSaver : IEditorUnsavedChangesQuietSaver
    {
        public string[] DetectUnsavedEditorChanges()
        {
            List<string> unsavedEditorChanges = new();
            AddDirtyLoadedScenes(unsavedEditorChanges);
            AddDirtyPrefabStage(unsavedEditorChanges);
            return unsavedEditorChanges.ToArray();
        }

        public string[] SaveUnsavedEditorChanges()
        {
            List<string> failedChanges = new();
            SaveDirtyLoadedScenes(failedChanges);
            SaveDirtyPrefabStage(failedChanges);
            return failedChanges.ToArray();
        }

        private static void AddDirtyLoadedScenes(List<string> unsavedEditorChanges)
        {
            Debug.Assert(unsavedEditorChanges != null, "unsavedEditorChanges must not be null");

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded || !scene.isDirty)
                {
                    continue;
                }

                unsavedEditorChanges.Add("Scene: " + GetSceneDisplayPath(scene));
            }
        }

        private static void SaveDirtyLoadedScenes(List<string> failedChanges)
        {
            Debug.Assert(failedChanges != null, "failedChanges must not be null");

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded || !scene.isDirty)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(scene.path) || !EditorSceneManager.SaveScene(scene))
                {
                    failedChanges.Add("Scene: " + GetSceneDisplayPath(scene));
                }
            }
        }

        private static void AddDirtyPrefabStage(List<string> unsavedEditorChanges)
        {
            Debug.Assert(unsavedEditorChanges != null, "unsavedEditorChanges must not be null");

            PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage == null || !prefabStage.scene.IsValid() || !prefabStage.scene.isDirty)
            {
                return;
            }

            unsavedEditorChanges.Add("Prefab Stage: " + GetPrefabStageDisplayPath(prefabStage));
        }

        private static void SaveDirtyPrefabStage(List<string> failedChanges)
        {
            Debug.Assert(failedChanges != null, "failedChanges must not be null");

            PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage == null || !prefabStage.scene.IsValid() || !prefabStage.scene.isDirty)
            {
                return;
            }

            if (!SavePrefabStage(prefabStage))
            {
                failedChanges.Add("Prefab Stage: " + GetPrefabStageDisplayPath(prefabStage));
            }
        }

        private static bool SavePrefabStage(PrefabStage prefabStage)
        {
            Debug.Assert(prefabStage != null, "prefabStage must not be null");

            if (string.IsNullOrEmpty(prefabStage.assetPath))
            {
                return false;
            }

            bool success;
            PrefabUtility.SaveAsPrefabAsset(prefabStage.prefabContentsRoot, prefabStage.assetPath, out success);
            if (success)
            {
                prefabStage.ClearDirtiness();
            }

            return success;
        }

        private static string GetSceneDisplayPath(Scene scene)
        {
            if (!string.IsNullOrEmpty(scene.path))
            {
                return scene.path;
            }

            if (!string.IsNullOrEmpty(scene.name))
            {
                return scene.name;
            }

            return "Untitled scene";
        }

        private static string GetPrefabStageDisplayPath(PrefabStage prefabStage)
        {
            Debug.Assert(prefabStage != null, "prefabStage must not be null");

            if (!string.IsNullOrEmpty(prefabStage.assetPath))
            {
                return prefabStage.assetPath;
            }

            return GetSceneDisplayPath(prefabStage.scene);
        }
    }
}
