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
                JsonRpcRequestIdentityValidator.Validate(null, "/project", "session-1"));
        }

        [Test]
        public void Validate_WhenExpectedProjectRootIsMissing_ShouldThrow()
        {
            JsonRpcRequestUloopMetadata metadata = new()
            {
                ExpectedProjectRoot = string.Empty,
                ExpectedServerSessionId = "session-1"
            };

            ParameterValidationException exception = Assert.Throws<ParameterValidationException>(() =>
                JsonRpcRequestIdentityValidator.Validate(metadata, "/project", "session-1"));

            Assert.That(exception.Message, Does.Contain("expectedProjectRoot is required"));
        }

        [Test]
        public void Validate_WhenExpectedServerSessionIdIsMissing_ShouldThrow()
        {
            JsonRpcRequestUloopMetadata metadata = new()
            {
                ExpectedProjectRoot = "/project",
                ExpectedServerSessionId = string.Empty
            };

            ParameterValidationException exception = Assert.Throws<ParameterValidationException>(() =>
                JsonRpcRequestIdentityValidator.Validate(metadata, "/project", "session-1"));

            Assert.That(exception.Message, Does.Contain("expectedServerSessionId is required"));
        }

        [Test]
        public void Validate_WhenActualProjectRootIsUnavailable_ShouldThrow()
        {
            JsonRpcRequestUloopMetadata metadata = new()
            {
                ExpectedProjectRoot = "/project",
                ExpectedServerSessionId = "session-1"
            };

            ParameterValidationException exception = Assert.Throws<ParameterValidationException>(() =>
                JsonRpcRequestIdentityValidator.Validate(metadata, string.Empty, "session-1"));

            Assert.That(exception.Message, Does.Contain("Fast project validation is unavailable"));
        }

        [Test]
        public void Validate_WhenActualServerSessionIdIsUnavailable_ShouldThrow()
        {
            JsonRpcRequestUloopMetadata metadata = new()
            {
                ExpectedProjectRoot = "/project",
                ExpectedServerSessionId = "session-1"
            };

            ParameterValidationException exception = Assert.Throws<ParameterValidationException>(() =>
                JsonRpcRequestIdentityValidator.Validate(metadata, "/project", string.Empty));

            Assert.That(exception.Message, Does.Contain("server session changed"));
        }

        [Test]
        public void Validate_WhenProjectRootDiffers_ShouldThrow()
        {
            JsonRpcRequestUloopMetadata metadata = new()
            {
                ExpectedProjectRoot = "/project-a",
                ExpectedServerSessionId = "session-1"
            };

            ParameterValidationException exception = Assert.Throws<ParameterValidationException>(() =>
                JsonRpcRequestIdentityValidator.Validate(metadata, "/project-b", "session-1"));

            Assert.That(exception.Message, Does.Contain("different project"));
        }

        [Test]
        public void Validate_WhenServerSessionDiffers_ShouldThrow()
        {
            JsonRpcRequestUloopMetadata metadata = new()
            {
                ExpectedProjectRoot = "/project",
                ExpectedServerSessionId = "session-1"
            };

            ParameterValidationException exception = Assert.Throws<ParameterValidationException>(() =>
                JsonRpcRequestIdentityValidator.Validate(metadata, "/project", "session-2"));

            Assert.That(exception.Message, Does.Contain("server session changed"));
        }

        [Test]
        public void Validate_WhenMetadataMatchesCurrentServerIdentity_ShouldSucceed()
        {
            JsonRpcRequestUloopMetadata metadata = new()
            {
                ExpectedProjectRoot = "/project",
                ExpectedServerSessionId = "session-1"
            };

            Assert.DoesNotThrow(() =>
                JsonRpcRequestIdentityValidator.Validate(metadata, "/project", "session-1"));
        }

        [Test]
        public void IsExpectedRetryableFailure_WhenServerSessionChanges_ShouldReturnTrue()
        {
            ParameterValidationException exception =
                new ParameterValidationException(JsonRpcRequestIdentityValidator.ServerSessionChangedMessage);

            bool result = JsonRpcRequestIdentityValidator.IsExpectedRetryableFailure(exception);

            Assert.That(result, Is.True);
        }

        [Test]
        public void IsExpectedRetryableFailure_WhenValidationErrorIsUnexpected_ShouldReturnFalse()
        {
            ParameterValidationException exception =
                new ParameterValidationException("Invalid x-uloop metadata: expectedProjectRoot is required.");

            bool result = JsonRpcRequestIdentityValidator.IsExpectedRetryableFailure(exception);

            Assert.That(result, Is.False);
        }
    }
}
