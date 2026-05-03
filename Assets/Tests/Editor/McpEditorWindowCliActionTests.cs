using NUnit.Framework;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    public class McpEditorWindowCliActionTests
    {
        [TestCase(null, "3.0.0", false)]
        [TestCase("2.9.0", "3.0.0", false)]
        [TestCase("3.1.0", "3.0.0", false)]
        [TestCase("3.0.0", "3.0.0", true)]
        public void ShouldUninstallCliFromPrimaryButton_ReturnsExpectedAction(
            string cliVersion,
            string packageVersion,
            bool expected)
        {
            // Verifies that only same-version installs route the primary CLI button to uninstall.
            bool result = McpEditorWindow.ShouldUninstallCliFromPrimaryButton(
                cliVersion,
                packageVersion);

            Assert.That(result, Is.EqualTo(expected));
        }
    }
}
