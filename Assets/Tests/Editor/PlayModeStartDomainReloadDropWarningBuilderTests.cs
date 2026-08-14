using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the Play-start domain-reload drop Warning only appears when entering Play from
    /// Edit with Domain Reload enabled and at least one patch or pause point to discard.
    /// </summary>
    [TestFixture]
    public sealed class PlayModeStartDomainReloadDropWarningBuilderTests
    {
        /// <summary>
        /// Verifies resume (already playing) never warns: that path does not domain-reload.
        /// </summary>
        [Test]
        public void BuildWarning_WhenAlreadyPlaying_ReturnsNull()
        {
            string warning = PlayModeStartDomainReloadDropWarningBuilder.BuildWarning(
                wasPlayingAtRequestStart: true,
                isDomainReloadDisabledOnEnterPlayMode: false,
                activeHotReloadPatchCount: 2,
                activePausePointCount: 3);

            Assert.That(warning, Is.Null);
        }

        /// <summary>
        /// Verifies no warning when Enter Play Mode Options disable Domain Reload.
        /// </summary>
        [Test]
        public void BuildWarning_WhenDomainReloadDisabled_ReturnsNull()
        {
            string warning = PlayModeStartDomainReloadDropWarningBuilder.BuildWarning(
                wasPlayingAtRequestStart: false,
                isDomainReloadDisabledOnEnterPlayMode: true,
                activeHotReloadPatchCount: 2,
                activePausePointCount: 3);

            Assert.That(warning, Is.Null);
        }

        /// <summary>
        /// Verifies no warning when there are no patches and no pause points to discard.
        /// </summary>
        [Test]
        public void BuildWarning_WhenBothCountsAreZero_ReturnsNull()
        {
            string warning = PlayModeStartDomainReloadDropWarningBuilder.BuildWarning(
                wasPlayingAtRequestStart: false,
                isDomainReloadDisabledOnEnterPlayMode: false,
                activeHotReloadPatchCount: 0,
                activePausePointCount: 0);

            Assert.That(warning, Is.Null);
        }

        /// <summary>
        /// Verifies the patch-only warning matches the specified wording and count.
        /// </summary>
        [Test]
        public void BuildWarning_WhenOnlyPatchesExist_ReturnsPatchOnlyWording()
        {
            string warning = PlayModeStartDomainReloadDropWarningBuilder.BuildWarning(
                wasPlayingAtRequestStart: false,
                isDomainReloadDisabledOnEnterPlayMode: false,
                activeHotReloadPatchCount: 2,
                activePausePointCount: 0);

            Assert.That(
                warning,
                Is.EqualTo(
                    "Entering Play Mode triggers a domain reload that will discard 2 active hot-reload change(s). The new session runs the last compiled assemblies, so hot-reloaded edits that were never compiled are not in effect — run `uloop compile` before Play to keep them, or re-apply `uloop hot-reload` after Play Mode starts."));
        }

        /// <summary>
        /// Verifies the pause-point-only warning matches the specified wording and count.
        /// </summary>
        [Test]
        public void BuildWarning_WhenOnlyPausePointsExist_ReturnsPausePointOnlyWording()
        {
            string warning = PlayModeStartDomainReloadDropWarningBuilder.BuildWarning(
                wasPlayingAtRequestStart: false,
                isDomainReloadDisabledOnEnterPlayMode: false,
                activeHotReloadPatchCount: 0,
                activePausePointCount: 3);

            Assert.That(
                warning,
                Is.EqualTo(
                    "Entering Play Mode triggers a domain reload that will discard 3 enabled pause point(s). Re-enable them after Play Mode starts."));
        }

        /// <summary>
        /// Verifies the combined warning matches the specified wording and both counts.
        /// </summary>
        [Test]
        public void BuildWarning_WhenPatchesAndPausePointsExist_ReturnsCombinedWording()
        {
            string warning = PlayModeStartDomainReloadDropWarningBuilder.BuildWarning(
                wasPlayingAtRequestStart: false,
                isDomainReloadDisabledOnEnterPlayMode: false,
                activeHotReloadPatchCount: 2,
                activePausePointCount: 3);

            Assert.That(
                warning,
                Is.EqualTo(
                    "Entering Play Mode triggers a domain reload that will discard 2 active hot-reload change(s) and 3 enabled pause point(s). The new session runs the last compiled assemblies, so hot-reloaded edits that were never compiled are not in effect — run `uloop compile` before Play to keep them, or re-apply `uloop hot-reload` and re-enable pause points after Play Mode starts."));
        }
    }
}
