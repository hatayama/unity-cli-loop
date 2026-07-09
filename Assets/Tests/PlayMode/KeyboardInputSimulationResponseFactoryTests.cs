#if ULOOP_HAS_INPUT_SYSTEM
#nullable enable
using System;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.PlayMode
{
    /// <summary>
    /// Characterizes wire-visible keyboard input response construction before factory extraction.
    /// </summary>
    [TestFixture]
    public sealed class KeyboardInputSimulationResponseFactoryTests
    {
        private readonly DateTime _nowUtc = new(2026, 7, 9, 0, 0, 0, DateTimeKind.Utc);

        [SetUp]
        public void SetUp()
        {
            UloopPausePointRegistry.ConfigureForTests(new FakePauseController(), () => _nowUtc);
        }

        [TearDown]
        public void TearDown()
        {
            UloopPausePointRegistry.ResetForTests();
        }

        /// <summary>
        /// Verifies interrupted press responses preserve edge observations and project every Pause Point hit.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void InterruptedKeyResult_WithMultiplePausePointHits_MapsPauseEvidenceAndPressEdge(
            bool pressEdgeObserved)
        {
            UloopPausePointRegistry.Enable("first-hit", 30);
            UloopPausePointRegistry.Enable("latest-hit", 30);
            UloopPausePoint.Pause("first-hit");
            UloopPausePoint.Pause("latest-hit");

            SimulateKeyboardResponse response = KeyboardInputSimulationResponseFactory.InterruptedKeyResult(
                UnityCliLoopKeyboardAction.Press,
                "Space",
                pressEdgeObserved);

            Assert.That(response.Success, Is.True);
            Assert.That(
                response.Message,
                Is.EqualTo(
                    "Keyboard input stopped because Unity paused during Pause Point inspection. Key 'Space' was released from Unity CLI Loop bookkeeping."));
            Assert.That(response.Action, Is.EqualTo(UnityCliLoopKeyboardAction.Press.ToString()));
            Assert.That(response.KeyName, Is.EqualTo("Space"));
            Assert.That(response.InterruptedByPausePoint, Is.True);
            Assert.That(response.PressEdgeObserved, Is.EqualTo(pressEdgeObserved));
            Assert.That(response.PausePointId, Is.EqualTo("latest-hit"));
            Assert.That(response.PausePointHitCount, Is.EqualTo(1));
            Assert.That(response.PausePointHits, Has.Count.EqualTo(2));
            Assert.That(response.PausePointHits![0].Id, Is.EqualTo("first-hit"));
            Assert.That(response.PausePointHits[1].Id, Is.EqualTo("latest-hit"));
        }

        /// <summary>
        /// Verifies interrupted key-up responses preserve the absent press edge and project a single Pause Point hit.
        /// </summary>
        [Test]
        public void InterruptedKeyResult_ForKeyUp_MapsNullPressEdgeAndSinglePausePointHit()
        {
            UloopPausePointRegistry.Enable("key-up-hit", 30);
            UloopPausePoint.Pause("key-up-hit");

            SimulateKeyboardResponse response = KeyboardInputSimulationResponseFactory.InterruptedKeyResult(
                UnityCliLoopKeyboardAction.KeyUp,
                "Enter",
                null);

            Assert.That(response.Success, Is.True);
            Assert.That(response.Action, Is.EqualTo(UnityCliLoopKeyboardAction.KeyUp.ToString()));
            Assert.That(response.KeyName, Is.EqualTo("Enter"));
            Assert.That(response.InterruptedByPausePoint, Is.True);
            Assert.That(response.PressEdgeObserved, Is.Null);
            Assert.That(response.PausePointId, Is.EqualTo("key-up-hit"));
            Assert.That(response.PausePointHitCount, Is.EqualTo(1));
            Assert.That(response.PausePointHits, Has.Count.EqualTo(1));
            Assert.That(response.PausePointHits![0].Id, Is.EqualTo("key-up-hit"));
        }

        /// <summary>
        /// Verifies timed-out key responses preserve the action, key name, and timeout failure fields.
        /// </summary>
        [Test]
        public void TimedOutKeyResult_WithActionAndKeyName_MapsFailureResponse()
        {
            SimulateKeyboardResponse response = KeyboardInputSimulationResponseFactory.TimedOutKeyResult(
                UnityCliLoopKeyboardAction.KeyDown,
                "W");

            Assert.That(response.Success, Is.False);
            Assert.That(
                response.Message,
                Is.EqualTo(
                    "Keyboard input timed out while waiting for Unity Editor update. Key 'W' cleanup is queued for the next Editor tick."));
            Assert.That(response.Action, Is.EqualTo(UnityCliLoopKeyboardAction.KeyDown.ToString()));
            Assert.That(response.KeyName, Is.EqualTo("W"));
            Assert.That(response.InterruptedByPausePoint, Is.False);
            Assert.That(response.PressEdgeObserved, Is.Null);
            Assert.That(response.PausePointId, Is.Null);
            Assert.That(response.PausePointHitCount, Is.Null);
            Assert.That(response.PausePointHits, Is.Null);
        }

        /// <summary>
        /// Test double that records Pause Point state without pausing the Unity Editor.
        /// </summary>
        private sealed class FakePauseController : IUloopPausePointPauseController
        {
            public bool IsPlaying => true;
            public bool IsPaused { get; private set; }

            public void Pause()
            {
                IsPaused = true;
            }
        }
    }
}
#endif
