using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the quiet saver's detection of dirty loaded scenes and its refusal to save a scene that has
    /// no disk path, using only an in-memory untitled scene.
    /// </summary>
    public sealed class EditorUnsavedChangesQuietSaverTests
    {
        private EditorUnsavedChangesQuietSaver _saver;

        [SetUp]
        public void SetUp()
        {
            // Why a fresh Scene: the saver walks every loaded scene, so the test owns the only one.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            _saver = new EditorUnsavedChangesQuietSaver();
        }

        [TearDown]
        public void TearDown()
        {
            // Replaces the dirty untitled scene so later tests start from a clean Editor.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        /// <summary>
        /// Verifies a clean scene is neither reported as unsaved nor as a save failure.
        /// </summary>
        [Test]
        public void DetectAndSave_WithACleanScene_ReportNothing()
        {
            Assert.That(_saver.DetectUnsavedEditorChanges(), Is.Empty);
            Assert.That(_saver.SaveUnsavedEditorChanges(), Is.Empty);
        }

        /// <summary>
        /// Verifies a dirty scene without a path or name is reported under the untitled label.
        /// </summary>
        [Test]
        public void DetectUnsavedEditorChanges_WithADirtyUntitledScene_ReportsItAsUntitled()
        {
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            Assert.That(_saver.DetectUnsavedEditorChanges(), Is.EqualTo(new[] { "Scene: Untitled scene" }));
        }

        /// <summary>
        /// Verifies a dirty scene with no disk path is reported as a failure and left dirty rather than saved
        /// through a prompt.
        /// </summary>
        [Test]
        public void SaveUnsavedEditorChanges_WithADirtyUntitledScene_ReportsAFailureWithoutSaving()
        {
            Scene scene = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);

            string[] failures = _saver.SaveUnsavedEditorChanges();

            Assert.That(failures, Is.EqualTo(new[] { "Scene: Untitled scene" }));
            Assert.That(scene.isDirty, Is.True);
        }
    }
}
