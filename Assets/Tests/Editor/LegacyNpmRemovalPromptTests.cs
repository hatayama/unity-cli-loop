using NUnit.Framework;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    public class LegacyNpmRemovalPromptTests
    {
        [Test]
        public void ConfirmInstallCanProceed_WhenLegacyInstallIsMissingSkipsDialog()
        {
            // Verifies that clean installs do not interrupt the setup wizard.
            bool showedDialog = false;

            bool result = LegacyNpmRemovalPrompt.ConfirmInstallCanProceed(
                hasLegacyNpmInstallation: false,
                (title, message, ok, cancel) =>
                {
                    showedDialog = true;
                    return false;
                });

            Assert.That(result, Is.True);
            Assert.That(showedDialog, Is.False);
        }

        [Test]
        public void ConfirmInstallCanProceed_WhenLegacyInstallExistsUsesUserChoice()
        {
            // Verifies that setup asks before removing the old Node.js/npm CLI.
            bool showedDialog = false;

            bool result = LegacyNpmRemovalPrompt.ConfirmInstallCanProceed(
                hasLegacyNpmInstallation: true,
                (title, message, ok, cancel) =>
                {
                    showedDialog = true;
                    Assert.That(title, Does.Contain("Remove Old"));
                    Assert.That(message, Does.Contain("Node.js/npm"));
                    Assert.That(ok, Does.Contain("Remove"));
                    Assert.That(cancel, Is.EqualTo("Cancel"));
                    return true;
                });

            Assert.That(result, Is.True);
            Assert.That(showedDialog, Is.True);
        }

        [Test]
        public void ConfirmInstallCanProceed_WhenUserCancelsReturnsFalse()
        {
            // Verifies that setup does not install when the user rejects legacy removal.
            bool result = LegacyNpmRemovalPrompt.ConfirmInstallCanProceed(
                hasLegacyNpmInstallation: true,
                (title, message, ok, cancel) => false);

            Assert.That(result, Is.False);
        }
    }
}
