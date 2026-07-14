using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies window-name fallback from Game to Device Simulator for screenshot capture.
    /// </summary>
    public class ScreenshotWindowNameResolverTests
    {
        [Test]
        public void ShouldFallbackToSimulator_WhenDefaultGameExactMisses_ReturnsTrue()
        {
            // Verifies the default Game exact lookup falls back when no window matched.
            bool shouldFallback = ScreenshotWindowNameResolver.ShouldFallbackToSimulator(
                UnityCliLoopConstants.SCREENSHOT_DEFAULT_WINDOW_NAME,
                WindowMatchMode.exact,
                matchCount: 0);

            Assert.That(shouldFallback, Is.True);
        }

        [Test]
        public void ShouldFallbackToSimulator_WhenGameAlreadyMatched_ReturnsFalse()
        {
            // Verifies fallback is skipped when the Game window was found.
            bool shouldFallback = ScreenshotWindowNameResolver.ShouldFallbackToSimulator(
                UnityCliLoopConstants.SCREENSHOT_DEFAULT_WINDOW_NAME,
                WindowMatchMode.exact,
                matchCount: 1);

            Assert.That(shouldFallback, Is.False);
        }

        [Test]
        public void ShouldFallbackToSimulator_WhenRequestedNameIsNotGame_ReturnsFalse()
        {
            // Verifies explicit non-Game names do not silently switch to Simulator.
            bool shouldFallback = ScreenshotWindowNameResolver.ShouldFallbackToSimulator(
                "Inspector",
                WindowMatchMode.exact,
                matchCount: 0);

            Assert.That(shouldFallback, Is.False);
        }

        [Test]
        public void ShouldFallbackToSimulator_WhenMatchModeIsNotExact_ReturnsFalse()
        {
            // Verifies prefix/contains Game lookups do not trigger Simulator fallback.
            bool shouldFallback = ScreenshotWindowNameResolver.ShouldFallbackToSimulator(
                UnityCliLoopConstants.SCREENSHOT_DEFAULT_WINDOW_NAME,
                WindowMatchMode.contains,
                matchCount: 0);

            Assert.That(shouldFallback, Is.False);
        }

        [Test]
        public void ResolveCaptureWindowName_WhenFallbackApplies_ReturnsSimulator()
        {
            // Verifies the resolved capture title becomes Simulator after a Game miss.
            string resolved = ScreenshotWindowNameResolver.ResolveCaptureWindowName(
                UnityCliLoopConstants.SCREENSHOT_DEFAULT_WINDOW_NAME,
                WindowMatchMode.exact,
                primaryMatchCount: 0);

            Assert.That(resolved, Is.EqualTo(UnityCliLoopConstants.SCREENSHOT_SIMULATOR_WINDOW_NAME));
        }
    }
}
