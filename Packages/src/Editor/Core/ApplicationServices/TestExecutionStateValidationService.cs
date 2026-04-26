using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace io.github.hatayama.uLoopMCP
{
    public class TestExecutionStateValidationService
    {
        private const string UnsavedEditorChangesFailureMessage =
            "Tests cannot run while the editor has unsaved scene or prefab changes. Save or discard these changes before running tests.";
        private const string UnsavedEditorChangesSaveFailureMessage =
            "Tests cannot save unsaved scene or prefab changes before running tests.";

        protected virtual bool IsPlaying => EditorApplication.isPlaying;
        protected virtual bool IsCompiling => EditorApplication.isCompiling;
        protected virtual bool IsDomainReloadInProgress => DomainReloadDetectionService.IsDomainReloadInProgress();
        protected virtual bool IsUpdating => EditorApplication.isUpdating;
        protected virtual string[] DetectUnsavedEditorChanges()
        {
            return DetectCurrentUnsavedEditorChanges();
        }
        protected virtual ValidationResult SaveUnsavedEditorChanges()
        {
            return SaveCurrentUnsavedEditorChanges();
        }

        public virtual ValidationResult Validate(TestMode testMode, bool saveBeforeRun)
        {
            if (IsCompiling)
            {
                return ValidationResult.Failure("Tests cannot run while compilation is in progress");
            }

            if (IsDomainReloadInProgress)
            {
                return ValidationResult.Failure("Tests cannot run while domain reload is in progress");
            }

            if (IsUpdating)
            {
                return ValidationResult.Failure("Tests cannot run while the editor is updating");
            }

            if (testMode == TestMode.EditMode && IsPlaying)
            {
                return ValidationResult.Failure("EditMode tests cannot run during play mode");
            }

            if (saveBeforeRun)
            {
                ValidationResult saveResult = SaveUnsavedEditorChanges();
                if (!saveResult.IsValid)
                {
                    return saveResult;
                }
            }

            string[] unsavedEditorChanges = DetectUnsavedEditorChanges();
            Debug.Assert(unsavedEditorChanges != null, "Unsaved editor change detection must return an array");
            if (unsavedEditorChanges.Length > 0)
            {
                return ValidationResult.Failure(CreateUnsavedEditorChangesFailureMessage(unsavedEditorChanges));
            }

            return ValidationResult.Success();
        }

        private static string[] DetectCurrentUnsavedEditorChanges()
        {
            List<string> unsavedEditorChanges = new List<string>();
            AddDirtyLoadedScenes(unsavedEditorChanges);
            AddDirtyPrefabStage(unsavedEditorChanges);
            return unsavedEditorChanges.ToArray();
        }

        private static ValidationResult SaveCurrentUnsavedEditorChanges()
        {
            List<string> failedChanges = new List<string>();
            SaveDirtyLoadedScenes(failedChanges);
            SaveDirtyPrefabStage(failedChanges);
            if (failedChanges.Count > 0)
            {
                return ValidationResult.Failure(CreateUnsavedEditorChangesSaveFailureMessage(failedChanges.ToArray()));
            }

            return ValidationResult.Success();
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

        private static string CreateUnsavedEditorChangesFailureMessage(string[] unsavedEditorChanges)
        {
            Debug.Assert(unsavedEditorChanges != null, "unsavedEditorChanges must not be null");
            Debug.Assert(unsavedEditorChanges.Length > 0, "unsavedEditorChanges must not be empty");

            return UnsavedEditorChangesFailureMessage + " Unsaved changes: " + string.Join(", ", unsavedEditorChanges);
        }

        private static string CreateUnsavedEditorChangesSaveFailureMessage(string[] failedChanges)
        {
            Debug.Assert(failedChanges != null, "failedChanges must not be null");
            Debug.Assert(failedChanges.Length > 0, "failedChanges must not be empty");

            return UnsavedEditorChangesSaveFailureMessage + " Unsaved changes that failed to save: " + string.Join(", ", failedChanges);
        }
    }
}
