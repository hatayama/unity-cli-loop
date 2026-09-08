using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the first-update-tick wiring that runs the domain-reload re-arm, which no EditMode
    /// test can exercise through a real reload.
    /// </summary>
    [TestFixture]
    public sealed class PausePointRearmSchedulerTests
    {
        /// <summary>
        /// What: Schedule registers an update handler and does not re-arm before it fires.
        /// </summary>
        [Test]
        public void Schedule_RegistersAnUpdateHandlerWithoutRearmingYet()
        {
            List<EditorApplication.CallbackFunction> subscribed = new();
            int rearmCount = 0;

            new PausePointRearmScheduler(() => rearmCount++, subscribed.Add, _ => { }).Schedule();

            Assert.That(subscribed.Count, Is.EqualTo(1), "Schedule must subscribe exactly one update handler.");
            Assert.That(rearmCount, Is.Zero, "The re-arm must wait for the first update tick.");
        }

        /// <summary>
        /// What: the first tick runs the re-arm once and unsubscribes the handler, and a second
        /// tick that still reaches the handler does not re-arm again.
        /// </summary>
        [Test]
        public void TheFirstUpdateTick_RearmsOnceAndUnsubscribesTheHandler()
        {
            EditorApplication.CallbackFunction subscribed = null;
            List<EditorApplication.CallbackFunction> unsubscribed = new();
            int rearmCount = 0;

            new PausePointRearmScheduler(
                () => rearmCount++,
                handler => subscribed = handler,
                unsubscribed.Add).Schedule();
            subscribed();
            subscribed();

            Assert.That(rearmCount, Is.EqualTo(1), "The re-arm consumes the store and must run only once.");
            Assert.That(unsubscribed, Does.Contain(subscribed), "The handler must remove itself on the first tick.");
        }
    }
}
