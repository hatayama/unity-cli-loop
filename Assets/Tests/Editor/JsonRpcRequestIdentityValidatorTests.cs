using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies JSON RPC Request Identity Validator behavior.
    /// </summary>
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

            UnityCliLoopToolParameterValidationException exception = Assert.Throws<UnityCliLoopToolParameterValidationException>(() =>
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

            UnityCliLoopToolParameterValidationException exception = Assert.Throws<UnityCliLoopToolParameterValidationException>(() =>
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

            UnityCliLoopToolParameterValidationException exception = Assert.Throws<UnityCliLoopToolParameterValidationException>(() =>
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
