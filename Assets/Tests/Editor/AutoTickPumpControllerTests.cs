using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    public sealed class AutoTickPumpControllerTests
    {
        private const double TrailingWindowSeconds = 10.0;

        /// <summary>
        /// Verifies that a freshly constructed controller does not request pumping.
        /// </summary>
        [Test]
        public void ShouldPump_WhenInitial_ReturnsFalse()
        {
            AutoTickPumpController controller = new AutoTickPumpController(TrailingWindowSeconds);

            Assert.That(controller.ShouldPump(0.0), Is.False);
            Assert.That(controller.ShouldPump(100.0), Is.False);
        }

        /// <summary>
        /// Verifies that an open scope keeps ShouldPump true regardless of elapsed time.
        /// </summary>
        [Test]
        public void ShouldPump_AfterScopeStarted_ReturnsTrue()
        {
            AutoTickPumpController controller = new AutoTickPumpController(TrailingWindowSeconds);

            controller.NotifyScopeStarted();

            Assert.That(controller.ShouldPump(0.0), Is.True);
            Assert.That(controller.ShouldPump(1000.0), Is.True);
        }

        /// <summary>
        /// Verifies that nested scopes use reference counting and stay active until the last ends.
        /// </summary>
        [Test]
        public void ShouldPump_WhenNestedScopesPartiallyEnded_ReturnsTrue()
        {
            AutoTickPumpController controller = new AutoTickPumpController(TrailingWindowSeconds);

            controller.NotifyScopeStarted();
            controller.NotifyScopeStarted();
            controller.NotifyScopeEnded(1.0);

            Assert.That(controller.ShouldPump(1.0), Is.True);
            Assert.That(controller.ShouldPump(100.0), Is.True);
        }

        /// <summary>
        /// Verifies that the trailing window keeps pumping shortly after the last scope ends.
        /// </summary>
        [Test]
        public void ShouldPump_WithinTrailingWindowAfterLastScopeEnded_ReturnsTrue()
        {
            AutoTickPumpController controller = new AutoTickPumpController(TrailingWindowSeconds);

            controller.NotifyScopeStarted();
            controller.NotifyScopeEnded(5.0);

            Assert.That(controller.ShouldPump(5.0 + 9.9), Is.True);
        }

        /// <summary>
        /// Verifies that the trailing window is exclusive at the boundary (elapsed == window => false).
        /// </summary>
        [Test]
        public void ShouldPump_AtTrailingWindowBoundaryAfterLastScopeEnded_ReturnsFalse()
        {
            AutoTickPumpController controller = new AutoTickPumpController(TrailingWindowSeconds);

            controller.NotifyScopeStarted();
            controller.NotifyScopeEnded(5.0);

            Assert.That(controller.ShouldPump(5.0 + TrailingWindowSeconds), Is.False);
        }

        /// <summary>
        /// Verifies that startup completion opens a trailing window that later expires.
        /// </summary>
        [Test]
        public void ShouldPump_AfterStartupCompleted_FollowsTrailingWindow()
        {
            AutoTickPumpController controller = new AutoTickPumpController(TrailingWindowSeconds);

            controller.NotifyStartupCompleted(2.0);

            Assert.That(controller.ShouldPump(2.0 + 9.9), Is.True);
            Assert.That(controller.ShouldPump(2.0 + TrailingWindowSeconds), Is.False);
        }
    }
}
