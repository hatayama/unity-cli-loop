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
                activePausePointCount: 3,
                activeHotReloadChangeCount: 0);

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
                activePausePointCount: 0,
                activeHotReloadChangeCount: 0);

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
                activePausePointCount: 2,
                activeHotReloadChangeCount: 0);

            Assert.That(
                warning,
                Is.EqualTo(
                    "Play Mode was active with 2 enabled pause point(s). The compile stops Play Mode and the domain reload discards the Play session state and all pause point patches — re-enable pause points after the compile completes."));
        }

        /// <summary>
        /// What: not playing with active hot-reload changes warns only about the domain reload dropping those patches.
        /// </summary>
        [Test]
        public void BuildWarning_WhenNotPlayingAndHotReloadChangesActive_ReturnsHotReloadDropWarning()
        {
            string warning = CompilePlayModeStopWarningBuilder.BuildWarning(
                wasPlayingAtRequestStart: false,
                activePausePointCount: 0,
                activeHotReloadChangeCount: 3);

            Assert.That(warning, Does.Contain("3 active hot-reload change(s)"));
            Assert.That(warning, Does.Not.Contain("Play Mode"));
        }

        /// <summary>
        /// What: Play plus active hot-reload changes keeps the Play sentence and appends the drop sentence.
        /// </summary>
        [Test]
        public void BuildWarning_WhenPlayingAndHotReloadChangesActive_ReturnsBothSentences()
        {
            string warning = CompilePlayModeStopWarningBuilder.BuildWarning(
                wasPlayingAtRequestStart: true,
                activePausePointCount: 0,
                activeHotReloadChangeCount: 2);

            Assert.That(warning, Does.Contain("Play Mode was active"));
            Assert.That(warning, Does.Contain("2 active hot-reload change(s)"));
        }

        /// <summary>
        /// What: no Play and no hot-reload changes produces no Warning.
        /// </summary>
        [Test]
        public void BuildWarning_WhenNothingActive_ReturnsNull()
        {
            string warning = CompilePlayModeStopWarningBuilder.BuildWarning(
                wasPlayingAtRequestStart: false,
                activePausePointCount: 0,
                activeHotReloadChangeCount: 0);

            Assert.That(warning, Is.Null);
        }
    }
}
