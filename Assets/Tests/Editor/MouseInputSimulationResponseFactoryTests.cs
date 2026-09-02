#if ULOOP_HAS_INPUT_SYSTEM
#nullable enable
using NUnit.Framework;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Pins the pause-point interruption verdicts of simulate-mouse-input button responses.
    /// </summary>
    [TestFixture]
    public sealed class MouseInputSimulationResponseFactoryTests
    {
        /// <summary>
        /// Verifies a press applied before the pause reports a definite delivered verdict and re-check guidance instead of "may have".
        /// </summary>
        [Test]
        public void InterruptedButtonResult_WhenPressWasApplied_ReportsPressDeliveredToGame()
        {
            SimulateMouseInputResponse response = MouseInputSimulationResponseFactory.InterruptedButtonResult(
                UnityCliLoopMouseInputAction.Click,
                "Left",
                new Vector2(10f, 20f),
                pressWasApplied: true);

            Assert.That(response.Success, Is.True);
            Assert.That(response.InterruptedByPausePoint, Is.True);
            Assert.That(response.PressDeliveredToGame, Is.True);
            Assert.That(response.Message, Does.Contain("was delivered to the game before the pause"));
            Assert.That(response.Message, Does.Contain("world state may already have changed"));
            Assert.That(response.Message, Does.Not.Contain("may have registered"));
        }

        /// <summary>
        /// Verifies a press discarded before apply reports a definite not-delivered verdict and says a retry is safe.
        /// </summary>
        [Test]
        public void InterruptedButtonResult_WhenPressWasDiscarded_ReportsPressNotDeliveredToGame()
        {
            SimulateMouseInputResponse response = MouseInputSimulationResponseFactory.InterruptedButtonResult(
                UnityCliLoopMouseInputAction.LongPress,
                "Right",
                new Vector2(1f, 2f),
                pressWasApplied: false);

            Assert.That(response.PressDeliveredToGame, Is.False);
            Assert.That(response.Message, Does.Contain("never observed"));
            Assert.That(response.Message, Does.Contain("safe to retry"));
        }

        /// <summary>
        /// Verifies non-button interruptions leave the press verdict absent because no press edge was involved.
        /// </summary>
        [Test]
        public void InterruptedActionResult_LeavesPressDeliveredToGameNull()
        {
            SimulateMouseInputResponse response = MouseInputSimulationResponseFactory.InterruptedActionResult(
                UnityCliLoopMouseInputAction.MoveDelta);

            Assert.That(response.InterruptedByPausePoint, Is.True);
            Assert.That(response.PressDeliveredToGame, Is.Null);
        }
    }
}
#endif
