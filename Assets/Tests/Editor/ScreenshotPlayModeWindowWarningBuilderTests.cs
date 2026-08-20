using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the Play Mode window-capture Warning is emitted only for window + playing.
    /// </summary>
    public sealed class ScreenshotPlayModeWindowWarningBuilderTests
    {
        /// <summary>
        /// What: a window capture during Play Mode warns that Editor chrome is included.
        /// </summary>
        [Test]
        public void Build_WhenWindowCaptureDuringPlayMode_ReturnsChromeWarning()
        {
            string warning = ScreenshotPlayModeWindowWarningBuilder.Build(CaptureMode.window, true);

            Assert.That(
                warning,
                Is.EqualTo(
                    "This window capture includes Unity Editor chrome. If you wanted the Game View image (typical during Play Mode), re-run with --capture-mode rendering."));
        }

        /// <summary>
        /// What: a window capture in Edit Mode does not warn.
        /// </summary>
        [Test]
        public void Build_WhenWindowCaptureInEditMode_ReturnsEmpty()
        {
            string warning = ScreenshotPlayModeWindowWarningBuilder.Build(CaptureMode.window, false);

            Assert.That(warning, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// What: a rendering capture during Play Mode does not warn.
        /// </summary>
        [Test]
        public void Build_WhenRenderingCaptureDuringPlayMode_ReturnsEmpty()
        {
            string warning = ScreenshotPlayModeWindowWarningBuilder.Build(CaptureMode.rendering, true);

            Assert.That(warning, Is.EqualTo(string.Empty));
        }
    }
}
