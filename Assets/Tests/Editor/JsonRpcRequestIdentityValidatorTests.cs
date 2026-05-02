using NUnit.Framework;

namespace io.github.hatayama.uLoopMCP.Tests.Editor
{
    [TestFixture]
    public class JsonRpcRequestIdentityValidatorTests
    {
        [Test]
        public void Validate_WhenMetadataIsNull_ShouldSucceed()
        {
            Assert.DoesNotThrow(() =>
                JsonRpcRequestIdentityValidator.Validate(null, "/project"));
        }

        [Test]
        public void Validate_WhenExpectedProjectRootIsMissing_ShouldThrow()
        {
            JsonRpcRequestUloopMetadata metadata = new()
            {
                ExpectedProjectRoot = string.Empty
            };

            ParameterValidationException exception = Assert.Throws<ParameterValidationException>(() =>
                JsonRpcRequestIdentityValidator.Validate(metadata, "/project"));

            Assert.That(exception.Message, Does.Contain("expectedProjectRoot is required"));
        }

        [Test]
        public void Validate_WhenActualProjectRootIsUnavailable_ShouldThrow()
        {
            JsonRpcRequestUloopMetadata metadata = new()
            {
                ExpectedProjectRoot = "/project"
            };

            ParameterValidationException exception = Assert.Throws<ParameterValidationException>(() =>
                JsonRpcRequestIdentityValidator.Validate(metadata, string.Empty));

            Assert.That(exception.Message, Does.Contain("Fast project validation is unavailable"));
        }

        [Test]
        public void Validate_WhenProjectRootDiffers_ShouldThrow()
        {
            JsonRpcRequestUloopMetadata metadata = new()
            {
                ExpectedProjectRoot = "/project-a"
            };

            ParameterValidationException exception = Assert.Throws<ParameterValidationException>(() =>
                JsonRpcRequestIdentityValidator.Validate(metadata, "/project-b"));

            Assert.That(exception.Message, Does.Contain("different project"));
        }

        [Test]
        public void Validate_WhenProjectRootMatchesCurrentProject_ShouldSucceed()
        {
            JsonRpcRequestUloopMetadata metadata = new()
            {
                ExpectedProjectRoot = "/project"
            };

            Assert.DoesNotThrow(() =>
                JsonRpcRequestIdentityValidator.Validate(metadata, "/project"));
        }
    }
}
