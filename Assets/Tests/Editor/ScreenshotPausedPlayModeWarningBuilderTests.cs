using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the paused Play Mode Warning is appended only for playing + paused + an image capture.
    /// </summary>
    public sealed class ScreenshotPausedPlayModeWarningBuilderTests
    {
        /// <summary>
        /// What: an image capture while Play Mode is paused returns the stale-UGUI warning with the Step hint.
        /// </summary>
        [Test]
        public void Append_WhenPausedWithImageAndNoExistingWarning_ReturnsPausedWarning()
        {
            string warning = ScreenshotPausedPlayModeWarningBuilder.Append(string.Empty, true, true, false, 1);

            Assert.That(warning, Is.EqualTo(ScreenshotPausedPlayModeWarningBuilder.PausedWarning));
            Assert.That(warning, Does.Contain("control-play-mode --action Step"));
        }

        /// <summary>
        /// What: an existing chrome warning is kept and the paused warning is appended after it.
        /// </summary>
        [Test]
        public void Append_WhenPausedWithExistingWarning_AppendsAfterExistingWarning()
        {
            string warning = ScreenshotPausedPlayModeWarningBuilder.Append("Chrome warning.", true, true, false, 1);

            Assert.That(warning, Is.EqualTo("Chrome warning. " + ScreenshotPausedPlayModeWarningBuilder.PausedWarning));
        }

        /// <summary>
        /// What: a capture while Play Mode runs unpaused leaves the existing warning untouched.
        /// </summary>
        [Test]
        public void Append_WhenPlayingButNotPaused_ReturnsExistingWarning()
        {
            string warning = ScreenshotPausedPlayModeWarningBuilder.Append("Chrome warning.", true, false, false, 1);

            Assert.That(warning, Is.EqualTo("Chrome warning."));
        }

        /// <summary>
        /// What: a paused flag outside Play Mode (Edit Mode capture) does not warn.
        /// </summary>
        [Test]
        public void Append_WhenPausedInEditMode_ReturnsExistingWarning()
        {
            string warning = ScreenshotPausedPlayModeWarningBuilder.Append(string.Empty, false, true, false, 1);

            Assert.That(warning, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// What: an elements-only response writes no image, so no stale-image warning is added.
        /// </summary>
        [Test]
        public void Append_WhenElementsOnly_ReturnsExistingWarning()
        {
            string warning = ScreenshotPausedPlayModeWarningBuilder.Append(string.Empty, true, true, true, 1);

            Assert.That(warning, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// What: a paused capture that saved zero images does not warn about images that do not exist.
        /// </summary>
        [Test]
        public void Append_WhenPausedWithZeroImages_ReturnsExistingWarning()
        {
            string warning = ScreenshotPausedPlayModeWarningBuilder.Append(string.Empty, true, true, false, 0);

            Assert.That(warning, Is.EqualTo(string.Empty));
        }
    }
}
