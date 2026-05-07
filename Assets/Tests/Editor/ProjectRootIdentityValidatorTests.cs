using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies Project Root Identity Validator behavior.
    /// </summary>
    [TestFixture]
    public sealed class ProjectRootIdentityValidatorTests
    {
        [Test]
        public void Validate_WhenExpectedProjectRootIsMissing_ReturnsInvalidResult()
        {
            // Verifies that missing expected project roots are rejected before request execution.
            ProjectRootIdentityValidationResult result = ProjectRootIdentityValidator.Validate(string.Empty, "/project");

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("expectedProjectRoot is required"));
        }

        [Test]
        public void Validate_WhenActualProjectRootIsUnavailable_ReturnsInvalidResult()
        {
            // Verifies that unavailable actual project roots fail closed instead of allowing execution.
            ProjectRootIdentityValidationResult result = ProjectRootIdentityValidator.Validate("/project", string.Empty);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("Fast project validation is unavailable"));
        }

        [Test]
        public void Validate_WhenProjectRootDiffers_ReturnsInvalidResult()
        {
            // Verifies that a CLI request for another project cannot execute against this Unity instance.
            ProjectRootIdentityValidationResult result = ProjectRootIdentityValidator.Validate("/project-a", "/project-b");

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("different project"));
        }

        [Test]
        public void Validate_WhenProjectRootMatches_ReturnsValidResult()
        {
            // Verifies that matching project identity allows request execution to continue.
            ProjectRootIdentityValidationResult result = ProjectRootIdentityValidator.Validate("/project", "/project");

            Assert.That(result.IsValid, Is.True);
        }
    }
}
