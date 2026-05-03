using NUnit.Framework;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    public class McpEditorWindowCliActionTests
    {
        [TestCase(null, "3.0.0", true, false)]
        [TestCase("2.9.0", "3.0.0", true, false)]
        [TestCase("3.1.0", "3.0.0", true, false)]
        [TestCase("3.0.0", "3.0.0", true, true)]
        [TestCase("3.0.0", "3.0.0", false, false)]
        public void ShouldUninstallCliFromPrimaryButton_ReturnsExpectedAction(
            string cliVersion,
            string packageVersion,
            bool canUninstallCli,
            bool expected)
        {
            // Verifies that only same-version package-owned installs route the primary CLI button to uninstall.
            bool result = McpEditorWindow.ShouldUninstallCliFromPrimaryButton(
                cliVersion,
                packageVersion,
                canUninstallCli);

            Assert.That(result, Is.EqualTo(expected));
        }
    }
}
