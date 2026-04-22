using NUnit.Framework;

namespace io.github.hatayama.uLoopMCP.Tests.Editor
{
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
