using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies dependency-aware tool availability behavior.
    /// </summary>
    public class ToolExecutionAvailabilityTests
    {
        [Test]
        public void ShouldReportDependencyUnavailableBeforeDisabled_WhenRunTestsDependencyIsMissing_ReturnsTrue()
        {
            // Verifies that run-tests can explain its missing dependency before disabled settings hide it.
            bool shouldReportDependency = ToolExecutionAvailability
                .ShouldReportDependencyUnavailableBeforeDisabled(
                    UnityCliLoopConstants.TOOL_NAME_RUN_TESTS,
                    isTestFrameworkAvailable: false);

            Assert.That(shouldReportDependency, Is.True);
        }

        [Test]
        public void ShouldReportDependencyUnavailableBeforeDisabled_WhenRunTestsDependencyExists_ReturnsFalse()
        {
            // Verifies that normal disabled-tool behavior applies when Unity Test Framework is installed.
            bool shouldReportDependency = ToolExecutionAvailability
                .ShouldReportDependencyUnavailableBeforeDisabled(
                    UnityCliLoopConstants.TOOL_NAME_RUN_TESTS,
                    isTestFrameworkAvailable: true);

            Assert.That(shouldReportDependency, Is.False);
        }

        [Test]
        public void ShouldReportDependencyUnavailableBeforeDisabled_WhenOtherToolIsDisabled_ReturnsFalse()
        {
            // Verifies that dependency bypass behavior stays scoped to run-tests.
            bool shouldReportDependency = ToolExecutionAvailability
                .ShouldReportDependencyUnavailableBeforeDisabled(
                    "compile",
                    isTestFrameworkAvailable: false);

            Assert.That(shouldReportDependency, Is.False);
        }

        [Test]
        public void ShouldExposeInRegisteredTools_WhenRunTestsDisabledAndDependencyIsMissing_ReturnsTrue()
        {
            // Verifies that run-tests stays discoverable when it needs to return the dependency error.
            bool shouldExpose = ToolExecutionAvailability
                .ShouldExposeInRegisteredTools(
                    UnityCliLoopConstants.TOOL_NAME_RUN_TESTS,
                    isToolEnabled: false,
                    isTestFrameworkAvailable: false);

            Assert.That(shouldExpose, Is.True);
        }

        [Test]
        public void ShouldExposeInRegisteredTools_WhenRunTestsDisabledAndDependencyExists_ReturnsFalse()
        {
            // Verifies that disabled run-tests is hidden after its dependency is available.
            bool shouldExpose = ToolExecutionAvailability
                .ShouldExposeInRegisteredTools(
                    UnityCliLoopConstants.TOOL_NAME_RUN_TESTS,
                    isToolEnabled: false,
                    isTestFrameworkAvailable: true);

            Assert.That(shouldExpose, Is.False);
        }

        [Test]
        public void ShouldExposeInRegisteredTools_WhenOtherToolIsEnabled_ReturnsTrue()
        {
            // Verifies that regular enabled tools remain visible.
            bool shouldExpose = ToolExecutionAvailability
                .ShouldExposeInRegisteredTools(
                    "compile",
                    isToolEnabled: true,
                    isTestFrameworkAvailable: false);

            Assert.That(shouldExpose, Is.True);
        }

        [Test]
        public void ShouldExposeInRegisteredTools_WhenOtherToolIsDisabled_ReturnsFalse()
        {
            // Verifies that dependency bypass behavior does not make unrelated disabled tools visible.
            bool shouldExpose = ToolExecutionAvailability
                .ShouldExposeInRegisteredTools(
                    "compile",
                    isToolEnabled: false,
                    isTestFrameworkAvailable: false);

            Assert.That(shouldExpose, Is.False);
        }
    }
}
