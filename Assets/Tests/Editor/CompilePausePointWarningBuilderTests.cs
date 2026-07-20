using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the compile Warning text only appears when Play Mode was active with at least
    /// one enabled pause point, and states the domain-reload loss when it does.
    /// </summary>
    [TestFixture]
    public sealed class CompilePausePointWarningBuilderTests
    {
        [Test]
        public void BuildWarning_WhenNotPlayingAtRequestStart_ReturnsNull()
        {
            // Verifies no warning is produced when Play Mode was not active, regardless of marker count.
            string warning = CompilePausePointWarningBuilder.BuildWarning(
                wasPlayingAtRequestStart: false,
                activePausePointCount: 3);

            Assert.That(warning, Is.Null);
        }

        [Test]
        public void BuildWarning_WhenPlayingButNoActivePausePoints_ReturnsNull()
        {
            // Verifies no warning is produced when Play Mode was active but no pause point is enabled.
            string warning = CompilePausePointWarningBuilder.BuildWarning(
                wasPlayingAtRequestStart: true,
                activePausePointCount: 0);

            Assert.That(warning, Is.Null);
        }

        [Test]
        public void BuildWarning_WhenPlayingWithActivePausePoints_MentionsCountAndDomainReloadLoss()
        {
            // Verifies the warning names the active count and explains what the domain reload discards.
            string warning = CompilePausePointWarningBuilder.BuildWarning(
                wasPlayingAtRequestStart: true,
                activePausePointCount: 2);

            Assert.That(warning, Does.Contain("2 enabled pause point"));
            Assert.That(warning, Does.Contain("Play session state"));
            Assert.That(warning, Does.Contain("pause point patches"));
        }
    }
}
