using NUnit.Framework;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    public class McpEditorWindowCliActionTests
    {
        [TestCase(null, "3.0.0", true, false)]
        [TestCase("2.9.0", "3.0.0", true, false)]
        [TestCase("3.1.0", "3.0.0", true, true)]
        [TestCase("3.0.0", "3.0.0", true, true)]
        [TestCase("3.0.0", "3.0.0", false, false)]
        public void ShouldUninstallCliFromPrimaryButton_ReturnsExpectedAction(
            string cliVersion,
            string requiredDispatcherVersion,
            bool canUninstallCli,
            bool expected)
        {
            // Verifies that package-owned installs route to uninstall when the dispatcher satisfies core requirements.
            bool result = McpEditorWindow.ShouldUninstallCliFromPrimaryButton(
                cliVersion,
                requiredDispatcherVersion,
                canUninstallCli);

            Assert.That(result, Is.EqualTo(expected));
        }

        [TestCase("3.0.0-beta.0", "3.0.0-beta.1", true)]
        [TestCase("3.0.0-beta.1", "3.0.0-beta.1", false)]
        [TestCase("3.0.0", "3.0.0-beta.1", false)]
        public void IsCliUpdateNeeded_UsesRequiredDispatcherVersion(
            string cliVersion,
            string requiredDispatcherVersion,
            bool expected)
        {
            // Verifies that the settings UI ignores package version drift and only updates old dispatchers.
            bool result = McpEditorWindow.IsCliUpdateNeeded(cliVersion, requiredDispatcherVersion);

            Assert.That(result, Is.EqualTo(expected));
        }
    }
}
