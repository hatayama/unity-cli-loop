#if ULOOP_HAS_INPUT_SYSTEM
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests which stack frames are classified as the Input System's monitor-removal path.
    /// </summary>
    public class InputSystemMonitorRemovalAssertionOriginTests
    {
        /// <summary>
        /// Verifies DynamicBitfield.ClearBit, the frame that raises the assert, is recognized.
        /// </summary>
        [Test]
        public void IsMonitorRemovalFrame_WhenDynamicBitfieldClearBit_ReturnsTrue()
        {
            bool result = InputSystemMonitorRemovalAssertionOrigin.IsMonitorRemovalFrame(
                "UnityEngine.InputSystem.DynamicBitfield", "ClearBit");

            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Verifies InputManager.FireStateChangeNotifications, where Input System 1.14 declares it, is recognized.
        /// </summary>
        [Test]
        public void IsMonitorRemovalFrame_WhenInputManagerFireStateChangeNotifications_ReturnsTrue()
        {
            bool result = InputSystemMonitorRemovalAssertionOrigin.IsMonitorRemovalFrame(
                "UnityEngine.InputSystem.InputManager", "FireStateChangeNotifications");

            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Verifies InputManagerStateMonitors.FireStateChangeNotifications, where Input System 1.20 declares it, is recognized.
        /// </summary>
        [Test]
        public void IsMonitorRemovalFrame_WhenInputManagerStateMonitorsFireStateChangeNotifications_ReturnsTrue()
        {
            bool result = InputSystemMonitorRemovalAssertionOrigin.IsMonitorRemovalFrame(
                "UnityEngine.InputSystem.InputManagerStateMonitors", "FireStateChangeNotifications");

            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Verifies a matching type alone is not enough when the method is not FireStateChangeNotifications.
        /// </summary>
        [Test]
        public void IsMonitorRemovalFrame_WhenInputManagerStateMonitorsOtherMethod_ReturnsFalse()
        {
            bool result = InputSystemMonitorRemovalAssertionOrigin.IsMonitorRemovalFrame(
                "UnityEngine.InputSystem.InputManagerStateMonitors", "RemoveAt");

            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Verifies a matching method name alone is not enough when a user type declares it.
        /// </summary>
        [Test]
        public void IsMonitorRemovalFrame_WhenUserTypeClearBit_ReturnsFalse()
        {
            bool result = InputSystemMonitorRemovalAssertionOrigin.IsMonitorRemovalFrame(
                "Game.PlayerInput", "ClearBit");

            Assert.That(result, Is.False);
        }
    }
}
#endif
