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
    /// Drives EditorUnsavedChangesDiscarder against real Scenes and Prefab Stages so discard
    /// restores disk contents without writing files or prompting.
    /// </summary>
    public sealed class EditorUnsavedChangesDiscarderTests
    {
        private const string TempFolder = "Assets/UloopUnsavedChangesDiscarderTemp";
        private const string TempScenePath = TempFolder + "/DiscarderScene.unity";
        private const string TempPrefabPath = TempFolder + "/DiscarderPrefab.prefab";

        private readonly EditorUnsavedChangesDiscarder _discarder = new EditorUnsavedChangesDiscarder();

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TempFolder))
            {
                AssetDatabase.CreateFolder("Assets", "UloopUnsavedChangesDiscarderTemp");
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

        /// <summary>
        /// Verifies a dirty saved Scene is reloaded from disk without writing the file.
        /// </summary>
        [Test]
        public void DiscardUnsavedEditorChanges_WithDirtySavedScene_ReloadsWithoutWritingFile()
        {
            Scene scene = CreateSavedActiveScene();
            MakeSceneDirty(scene);
            DateTime mtimeBefore = File.GetLastWriteTimeUtc(TempScenePath);

            string[] failures = _discarder.DiscardUnsavedEditorChanges();

            Scene reloaded = SceneManager.GetSceneByPath(TempScenePath);
            Assert.That(failures, Is.Empty);
            Assert.That(reloaded.IsValid(), Is.True);
            Assert.That(reloaded.isDirty, Is.False);
            Assert.That(GameObject.Find("DiscarderProbe"), Is.Null);
            Assert.That(File.GetLastWriteTimeUtc(TempScenePath), Is.EqualTo(mtimeBefore));
        }

        /// <summary>
        /// Verifies a dirty Prefab Stage is reopened from disk without writing the prefab file.
        /// </summary>
        [Test]
        public void DiscardUnsavedEditorChanges_WithDirtyPrefabStage_ReopensWithoutWritingFile()
        {
            PrefabStage stage = OpenDirtyPrefabStage();
            DateTime mtimeBefore = File.GetLastWriteTimeUtc(TempPrefabPath);

            string[] failures = _discarder.DiscardUnsavedEditorChanges();

            PrefabStage current = PrefabStageUtility.GetCurrentPrefabStage();
            Assert.That(failures, Is.Empty);
            Assert.That(current, Is.Not.Null);
            Assert.That(current.assetPath, Is.EqualTo(TempPrefabPath));
            Assert.That(current.scene.isDirty, Is.False);
            Assert.That(File.GetLastWriteTimeUtc(TempPrefabPath), Is.EqualTo(mtimeBefore));
            Assert.That(stage, Is.Not.EqualTo(current));
        }

        /// <summary>
        /// Verifies discard restores both a dirty Prefab Stage and a dirty saved Scene without writing either file.
        /// </summary>
        [Test]
        public void DiscardUnsavedEditorChanges_WithDirtyPrefabStageAndDirtySavedScene_RestoresBothWithoutWritingFiles()
        {
            Scene scene = CreateSavedActiveScene();
            MakeSceneDirty(scene);
            OpenDirtyPrefabStage();
            DateTime sceneMtimeBefore = File.GetLastWriteTimeUtc(TempScenePath);
            DateTime prefabMtimeBefore = File.GetLastWriteTimeUtc(TempPrefabPath);

            string[] failures = _discarder.DiscardUnsavedEditorChanges();

            PrefabStage current = PrefabStageUtility.GetCurrentPrefabStage();
            Scene reloaded = SceneManager.GetSceneByPath(TempScenePath);
            Assert.That(failures, Is.Empty);
            Assert.That(current, Is.Not.Null);
            Assert.That(current.assetPath, Is.EqualTo(TempPrefabPath));
            Assert.That(current.scene.isDirty, Is.False);
            Assert.That(reloaded.IsValid(), Is.True);
            Assert.That(reloaded.isLoaded, Is.True);
            Assert.That(reloaded.isDirty, Is.False);
            Assert.That(GameObject.Find("DiscarderProbe"), Is.Null);
            Assert.That(File.GetLastWriteTimeUtc(TempScenePath), Is.EqualTo(sceneMtimeBefore));
            Assert.That(File.GetLastWriteTimeUtc(TempPrefabPath), Is.EqualTo(prefabMtimeBefore));
        }

        /// <summary>
        /// Verifies an untitled dirty Scene is reported and left untouched because it has no disk path.
        /// </summary>
        [Test]
        public void DiscardUnsavedEditorChanges_WithUntitledDirtyScene_ReturnsFailureAndLeavesSceneUntouched()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            MakeSceneDirty(scene);

            string[] failures = _discarder.DiscardUnsavedEditorChanges();

            Assert.That(failures, Has.Some.Contains("Scene:"));
            Assert.That(scene.IsValid(), Is.True);
            Assert.That(scene.isDirty, Is.True);
            Assert.That(string.IsNullOrEmpty(scene.path), Is.True);
        }

        private static Scene CreateSavedActiveScene()
        {
            // Why Single: the test runner can hold an untitled unsaved scene, and Unity refuses to
            // create an additive scene next to one. Replacing the active scene avoids that restriction.
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            bool saved = EditorSceneManager.SaveScene(scene, TempScenePath);
            Assert.That(saved, Is.True, "temp scene must save so discard can reload it from disk");
            return scene;
        }

        private static void MakeSceneDirty(Scene scene)
        {
            GameObject probe = new GameObject("DiscarderProbe");
            SceneManager.MoveGameObjectToScene(probe, scene);
            EditorSceneManager.MarkSceneDirty(scene);
            Assert.That(scene.isDirty, Is.True, "scene must be dirty before discard runs");
        }

        private static PrefabStage OpenDirtyPrefabStage()
        {
            GameObject root = new GameObject("DiscarderPrefabRoot");
            bool created;
            PrefabUtility.SaveAsPrefabAsset(root, TempPrefabPath, out created);
            UnityEngine.Object.DestroyImmediate(root);
            Assert.That(created, Is.True, "temp prefab must save before it can be opened in a stage");

            PrefabStage stage = PrefabStageUtility.OpenPrefab(TempPrefabPath);
            Assert.That(stage, Is.Not.Null, "temp prefab stage must open");
            GameObject probe = new GameObject("DiscarderProbe");
            probe.transform.SetParent(stage.prefabContentsRoot.transform);
            EditorSceneManager.MarkSceneDirty(stage.scene);
            Assert.That(stage.scene.isDirty, Is.True, "prefab stage must be dirty before discard runs");
            return stage;
        }
    }
}
