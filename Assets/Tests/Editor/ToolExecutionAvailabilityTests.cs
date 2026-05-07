using NUnit.Framework;

namespace io.github.hatayama.uLoopMCP.Tests.Editor
{
    public class ToolExecutionAvailabilityTests
    {
        [Test]
        public void ShouldReportDependencyUnavailableBeforeDisabled_WhenRunTestsDependencyIsMissing_ReturnsTrue()
        {
            bool shouldReportDependency = ToolExecutionAvailability
                .ShouldReportDependencyUnavailableBeforeDisabled(
                    McpConstants.TOOL_NAME_RUN_TESTS,
                    isTestFrameworkAvailable: false);

            Assert.That(shouldReportDependency, Is.True);
        }

        [Test]
        public void ShouldReportDependencyUnavailableBeforeDisabled_WhenRunTestsDependencyExists_ReturnsFalse()
        {
            bool shouldReportDependency = ToolExecutionAvailability
                .ShouldReportDependencyUnavailableBeforeDisabled(
                    McpConstants.TOOL_NAME_RUN_TESTS,
                    isTestFrameworkAvailable: true);

            Assert.That(shouldReportDependency, Is.False);
        }

        [Test]
        public void ShouldReportDependencyUnavailableBeforeDisabled_WhenOtherToolIsDisabled_ReturnsFalse()
        {
            bool shouldReportDependency = ToolExecutionAvailability
                .ShouldReportDependencyUnavailableBeforeDisabled(
                    "compile",
                    isTestFrameworkAvailable: false);

            Assert.That(shouldReportDependency, Is.False);
        }

        [Test]
        public void ShouldExposeInRegisteredTools_WhenRunTestsDisabledAndDependencyIsMissing_ReturnsTrue()
        {
            bool shouldExpose = ToolExecutionAvailability
                .ShouldExposeInRegisteredTools(
                    McpConstants.TOOL_NAME_RUN_TESTS,
                    isToolEnabled: false,
                    isTestFrameworkAvailable: false);

            Assert.That(shouldExpose, Is.True);
        }

        [Test]
        public void ShouldExposeInRegisteredTools_WhenRunTestsDisabledAndDependencyExists_ReturnsFalse()
        {
            bool shouldExpose = ToolExecutionAvailability
                .ShouldExposeInRegisteredTools(
                    McpConstants.TOOL_NAME_RUN_TESTS,
                    isToolEnabled: false,
                    isTestFrameworkAvailable: true);

            Assert.That(shouldExpose, Is.False);
        }

        [Test]
        public void ShouldExposeInRegisteredTools_WhenOtherToolIsEnabled_ReturnsTrue()
        {
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
            bool shouldExpose = ToolExecutionAvailability
                .ShouldExposeInRegisteredTools(
                    "compile",
                    isToolEnabled: false,
                    isTestFrameworkAvailable: false);

            Assert.That(shouldExpose, Is.False);
        }
    }
}
