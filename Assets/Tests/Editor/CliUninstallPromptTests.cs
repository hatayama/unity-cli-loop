using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Presentation;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies CLI Uninstall Prompt behavior.
    /// </summary>
    public class CliUninstallPromptTests
    {
        [Test]
        public void ConfirmUninstall_WhenUserAcceptsReturnsTrue()
        {
            // Verifies that the uninstall confirmation can proceed on OK.
            bool showedDialog = false;

            bool result = CliUninstallPrompt.ConfirmUninstall(
                (title, message, ok, cancel) =>
                {
                    showedDialog = true;
                    Assert.That(title, Does.Contain("Uninstall"));
                    Assert.That(message, Does.Contain("Project-local"));
                    Assert.That(message, Does.Not.Contain("Windows User PATH"));
                    Assert.That(ok, Is.EqualTo("OK"));
                    Assert.That(cancel, Is.EqualTo("Cancel"));
                    return true;
                });

            Assert.That(result, Is.True);
            Assert.That(showedDialog, Is.True);
        }

        [Test]
        public void ConfirmUninstall_WhenUserCancelsReturnsFalse()
        {
            // Verifies that uninstall is skipped when the dialog is cancelled.
            bool result = CliUninstallPrompt.ConfirmUninstall(
                (title, message, ok, cancel) => false);

            Assert.That(result, Is.False);
        }
    }
}
