using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Discards dirty loaded Scenes and the current Prefab Stage without prompting the user.
    /// Untitled dirty scenes cannot be reloaded from disk, so those are reported and left untouched.
    /// </summary>
    public sealed class EditorUnsavedChangesDiscarder : IEditorUnsavedChangesDiscarder
    {
        public string[] DiscardUnsavedEditorChanges()
        {
            List<string> failures = new();
            CollectUndiscardableDirtyScenes(failures);
            if (failures.Count > 0)
            {
                return failures.ToArray();
            }

            DiscardDirtyPrefabStage(failures);
            ReloadDirtySavedScenes();
            return failures.ToArray();
        }

        private static void CollectUndiscardableDirtyScenes(List<string> failures)
        {
            Debug.Assert(failures != null, "failures must not be null");

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded || !scene.isDirty)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(scene.path))
                {
                    continue;
                }

                failures.Add("Scene: " + GetSceneDisplayPath(scene));
            }
        }

        private static void DiscardDirtyPrefabStage(List<string> failures)
        {
            Debug.Assert(failures != null, "failures must not be null");

            PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage == null || !prefabStage.scene.IsValid() || !prefabStage.scene.isDirty)
            {
                return;
            }

            string assetPath = prefabStage.assetPath;
            if (string.IsNullOrEmpty(assetPath))
            {
                // OpenPrefab requires a disk path; leaving the stage alone avoids a prompt-less dead end.
                failures.Add("Prefab Stage: " + GetPrefabStageDisplayPath(prefabStage));
                return;
            }

            prefabStage.ClearDirtiness();
            StageUtility.GoBackToPreviousStage();
            PrefabStage reopened = PrefabStageUtility.OpenPrefab(assetPath);
            if (reopened == null)
            {
                failures.Add("Prefab Stage: " + assetPath);
            }
        }

        private static void ReloadDirtySavedScenes()
        {
            if (!HasDirtyLoadedSceneWithPath())
            {
                return;
            }

            List<LoadedSceneSnapshot> snapshots = SnapshotLoadedScenes();
            Debug.Assert(snapshots.Count > 0, "dirty saved scenes must produce at least one path snapshot");

            string activePath = SceneManager.GetActiveScene().path;
            EditorSceneManager.OpenScene(snapshots[0].Path, OpenSceneMode.Single);
            for (int i = 1; i < snapshots.Count; i++)
            {
                OpenSceneMode mode = snapshots[i].IsLoaded
                    ? OpenSceneMode.Additive
                    : OpenSceneMode.AdditiveWithoutLoading;
                EditorSceneManager.OpenScene(snapshots[i].Path, mode);
            }

            RestoreActiveScene(activePath);
        }

        private static bool HasDirtyLoadedSceneWithPath()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.IsValid() && scene.isLoaded && scene.isDirty && !string.IsNullOrEmpty(scene.path))
                {
                    return true;
                }
            }

            return false;
        }

        private static List<LoadedSceneSnapshot> SnapshotLoadedScenes()
        {
            List<LoadedSceneSnapshot> snapshots = new();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
                {
                    continue;
                }

                snapshots.Add(new LoadedSceneSnapshot(scene.path, scene.isLoaded));
            }

            return snapshots;
        }

        private static void RestoreActiveScene(string activePath)
        {
            if (string.IsNullOrEmpty(activePath))
            {
                return;
            }

            Scene activeScene = SceneManager.GetSceneByPath(activePath);
            if (!activeScene.IsValid() || !activeScene.isLoaded)
            {
                return;
            }

            SceneManager.SetActiveScene(activeScene);
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

        private readonly struct LoadedSceneSnapshot
        {
            public readonly string Path;
            public readonly bool IsLoaded;

            public LoadedSceneSnapshot(string path, bool isLoaded)
            {
                Path = path;
                IsLoaded = isLoaded;
            }
        }
    }
}
