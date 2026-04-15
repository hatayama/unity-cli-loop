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

        private sealed class StubTestExecutionStateValidationService : TestExecutionStateValidationService
        {
            private readonly bool _isPlaying;

            public StubTestExecutionStateValidationService(bool isPlaying)
            {
                _isPlaying = isPlaying;
            }

            protected override bool IsPlaying => _isPlaying;
        }
    }
}
