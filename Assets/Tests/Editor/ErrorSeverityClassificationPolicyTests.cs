using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests domain severity decisions after application-specific errors are categorized.
    /// </summary>
    public sealed class ErrorSeverityClassificationPolicyTests
    {
        [Test]
        public void DetermineSeverity_WhenCategoryIsSecurityViolation_ReturnsHigh()
        {
            // Verifies security failures stay highly visible to users.
            ErrorSeverity severity = ErrorSeverityClassificationPolicy.DetermineSeverity(
                ErrorSeverityCategory.SecurityViolation);

            Assert.That(severity, Is.EqualTo(ErrorSeverity.High));
        }

        [Test]
        public void DetermineSeverity_WhenCategoryIsRecoverableExecutionState_ReturnsMedium()
        {
            // Verifies busy, timeout, and disabled-tool states share the recoverable severity.
            ErrorSeverity severity = ErrorSeverityClassificationPolicy.DetermineSeverity(
                ErrorSeverityCategory.RecoverableExecutionState);

            Assert.That(severity, Is.EqualTo(ErrorSeverity.Medium));
        }

        [Test]
        public void DetermineSeverity_WhenCategoryIsParameterValidation_ReturnsLow()
        {
            // Verifies user-correctable parameter issues stay at low severity.
            ErrorSeverity severity = ErrorSeverityClassificationPolicy.DetermineSeverity(
                ErrorSeverityCategory.ParameterValidation);

            Assert.That(severity, Is.EqualTo(ErrorSeverity.Low));
        }

        [Test]
        public void DetermineSeverity_WhenCategoryIsUnknown_ReturnsHigh()
        {
            // Verifies unexpected categories fail closed as high severity.
            ErrorSeverity severity = ErrorSeverityClassificationPolicy.DetermineSeverity(
                (ErrorSeverityCategory)999);

            Assert.That(severity, Is.EqualTo(ErrorSeverity.High));
        }
    }
}
