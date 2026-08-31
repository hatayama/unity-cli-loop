using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Presentation;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies CLI Install Refresh Policy behavior.
    /// </summary>
    public class CliInstallRefreshPolicyTests
    {
        [Test]
        public void ShouldRefreshSkillsAfterCliInstall_WhenCliWasNotInstalled_ReturnsTrue()
        {
            bool shouldRefresh = CliInstallRefreshPolicy.ShouldRefreshSkillsAfterCliInstall(
                wasCliInstalledBeforeInstall: false);

            Assert.That(shouldRefresh, Is.True);
        }

        [Test]
        public void ShouldRefreshSkillsAfterCliInstall_WhenCliWasAlreadyInstalled_ReturnsFalse()
        {
            bool shouldRefresh = CliInstallRefreshPolicy.ShouldRefreshSkillsAfterCliInstall(
                wasCliInstalledBeforeInstall: true);

            Assert.That(shouldRefresh, Is.False);
        }
    }
}
