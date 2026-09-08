using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies compile Warning text for each Play-at-request-start branch: Edit Mode with and
    /// without enabled pause points, Play without pause points, and Play with enabled pause points.
    /// </summary>
    [TestFixture]
    public sealed class CompilePlayModeStopWarningBuilderTests
    {
        /// <summary>
        /// What: Edit Mode with enabled pause points warns about the drop without claiming Play Mode was active.
        /// </summary>
        [Test]
        public void BuildWarning_WhenNotPlayingWithActivePausePoints_ReturnsEditModeDropWarning()
        {
            string warning = CompilePlayModeStopWarningBuilder.BuildWarning(
                wasPlayingAtRequestStart: false,
                activePausePointCount: 2,
                activeHotReloadChangeCount: 0);

            Assert.That(warning, Is.Not.Null);
            Assert.That(warning, Does.Contain("2 enabled pause point(s)"));
            Assert.That(
                warning,
                Does.Not.Contain("Play Mode was active"),
                "An Edit Mode compile must not claim Play Mode was running.");
        }

        /// <summary>
        /// What: Edit Mode with both pause points and hot-reload changes joins the pause point sentence first.
        /// </summary>
        [Test]
        public void BuildWarning_WhenNotPlayingWithPausePointsAndHotReloadChanges_JoinsPausePointSentenceFirst()
        {
            string pausePointOnly = CompilePlayModeStopWarningBuilder.BuildWarning(
                wasPlayingAtRequestStart: false,
                activePausePointCount: 2,
                activeHotReloadChangeCount: 0);
            string hotReloadOnly = CompilePlayModeStopWarningBuilder.BuildWarning(
                wasPlayingAtRequestStart: false,
                activePausePointCount: 0,
                activeHotReloadChangeCount: 3);

            string warning = CompilePlayModeStopWarningBuilder.BuildWarning(
                wasPlayingAtRequestStart: false,
                activePausePointCount: 2,
                activeHotReloadChangeCount: 3);

            Assert.That(warning, Is.EqualTo(pausePointOnly + " " + hotReloadOnly));
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
            Assert.That(
                warning,
                Does.Contain("drops every hot-reload change, introduced types included"),
                "A caller must not read the warning as covering patched methods only.");
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
