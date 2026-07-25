using NUnit.Framework;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Runtime;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Characterizes synchronous mouse UI cleanup scheduling.
    /// </summary>
    [TestFixture]
    public sealed class MouseUiMainThreadCleanupSchedulerTests
    {
        [TearDown]
        public void TearDown()
        {
            SimulateMouseUiOverlayState.Clear();
        }

        /// <summary>
        /// Verifies cleanup executes immediately when invoked from the Unity main thread.
        /// </summary>
        [Test]
        public void ExecuteCleanupOnMainThread_AfterContextCapture_RunsImmediately()
        {
            MouseUiMainThreadCleanupScheduler scheduler = new();
            scheduler.CaptureMainThreadContext();
            bool cleanupRan = false;

            scheduler.ExecuteCleanupOnMainThread(() => cleanupRan = true);

            Assert.That(cleanupRan, Is.True);
        }

        /// <summary>
        /// Verifies queued overlay cleanup clears active visualization state on the main thread.
        /// </summary>
        [Test]
        public void QueueOverlayClear_WithActiveOverlayState_ClearsState()
        {
            SimulateMouseUiOverlayState.Update(
                MouseAction.Click,
                new Vector2(10f, 20f),
                null,
                new Vector2(100f, 200f));
            MouseUiMainThreadCleanupScheduler scheduler = new();
            scheduler.CaptureMainThreadContext();

            scheduler.QueueOverlayClear();

            Assert.That(SimulateMouseUiOverlayState.IsActive, Is.False);
        }
    }
}
