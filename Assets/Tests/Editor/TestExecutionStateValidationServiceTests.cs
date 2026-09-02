using System;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies Test Execution State Validation Service behavior.
    /// </summary>
    public class TestExecutionStateValidationServiceTests
    {
        /// <summary>
        /// Verifies EditMode tests are rejected while Play Mode is active.
        /// </summary>
        [Test]
        public void Validate_WithEditModeWhilePlaying_ShouldReturnFailure()
        {
            TestExecutionStateValidationService service = new StubTestExecutionStateValidationService(true);

            ValidationResult result = service.Validate(UnityCliLoopTestMode.EditMode, RunTestsUnsavedChangesMode.fail);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo("EditMode tests cannot run during play mode. Use control-play-mode --action Stop to exit play mode, then rerun the tests."));
        }

        /// <summary>
        /// Verifies EditMode tests pass validation when the editor is not playing.
        /// </summary>
        [Test]
        public void Validate_WithEditModeWhileNotPlaying_ShouldReturnSuccess()
        {
            TestExecutionStateValidationService service = new StubTestExecutionStateValidationService(false);

            ValidationResult result = service.Validate(UnityCliLoopTestMode.EditMode, RunTestsUnsavedChangesMode.fail);

            Assert.That(result.IsValid, Is.True);
            Assert.That(result.ErrorMessage, Is.Null);
        }

        /// <summary>
        /// Verifies PlayMode tests pass validation while Play Mode is active and not paused.
        /// </summary>
        [Test]
        public void Validate_WithPlayModeWhilePlaying_ShouldReturnSuccess()
        {
            TestExecutionStateValidationService service = new StubTestExecutionStateValidationService(true);

            ValidationResult result = service.Validate(UnityCliLoopTestMode.PlayMode, RunTestsUnsavedChangesMode.fail);

            Assert.That(result.IsValid, Is.True);
            Assert.That(result.ErrorMessage, Is.Null);
        }

        /// <summary>
        /// Verifies tests are rejected while compilation is in progress.
        /// </summary>
        [Test]
        public void Validate_WhenCompilationIsInProgress_ShouldReturnFailure()
        {
            TestExecutionStateValidationService service = new StubTestExecutionStateValidationService(
                isPlaying: false,
                isCompiling: true);

            ValidationResult result = service.Validate(UnityCliLoopTestMode.EditMode, RunTestsUnsavedChangesMode.fail);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo("Tests cannot run while compilation is in progress"));
        }

        /// <summary>
        /// Verifies tests are rejected while the editor is updating.
        /// </summary>
        [Test]
        public void Validate_WhenEditorIsUpdating_ShouldReturnFailure()
        {
            TestExecutionStateValidationService service = new StubTestExecutionStateValidationService(
                isPlaying: false,
                isUpdating: true);

            ValidationResult result = service.Validate(UnityCliLoopTestMode.EditMode, RunTestsUnsavedChangesMode.fail);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo("Tests cannot run while the editor is updating"));
        }

        /// <summary>
        /// Verifies PlayMode tests are rejected when Play Mode is paused.
        /// </summary>
        [Test]
        public void Validate_WithPlayModeWhilePaused_ShouldReturnFailure()
        {
            TestExecutionStateValidationService service = new StubTestExecutionStateValidationService(
                isPlaying: true,
                isPaused: true);

            ValidationResult result = service.Validate(UnityCliLoopTestMode.PlayMode, RunTestsUnsavedChangesMode.fail);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("paused"));
            Assert.That(result.ErrorMessage, Does.Contain("control-play-mode"));
            Assert.That(result.ErrorMessage, Does.Contain("clear-pause-point"));
        }

        /// <summary>
        /// Verifies the existing "cannot run during play mode" check takes precedence over the paused check for EditMode.
        /// </summary>
        [Test]
        public void Validate_WithEditModeWhilePaused_ShouldReturnPlayModeFailureNotPausedFailure()
        {
            TestExecutionStateValidationService service = new StubTestExecutionStateValidationService(
                isPlaying: true,
                isPaused: true);

            ValidationResult result = service.Validate(UnityCliLoopTestMode.EditMode, RunTestsUnsavedChangesMode.fail);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo("EditMode tests cannot run during play mode. Use control-play-mode --action Stop to exit play mode, then rerun the tests."));
        }

        /// <summary>
        /// Verifies PlayMode tests pass validation when playing but not paused.
        /// </summary>
        [Test]
        public void Validate_WithPlayModeWhilePlayingNotPaused_ShouldReturnSuccess()
        {
            TestExecutionStateValidationService service = new StubTestExecutionStateValidationService(
                isPlaying: true,
                isPaused: false);

            ValidationResult result = service.Validate(UnityCliLoopTestMode.PlayMode, RunTestsUnsavedChangesMode.fail);

            Assert.That(result.IsValid, Is.True);
        }

        /// <summary>
        /// Verifies save mode persists dirty editor changes and then succeeds.
        /// </summary>
        [Test]
        public void Validate_WithSaveModeWhenSaveSucceeds_ShouldReturnSuccess()
        {
            string[] unsavedEditorChanges =
            {
                "Scene: Assets/Scenes/Minecraft.unity"
            };
            FakeEditorUnsavedChangesQuietSaver saver = new FakeEditorUnsavedChangesQuietSaver(
                unsavedEditorChanges,
                Array.Empty<string>());
            FakeEditorUnsavedChangesDiscarder discarder = new FakeEditorUnsavedChangesDiscarder(Array.Empty<string>());
            StubTestExecutionStateValidationService service = new StubTestExecutionStateValidationService(
                isPlaying: false,
                saver: saver,
                discarder: discarder);

            ValidationResult result = service.Validate(UnityCliLoopTestMode.PlayMode, RunTestsUnsavedChangesMode.save);

            Assert.That(result.IsValid, Is.True);
            Assert.That(result.ErrorMessage, Is.Null);
            Assert.That(saver.SaveCallCount, Is.EqualTo(1));
            Assert.That(discarder.DiscardCallCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies save mode returns the save-failure message and the failed change list.
        /// </summary>
        [Test]
        public void Validate_WithSaveModeWhenSaveFails_ShouldReturnFailureWithFailedChanges()
        {
            string[] unsavedEditorChanges =
            {
                "Prefab Stage: Assets/Scenes/Crosshair.prefab"
            };
            FakeEditorUnsavedChangesQuietSaver saver = new FakeEditorUnsavedChangesQuietSaver(
                unsavedEditorChanges,
                unsavedEditorChanges);
            FakeEditorUnsavedChangesDiscarder discarder = new FakeEditorUnsavedChangesDiscarder(Array.Empty<string>());
            TestExecutionStateValidationService service = new StubTestExecutionStateValidationService(
                isPlaying: false,
                saver: saver,
                discarder: discarder);

            ValidationResult result = service.Validate(UnityCliLoopTestMode.PlayMode, RunTestsUnsavedChangesMode.save);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("Tests cannot save unsaved scene or prefab changes before running tests"));
            Assert.That(result.ErrorMessage, Does.Contain("Prefab Stage: Assets/Scenes/Crosshair.prefab"));
            Assert.That(saver.SaveCallCount, Is.EqualTo(1));
            Assert.That(discarder.DiscardCallCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies fail mode leaves dirty editor changes untouched and rejects the run.
        /// </summary>
        [Test]
        public void Validate_WithFailModeWhenEditorIsDirty_ShouldReturnFailureWithoutSavingOrDiscarding()
        {
            string[] unsavedEditorChanges =
            {
                "Scene: Assets/Scenes/Minecraft.unity",
                "Prefab Stage: Assets/Scenes/GameCanvas.prefab"
            };
            FakeEditorUnsavedChangesQuietSaver saver = new FakeEditorUnsavedChangesQuietSaver(
                unsavedEditorChanges,
                Array.Empty<string>());
            FakeEditorUnsavedChangesDiscarder discarder = new FakeEditorUnsavedChangesDiscarder(Array.Empty<string>());
            TestExecutionStateValidationService service = new StubTestExecutionStateValidationService(
                isPlaying: false,
                saver: saver,
                discarder: discarder);

            ValidationResult result = service.Validate(UnityCliLoopTestMode.PlayMode, RunTestsUnsavedChangesMode.fail);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("Tests cannot run while the editor has unsaved scene or prefab changes"));
            Assert.That(result.ErrorMessage, Does.Contain("Scene: Assets/Scenes/Minecraft.unity"));
            Assert.That(result.ErrorMessage, Does.Contain("Prefab Stage: Assets/Scenes/GameCanvas.prefab"));
            Assert.That(saver.SaveCallCount, Is.EqualTo(0));
            Assert.That(discarder.DiscardCallCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies fail mode succeeds when the editor has no unsaved changes.
        /// </summary>
        [Test]
        public void Validate_WithFailModeWhenEditorIsClean_ShouldReturnSuccess()
        {
            FakeEditorUnsavedChangesQuietSaver saver = new FakeEditorUnsavedChangesQuietSaver(
                Array.Empty<string>(),
                Array.Empty<string>());
            FakeEditorUnsavedChangesDiscarder discarder = new FakeEditorUnsavedChangesDiscarder(Array.Empty<string>());
            TestExecutionStateValidationService service = new StubTestExecutionStateValidationService(
                isPlaying: false,
                saver: saver,
                discarder: discarder);

            ValidationResult result = service.Validate(UnityCliLoopTestMode.PlayMode, RunTestsUnsavedChangesMode.fail);

            Assert.That(result.IsValid, Is.True);
            Assert.That(result.ErrorMessage, Is.Null);
            Assert.That(saver.SaveCallCount, Is.EqualTo(0));
            Assert.That(discarder.DiscardCallCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies discard mode drops dirty editor changes and then succeeds.
        /// </summary>
        [Test]
        public void Validate_WithDiscardModeWhenDiscardSucceeds_ShouldReturnSuccess()
        {
            string[] unsavedEditorChanges =
            {
                "Scene: Assets/Scenes/Minecraft.unity"
            };
            FakeEditorUnsavedChangesQuietSaver saver = new FakeEditorUnsavedChangesQuietSaver(
                unsavedEditorChanges,
                Array.Empty<string>());
            FakeEditorUnsavedChangesDiscarder discarder = new FakeEditorUnsavedChangesDiscarder(Array.Empty<string>());
            discarder.BindSaver(saver);
            TestExecutionStateValidationService service = new StubTestExecutionStateValidationService(
                isPlaying: false,
                saver: saver,
                discarder: discarder);

            ValidationResult result = service.Validate(UnityCliLoopTestMode.PlayMode, RunTestsUnsavedChangesMode.discard);

            Assert.That(result.IsValid, Is.True);
            Assert.That(result.ErrorMessage, Is.Null);
            Assert.That(saver.SaveCallCount, Is.EqualTo(0));
            Assert.That(discarder.DiscardCallCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies discard mode returns the discard-failure message and the failed change list.
        /// </summary>
        [Test]
        public void Validate_WithDiscardModeWhenDiscardFails_ShouldReturnFailureWithFailedChanges()
        {
            string[] unsavedEditorChanges =
            {
                "Scene: Untitled scene"
            };
            FakeEditorUnsavedChangesQuietSaver saver = new FakeEditorUnsavedChangesQuietSaver(
                unsavedEditorChanges,
                Array.Empty<string>());
            FakeEditorUnsavedChangesDiscarder discarder = new FakeEditorUnsavedChangesDiscarder(unsavedEditorChanges);
            TestExecutionStateValidationService service = new StubTestExecutionStateValidationService(
                isPlaying: false,
                saver: saver,
                discarder: discarder);

            ValidationResult result = service.Validate(UnityCliLoopTestMode.PlayMode, RunTestsUnsavedChangesMode.discard);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("Tests cannot discard unsaved scene or prefab changes before running tests."));
            Assert.That(result.ErrorMessage, Does.Contain("Scene: Untitled scene"));
            Assert.That(saver.SaveCallCount, Is.EqualTo(0));
            Assert.That(discarder.DiscardCallCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Test support type used by editor and play mode fixtures.
        /// </summary>
        private sealed class StubTestExecutionStateValidationService : TestExecutionStateValidationService
        {
            private readonly bool _isPlaying;
            private readonly bool _isPaused;
            private readonly bool _isCompiling;
            private readonly bool _isUpdating;

            public StubTestExecutionStateValidationService(
                bool isPlaying,
                bool isPaused = false,
                bool isCompiling = false,
                bool isUpdating = false,
                FakeEditorUnsavedChangesQuietSaver saver = null,
                FakeEditorUnsavedChangesDiscarder discarder = null)
                : base(
                    saver ?? new FakeEditorUnsavedChangesQuietSaver(Array.Empty<string>(), Array.Empty<string>()),
                    discarder ?? new FakeEditorUnsavedChangesDiscarder(Array.Empty<string>()))
            {
                _isPlaying = isPlaying;
                _isPaused = isPaused;
                _isCompiling = isCompiling;
                _isUpdating = isUpdating;
            }

            protected override bool IsPlaying => _isPlaying;
            protected override bool IsPaused => _isPaused;
            protected override bool IsCompiling => _isCompiling;
            protected override bool IsUpdating => _isUpdating;
        }

        /// <summary>
        /// Fake saver that records save calls and optionally clears detected dirty items after a successful save.
        /// </summary>
        private sealed class FakeEditorUnsavedChangesQuietSaver : IEditorUnsavedChangesQuietSaver
        {
            private string[] _detectedChanges;
            private readonly string[] _saveFailures;

            public int SaveCallCount { get; private set; }

            public FakeEditorUnsavedChangesQuietSaver(string[] detectedChanges, string[] saveFailures)
            {
                _detectedChanges = detectedChanges;
                _saveFailures = saveFailures;
            }

            public void ClearDetectedChanges()
            {
                _detectedChanges = Array.Empty<string>();
            }

            public string[] DetectUnsavedEditorChanges()
            {
                return _detectedChanges;
            }

            public string[] SaveUnsavedEditorChanges()
            {
                SaveCallCount++;
                if (_saveFailures.Length == 0)
                {
                    ClearDetectedChanges();
                }

                return _saveFailures;
            }
        }

        /// <summary>
        /// Fake discarder that records discard calls and optionally clears the bound saver's dirty items.
        /// </summary>
        private sealed class FakeEditorUnsavedChangesDiscarder : IEditorUnsavedChangesDiscarder
        {
            private readonly string[] _discardFailures;
            private FakeEditorUnsavedChangesQuietSaver _saver;

            public int DiscardCallCount { get; private set; }

            public FakeEditorUnsavedChangesDiscarder(string[] discardFailures)
            {
                _discardFailures = discardFailures;
            }

            public void BindSaver(FakeEditorUnsavedChangesQuietSaver saver)
            {
                _saver = saver;
            }

            public string[] DiscardUnsavedEditorChanges()
            {
                DiscardCallCount++;
                if (_discardFailures.Length == 0 && _saver != null)
                {
                    _saver.ClearDetectedChanges();
                }

                return _discardFailures;
            }
        }
    }
}
