using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Drives the focus-return save and reload paths through the real trackers with real assets,
    /// so a broken wiring between the save policy and the trackers fails these tests.
    /// </summary>
    public sealed class ExternalFocusReturnTrackerWiringTests
    {
        private const string TempFolder = "Assets/UloopFocusReturnWiringTemp";
        private const string TempScenePath = TempFolder + "/WiringScene.unity";
        private const string TempPrefabPath = TempFolder + "/WiringPrefab.prefab";

        [SetUp]
        public void SetUp()
        {
            ExternalSceneChangeTracker.Initialize();
            if (!AssetDatabase.IsValidFolder(TempFolder))
            {
                AssetDatabase.CreateFolder("Assets", "UloopFocusReturnWiringTemp");
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (PrefabStageUtility.GetCurrentPrefabStage() != null)
            {
                PrefabStageUtility.GetCurrentPrefabStage().ClearDirtiness();
                StageUtility.GoBackToPreviousStage();
            }

            Scene tempScene = SceneManager.GetSceneByPath(TempScenePath);
            if (tempScene.IsValid() && tempScene.isLoaded)
            {
                // Why NewScene: the temp scene is the active (and only) scene, and Unity cannot
                // close the last loaded scene; replacing it releases the asset for deletion.
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }

            AssetDatabase.DeleteAsset(TempFolder);
        }

        [Test]
        public void SaveDirtyOpenScenesChangedExternally_WithUnchangedFile_LeavesSceneDirtyAndFileUntouched()
        {
            // Verifies the focus-return wiring does not save a dirty Scene whose file never changed on disk.
            Scene scene = CreateSavedActiveScene();
            MakeSceneDirty(scene);
            DateTime mtimeBefore = File.GetLastWriteTimeUtc(TempScenePath);

            string[] failures = ExternalSceneChangeTracker.SaveDirtyOpenScenesChangedExternally();

            Assert.That(failures, Is.Empty);
            Assert.That(scene.isDirty, Is.True);
            Assert.That(File.GetLastWriteTimeUtc(TempScenePath), Is.EqualTo(mtimeBefore));
        }

        [Test]
        public void SaveDirtyOpenScenesChangedExternally_WithExternallyTouchedFile_SavesTheScene()
        {
            // Verifies the focus-return wiring still writes the in-memory state over an externally changed file.
            Scene scene = CreateSavedActiveScene();
            MakeSceneDirty(scene);
            File.SetLastWriteTimeUtc(TempScenePath, DateTime.UtcNow.AddMinutes(1));

            string[] failures = ExternalSceneChangeTracker.SaveDirtyOpenScenesChangedExternally();

            Assert.That(failures, Is.Empty);
            Assert.That(scene.isDirty, Is.False);
        }

        [Test]
        public void SaveDirtyIfChangedExternally_WithUnchangedFile_LeavesPrefabStageDirtyAndFileUntouched()
        {
            // Verifies the focus-return wiring does not save a dirty Prefab Stage whose file never changed on disk.
            PrefabStage stage = OpenDirtyPrefabStage();
            DateTime mtimeBefore = File.GetLastWriteTimeUtc(TempPrefabPath);

            string[] failures = ExternalPrefabStageChangeTracker.SaveDirtyIfChangedExternally();

            Assert.That(failures, Is.Empty);
            Assert.That(stage.scene.isDirty, Is.True);
            Assert.That(File.GetLastWriteTimeUtc(TempPrefabPath), Is.EqualTo(mtimeBefore));
        }

        [Test]
        public void SaveDirtyIfChangedExternally_WithExternallyTouchedFile_SavesThePrefabStage()
        {
            // Verifies the focus-return wiring still writes a dirty Prefab Stage over an externally changed file.
            PrefabStage stage = OpenDirtyPrefabStage();
            File.SetLastWriteTimeUtc(TempPrefabPath, DateTime.UtcNow.AddMinutes(1));

            string[] failures = ExternalPrefabStageChangeTracker.SaveDirtyIfChangedExternally();

            Assert.That(failures, Is.Empty);
            Assert.That(stage.scene.isDirty, Is.False);
        }

        [Test]
        public void ResolveExternalChangeForFocusReturn_WithDirtyStageAndChangedFile_KeepsUnsavedEditsInsteadOfReloading()
        {
            // Verifies a Prefab Stage left dirty when its file changed after the save check is not reloaded over.
            PrefabStage stage = OpenDirtyPrefabStage();
            File.SetLastWriteTimeUtc(TempPrefabPath, DateTime.UtcNow.AddMinutes(1));

            ExternalPrefabStageChangeTracker.ResolveExternalChangeForFocusReturn();

            PrefabStage current = PrefabStageUtility.GetCurrentPrefabStage();
            Assert.That(current, Is.SameAs(stage));
            Assert.That(current.scene.isDirty, Is.True);
            Assert.That(current.prefabContentsRoot.transform.Find("WiringProbe"), Is.Not.Null);
        }

        private static Scene CreateSavedActiveScene()
        {
            // Why Single: the test runner can hold an untitled unsaved scene, and Unity refuses to
            // create an additive scene next to one. Replacing the active scene avoids that restriction.
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            bool saved = EditorSceneManager.SaveScene(scene, TempScenePath);
            Assert.That(saved, Is.True, "temp scene must save so a snapshot fingerprint exists");
            return scene;
        }

        private static void MakeSceneDirty(Scene scene)
        {
            GameObject probe = new GameObject("WiringProbe");
            SceneManager.MoveGameObjectToScene(probe, scene);
            EditorSceneManager.MarkSceneDirty(scene);
            Assert.That(scene.isDirty, Is.True, "scene must be dirty before the save decision runs");
        }

        private static PrefabStage OpenDirtyPrefabStage()
        {
            GameObject root = new GameObject("WiringPrefabRoot");
            bool created;
            PrefabUtility.SaveAsPrefabAsset(root, TempPrefabPath, out created);
            UnityEngine.Object.DestroyImmediate(root);
            Assert.That(created, Is.True, "temp prefab must save before it can be opened in a stage");

            PrefabStage stage = PrefabStageUtility.OpenPrefab(TempPrefabPath);
            ExternalPrefabStageChangeTracker.RecordCurrent();
            GameObject probe = new GameObject("WiringProbe");
            probe.transform.SetParent(stage.prefabContentsRoot.transform);
            EditorSceneManager.MarkSceneDirty(stage.scene);
            Assert.That(stage.scene.isDirty, Is.True, "prefab stage must be dirty before the save decision runs");
            return stage;
        }
    }
}
