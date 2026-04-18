using NUnit.Framework;
using UnityEditor.TestTools.TestRunner.Api;

namespace io.github.hatayama.uLoopMCP
{
    public class TestExecutionStateValidationServiceTests
    {
        [Test]
        public void Validate_WithEditModeWhilePlaying_ShouldReturnFailure()
        {
            TestExecutionStateValidationService service = new StubTestExecutionStateValidationService(true);

            ValidationResult result = service.Validate(TestMode.EditMode);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo("EditMode tests cannot run during play mode"));
        }

        [Test]
        public void Validate_WithEditModeWhileNotPlaying_ShouldReturnSuccess()
        {
            TestExecutionStateValidationService service = new StubTestExecutionStateValidationService(false);

            ValidationResult result = service.Validate(TestMode.EditMode);

            Assert.That(result.IsValid, Is.True);
            Assert.That(result.ErrorMessage, Is.Null);
        }

        [Test]
        public void Validate_WithPlayModeWhilePlaying_ShouldReturnSuccess()
        {
            TestExecutionStateValidationService service = new StubTestExecutionStateValidationService(true);

            ValidationResult result = service.Validate(TestMode.PlayMode);

            Assert.That(result.IsValid, Is.True);
            Assert.That(result.ErrorMessage, Is.Null);
        }

        [Test]
        public void Validate_WhenCompilationIsInProgress_ShouldReturnFailure()
        {
            TestExecutionStateValidationService service = new StubTestExecutionStateValidationService(
                isPlaying: false,
                isCompiling: true);

            ValidationResult result = service.Validate(TestMode.EditMode);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo("Tests cannot run while compilation is in progress"));
        }

        [Test]
        public void Validate_WhenDomainReloadIsInProgress_ShouldReturnFailure()
        {
            TestExecutionStateValidationService service = new StubTestExecutionStateValidationService(
                isPlaying: false,
                isDomainReloadInProgress: true);

            ValidationResult result = service.Validate(TestMode.EditMode);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo("Tests cannot run while domain reload is in progress"));
        }

        [Test]
        public void Validate_WhenEditorIsUpdating_ShouldReturnFailure()
        {
            TestExecutionStateValidationService service = new StubTestExecutionStateValidationService(
                isPlaying: false,
                isUpdating: true);

            ValidationResult result = service.Validate(TestMode.EditMode);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo("Tests cannot run while the editor is updating"));
        }

        private sealed class StubTestExecutionStateValidationService : TestExecutionStateValidationService
        {
            private readonly bool _isPlaying;
            private readonly bool _isCompiling;
            private readonly bool _isDomainReloadInProgress;
            private readonly bool _isUpdating;

            public StubTestExecutionStateValidationService(
                bool isPlaying,
                bool isCompiling = false,
                bool isDomainReloadInProgress = false,
                bool isUpdating = false)
            {
                _isPlaying = isPlaying;
                _isCompiling = isCompiling;
                _isDomainReloadInProgress = isDomainReloadInProgress;
                _isUpdating = isUpdating;
            }

            protected override bool IsPlaying => _isPlaying;
            protected override bool IsCompiling => _isCompiling;
            protected override bool IsDomainReloadInProgress => _isDomainReloadInProgress;
            protected override bool IsUpdating => _isUpdating;
        }
    }
}
