using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies CaptureMode.auto resolution and wire names for explicit and omitted modes.
    /// </summary>
    public sealed class ScreenshotCaptureModeResolverTests
    {
        /// <summary>
        /// What: omitted auto in Play Mode resolves to rendering.
        /// </summary>
        [Test]
        public void Resolve_WhenAutoAndPlaying_ReturnsRendering()
        {
            CaptureMode resolved = ScreenshotCaptureModeResolver.Resolve(CaptureMode.auto, true);

            Assert.That(resolved, Is.EqualTo(CaptureMode.rendering));
            Assert.That(ScreenshotCaptureModeResolver.ToWireName(resolved), Is.EqualTo("rendering"));
        }

        /// <summary>
        /// What: omitted auto in Edit Mode resolves to window.
        /// </summary>
        [Test]
        public void Resolve_WhenAutoAndNotPlaying_ReturnsWindow()
        {
            CaptureMode resolved = ScreenshotCaptureModeResolver.Resolve(CaptureMode.auto, false);

            Assert.That(resolved, Is.EqualTo(CaptureMode.window));
            Assert.That(ScreenshotCaptureModeResolver.ToWireName(resolved), Is.EqualTo("window"));
        }

        /// <summary>
        /// What: explicit window stays window while Play Mode is running.
        /// </summary>
        [Test]
        public void Resolve_WhenWindowAndPlaying_ReturnsWindow()
        {
            CaptureMode resolved = ScreenshotCaptureModeResolver.Resolve(CaptureMode.window, true);

            Assert.That(resolved, Is.EqualTo(CaptureMode.window));
            Assert.That(ScreenshotCaptureModeResolver.ToWireName(resolved), Is.EqualTo("window"));
        }

        /// <summary>
        /// What: explicit window stays window while Play Mode is stopped.
        /// </summary>
        [Test]
        public void Resolve_WhenWindowAndNotPlaying_ReturnsWindow()
        {
            CaptureMode resolved = ScreenshotCaptureModeResolver.Resolve(CaptureMode.window, false);

            Assert.That(resolved, Is.EqualTo(CaptureMode.window));
            Assert.That(ScreenshotCaptureModeResolver.ToWireName(resolved), Is.EqualTo("window"));
        }

        /// <summary>
        /// What: explicit rendering stays rendering while Play Mode is running.
        /// </summary>
        [Test]
        public void Resolve_WhenRenderingAndPlaying_ReturnsRendering()
        {
            CaptureMode resolved = ScreenshotCaptureModeResolver.Resolve(CaptureMode.rendering, true);

            Assert.That(resolved, Is.EqualTo(CaptureMode.rendering));
            Assert.That(ScreenshotCaptureModeResolver.ToWireName(resolved), Is.EqualTo("rendering"));
        }

        /// <summary>
        /// What: explicit rendering stays rendering while Play Mode is stopped.
        /// </summary>
        [Test]
        public void Resolve_WhenRenderingAndNotPlaying_ReturnsRendering()
        {
            CaptureMode resolved = ScreenshotCaptureModeResolver.Resolve(CaptureMode.rendering, false);

            Assert.That(resolved, Is.EqualTo(CaptureMode.rendering));
            Assert.That(ScreenshotCaptureModeResolver.ToWireName(resolved), Is.EqualTo("rendering"));
        }

        /// <summary>
        /// What: explicit GameView is a rendering request even while Play Mode is running.
        /// </summary>
        [Test]
        public void Resolve_WhenGameViewAndPlaying_ReturnsRendering()
        {
            CaptureMode resolved = ScreenshotCaptureModeResolver.Resolve(CaptureMode.GameView, true);

            Assert.That(resolved, Is.EqualTo(CaptureMode.rendering));
            Assert.That(ScreenshotCaptureModeResolver.ToWireName(resolved), Is.EqualTo("rendering"));
        }

        /// <summary>
        /// What: explicit GameView is a rendering request even while Play Mode is stopped.
        /// </summary>
        [Test]
        public void Resolve_WhenGameViewAndNotPlaying_ReturnsRendering()
        {
            CaptureMode resolved = ScreenshotCaptureModeResolver.Resolve(CaptureMode.GameView, false);

            Assert.That(resolved, Is.EqualTo(CaptureMode.rendering));
            Assert.That(ScreenshotCaptureModeResolver.ToWireName(resolved), Is.EqualTo("rendering"));
        }
    }
}
