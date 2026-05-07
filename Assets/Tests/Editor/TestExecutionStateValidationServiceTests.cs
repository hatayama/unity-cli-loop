using NUnit.Framework;

namespace io.github.hatayama.uLoopMCP
{
    public class TestExecutionStateValidationServiceTests
    {
        [Test]
        public void Validate_WithEditModeWhilePlaying_ShouldReturnFailure()
        {
            TestExecutionStateValidationService service = new StubTestExecutionStateValidationService(true);

            ValidationResult result = service.Validate(RunTestMode.EditMode, saveBeforeRun: false);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo("EditMode tests cannot run during play mode"));
        }

        [Test]
        public void Validate_WithEditModeWhileNotPlaying_ShouldReturnSuccess()
        {
            TestExecutionStateValidationService service = new StubTestExecutionStateValidationService(false);

            ValidationResult result = service.Validate(RunTestMode.EditMode, saveBeforeRun: false);

            Assert.That(result.IsValid, Is.True);
            Assert.That(result.ErrorMessage, Is.Null);
        }

        [Test]
        public void Validate_WithPlayModeWhilePlaying_ShouldReturnSuccess()
        {
            TestExecutionStateValidationService service = new StubTestExecutionStateValidationService(true);

            ValidationResult result = service.Validate(RunTestMode.PlayMode, saveBeforeRun: false);

            Assert.That(result.IsValid, Is.True);
            Assert.That(result.ErrorMessage, Is.Null);
        }

        [Test]
        public void Validate_WhenCompilationIsInProgress_ShouldReturnFailure()
        {
            TestExecutionStateValidationService service = new StubTestExecutionStateValidationService(
                isPlaying: false,
                isCompiling: true);

            ValidationResult result = service.Validate(RunTestMode.EditMode, saveBeforeRun: false);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo("Tests cannot run while compilation is in progress"));
        }

        [Test]
        public void Validate_WhenDomainReloadIsInProgress_ShouldReturnFailure()
        {
            TestExecutionStateValidationService service = new StubTestExecutionStateValidationService(
                isPlaying: false,
                isDomainReloadInProgress: true);

            ValidationResult result = service.Validate(RunTestMode.EditMode, saveBeforeRun: false);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo("Tests cannot run while domain reload is in progress"));
        }

        [Test]
        public void Validate_WhenEditorIsUpdating_ShouldReturnFailure()
        {
            TestExecutionStateValidationService service = new StubTestExecutionStateValidationService(
                isPlaying: false,
                isUpdating: true);

            ValidationResult result = service.Validate(RunTestMode.EditMode, saveBeforeRun: false);

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

            ValidationResult result = service.Validate(RunTestMode.PlayMode, saveBeforeRun: false);

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
            StubTestExecutionStateValidationService service = new StubTestExecutionStateValidationService(
                isPlaying: false,
                unsavedEditorChanges: unsavedEditorChanges,
                saveResult: ValidationResult.Success(),
                clearUnsavedChangesAfterSave: true);

            ValidationResult result = service.Validate(RunTestMode.PlayMode, saveBeforeRun: true);

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

            ValidationResult result = service.Validate(RunTestMode.PlayMode, saveBeforeRun: true);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("Tests cannot save unsaved scene or prefab changes before running tests"));
            Assert.That(result.ErrorMessage, Does.Contain("Prefab Stage: Assets/Scenes/Crosshair.prefab"));
        }

        private sealed class StubTestExecutionStateValidationService : TestExecutionStateValidationService
        {
            private readonly bool _isPlaying;
            private readonly bool _isCompiling;
            private readonly bool _isDomainReloadInProgress;
            private readonly bool _isUpdating;
            private readonly ValidationResult _saveResult;
            private readonly bool _clearUnsavedChangesAfterSave;
            private string[] _unsavedEditorChanges;

            public bool SaveWasCalled { get; private set; }

            public StubTestExecutionStateValidationService(
                bool isPlaying,
                bool isCompiling = false,
                bool isDomainReloadInProgress = false,
                bool isUpdating = false,
                string[] unsavedEditorChanges = null,
                ValidationResult saveResult = null,
                bool clearUnsavedChangesAfterSave = false)
            {
                _isPlaying = isPlaying;
                _isCompiling = isCompiling;
                _isDomainReloadInProgress = isDomainReloadInProgress;
                _isUpdating = isUpdating;
                _unsavedEditorChanges = unsavedEditorChanges ?? new string[0];
                _saveResult = saveResult ?? ValidationResult.Success();
                _clearUnsavedChangesAfterSave = clearUnsavedChangesAfterSave;
            }

            protected override bool IsPlaying => _isPlaying;
            protected override bool IsCompiling => _isCompiling;
            protected override bool IsDomainReloadInProgress => _isDomainReloadInProgress;
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
