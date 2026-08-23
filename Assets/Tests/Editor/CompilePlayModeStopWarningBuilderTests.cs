using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies compile Warning text for each Play-at-request-start branch: none, Play without
    /// pause points, and Play with enabled pause points.
    /// </summary>
    [TestFixture]
    public sealed class CompilePlayModeStopWarningBuilderTests
    {
        /// <summary>
        /// What: no warning when Play Mode was not active, regardless of marker count.
        /// </summary>
        [Test]
        public void BuildWarning_WhenNotPlayingAtRequestStart_ReturnsNull()
        {
            string warning = CompilePlayModeStopWarningBuilder.BuildWarning(
                wasPlayingAtRequestStart: false,
                activePausePointCount: 3);

            Assert.That(warning, Is.Null);
        }

        /// <summary>
        /// What: Play without enabled pause points warns that compile stops Play and discards session state.
        /// </summary>
        [Test]
        public void BuildWarning_WhenPlayingButNoActivePausePoints_ReturnsPlaySessionDiscardWarning()
        {
            string warning = CompilePlayModeStopWarningBuilder.BuildWarning(
                wasPlayingAtRequestStart: true,
                activePausePointCount: 0);

            Assert.That(
                warning,
                Is.EqualTo(
                    "Play Mode was active when this compile was requested. The compile stops Play Mode and the domain reload discards the Play session state — re-establish your runtime state before continuing verification."));
        }

        /// <summary>
        /// What: Play with enabled pause points keeps the existing count-and-patch-loss wording exactly.
        /// </summary>
        [Test]
        public void BuildWarning_WhenPlayingWithActivePausePoints_ReturnsExistingPausePointWording()
        {
            string warning = CompilePlayModeStopWarningBuilder.BuildWarning(
                wasPlayingAtRequestStart: true,
                activePausePointCount: 2);

            Assert.That(
                warning,
                Is.EqualTo(
                    "Play Mode was active with 2 enabled pause point(s). The compile stops Play Mode and the domain reload discards the Play session state and all pause point patches — re-enable pause points after the compile completes."));
        }
    }
}
