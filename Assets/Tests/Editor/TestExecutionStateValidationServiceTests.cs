using NUnit.Framework;
using UnityEditor.TestTools.TestRunner.Api;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies Test Execution State Validation Service behavior.
    /// </summary>
    public class TestExecutionStateValidationServiceTests
    {
        [Test]
        public void Validate_WithEditModeWhilePlaying_ShouldReturnFailure()
        {
            TestExecutionStateValidationService service = new StubTestExecutionStateValidationService(true);

            ValidationResult result = service.Validate(TestMode.EditMode, saveBeforeRun: false);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo("EditMode tests cannot run during play mode"));
        }

        [Test]
        public void Validate_WithEditModeWhileNotPlaying_ShouldReturnSuccess()
        {
            TestExecutionStateValidationService service = new StubTestExecutionStateValidationService(false);

            ValidationResult result = service.Validate(TestMode.EditMode, saveBeforeRun: false);

            Assert.That(result.IsValid, Is.True);
            Assert.That(result.ErrorMessage, Is.Null);
        }

        [Test]
        public void Validate_WithPlayModeWhilePlaying_ShouldReturnSuccess()
        {
            TestExecutionStateValidationService service = new StubTestExecutionStateValidationService(true);

            ValidationResult result = service.Validate(TestMode.PlayMode, saveBeforeRun: false);

            Assert.That(result.IsValid, Is.True);
            Assert.That(result.ErrorMessage, Is.Null);
        }

        [Test]
        public void Validate_WhenCompilationIsInProgress_ShouldReturnFailure()
        {
            TestExecutionStateValidationService service = new StubTestExecutionStateValidationService(
                isPlaying: false,
                isCompiling: true);

            ValidationResult result = service.Validate(TestMode.EditMode, saveBeforeRun: false);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo("Tests cannot run while compilation is in progress"));
        }

        [Test]
        public void Validate_WhenEditorIsUpdating_ShouldReturnFailure()
        {
            TestExecutionStateValidationService service = new StubTestExecutionStateValidationService(
                isPlaying: false,
                isUpdating: true);

            ValidationResult result = service.Validate(TestMode.EditMode, saveBeforeRun: false);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo("Tests cannot run while the editor is updating"));
        }

        [Test]
        public void Validate_WhenEditorHasUnsavedChangesAndSaveBeforeRunIsFalse_ShouldReturnFailure()
        {
            string[] unsavedEditorChanges =
            {
                "Scene: Assets/Scenes/Minecraft.unity",
                "Prefab Stage: Assets/Scenes/GameCanvas.prefab"
            };
            TestExecutionStateValidationService service = new StubTestExecutionStateValidationService(
                isPlaying: false,
                unsavedEditorChanges: unsavedEditorChanges);

            ValidationResult result = service.Validate(TestMode.PlayMode, saveBeforeRun: false);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("Tests cannot run while the editor has unsaved scene or prefab changes"));
            Assert.That(result.ErrorMessage, Does.Contain("Scene: Assets/Scenes/Minecraft.unity"));
            Assert.That(result.ErrorMessage, Does.Contain("Prefab Stage: Assets/Scenes/GameCanvas.prefab"));
        }

        [Test]
        public void Validate_WhenEditorHasUnsavedChangesAndSaveBeforeRunIsTrue_ShouldSaveAndReturnSuccess()
        {
            string[] unsavedEditorChanges =
            {
                "Scene: Assets/Scenes/Minecraft.unity"
            };
            StubTestExecutionStateValidationService service = new(
                isPlaying: false,
                unsavedEditorChanges: unsavedEditorChanges,
                saveResult: ValidationResult.Success(),
                clearUnsavedChangesAfterSave: true);

            ValidationResult result = service.Validate(TestMode.PlayMode, saveBeforeRun: true);

            Assert.That(result.IsValid, Is.True);
            Assert.That(result.ErrorMessage, Is.Null);
            Assert.That(service.SaveWasCalled, Is.True);
        }

        [Test]
        public void Validate_WhenSaveBeforeRunFails_ShouldReturnFailure()
        {
            string[] unsavedEditorChanges =
            {
                "Prefab Stage: Assets/Scenes/Crosshair.prefab"
            };
            TestExecutionStateValidationService service = new StubTestExecutionStateValidationService(
                isPlaying: false,
                unsavedEditorChanges: unsavedEditorChanges,
                saveResult: ValidationResult.Failure("Tests cannot save unsaved scene or prefab changes before running tests. Unsaved changes that failed to save: Prefab Stage: Assets/Scenes/Crosshair.prefab"));

            ValidationResult result = service.Validate(TestMode.PlayMode, saveBeforeRun: true);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("Tests cannot save unsaved scene or prefab changes before running tests"));
            Assert.That(result.ErrorMessage, Does.Contain("Prefab Stage: Assets/Scenes/Crosshair.prefab"));
        }

        /// <summary>
        /// Test support type used by editor and play mode fixtures.
        /// </summary>
        private sealed class StubTestExecutionStateValidationService : TestExecutionStateValidationService
        {
            private readonly bool _isPlaying;
            private readonly bool _isCompiling;
            private readonly bool _isUpdating;
            private readonly ValidationResult _saveResult;
            private readonly bool _clearUnsavedChangesAfterSave;
            private string[] _unsavedEditorChanges;

            public bool SaveWasCalled { get; private set; }

            public StubTestExecutionStateValidationService(
                bool isPlaying,
                bool isCompiling = false,
                bool isUpdating = false,
                string[] unsavedEditorChanges = null,
                ValidationResult saveResult = null,
                bool clearUnsavedChangesAfterSave = false)
            {
                _isPlaying = isPlaying;
                _isCompiling = isCompiling;
                _isUpdating = isUpdating;
                _unsavedEditorChanges = unsavedEditorChanges ?? new string[0];
                _saveResult = saveResult ?? ValidationResult.Success();
                _clearUnsavedChangesAfterSave = clearUnsavedChangesAfterSave;
            }

            protected override bool IsPlaying => _isPlaying;
            protected override bool IsCompiling => _isCompiling;
            protected override bool IsUpdating => _isUpdating;
            protected override string[] DetectUnsavedEditorChanges()
            {
                return _unsavedEditorChanges;
            }

            protected override ValidationResult SaveUnsavedEditorChanges()
            {
                SaveWasCalled = true;
                if (_clearUnsavedChangesAfterSave)
                {
                    _unsavedEditorChanges = new string[0];
                }

                return _saveResult;
            }
        }
    }
}
