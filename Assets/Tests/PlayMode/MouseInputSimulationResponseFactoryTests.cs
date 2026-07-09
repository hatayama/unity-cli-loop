#if ULOOP_HAS_INPUT_SYSTEM
#nullable enable
using System;
using NUnit.Framework;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.PlayMode
{
    /// <summary>
    /// Characterizes wire-visible mouse input response construction before factory extraction.
    /// </summary>
    [TestFixture]
    public sealed class MouseInputSimulationResponseFactoryTests
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
        /// Verifies interrupted button responses project every Pause Point hit and preserve button coordinates.
        /// </summary>
        [Test]
        public void InterruptedButtonResult_WithMultiplePausePointHits_MapsPauseEvidenceAndButtonPosition()
        {
            UloopPausePointRegistry.Enable("first-hit", 30);
            UloopPausePointRegistry.Enable("latest-hit", 30);
            UloopPausePoint.Pause("first-hit");
            UloopPausePoint.Pause("latest-hit");
            Vector2 inputPosition = new(10.25f, 20.75f);

            SimulateMouseInputResponse response = MouseInputSimulationResponseFactory.InterruptedButtonResult(
                UnityCliLoopMouseInputAction.Click,
                "Left",
                inputPosition);

            Assert.That(response.Success, Is.True);
            Assert.That(
                response.Message,
                Is.EqualTo(
                    "Mouse input stopped because Unity paused during Pause Point inspection. Unity CLI Loop released its held input bookkeeping."));
            Assert.That(response.Action, Is.EqualTo(UnityCliLoopMouseInputAction.Click.ToString()));
            Assert.That(response.Button, Is.EqualTo("Left"));
            Assert.That(response.PositionX, Is.EqualTo(inputPosition.x));
            Assert.That(response.PositionY, Is.EqualTo(inputPosition.y));
            Assert.That(response.InterruptedByPausePoint, Is.True);
            Assert.That(response.PausePointId, Is.EqualTo("latest-hit"));
            Assert.That(response.PausePointHitCount, Is.EqualTo(1));
            Assert.That(response.PausePointHits, Has.Count.EqualTo(2));
            Assert.That(response.PausePointHits![0].Id, Is.EqualTo("first-hit"));
            Assert.That(response.PausePointHits[1].Id, Is.EqualTo("latest-hit"));
        }

        /// <summary>
        /// Verifies timed-out button responses preserve the action, button, coordinates, and timeout message.
        /// </summary>
        [Test]
        public void TimedOutButtonResult_WithButtonAndPosition_MapsFailureResponse()
        {
            Vector2 inputPosition = new(30.5f, 40.25f);

            SimulateMouseInputResponse response = MouseInputSimulationResponseFactory.TimedOutButtonResult(
                UnityCliLoopMouseInputAction.LongPress,
                "Right",
                inputPosition);

            Assert.That(response.Success, Is.False);
            Assert.That(
                response.Message,
                Is.EqualTo(
                    "Mouse input timed out while waiting for Unity Editor update. Cleanup is queued for the next Editor tick."));
            Assert.That(response.Action, Is.EqualTo(UnityCliLoopMouseInputAction.LongPress.ToString()));
            Assert.That(response.Button, Is.EqualTo("Right"));
            Assert.That(response.PositionX, Is.EqualTo(inputPosition.x));
            Assert.That(response.PositionY, Is.EqualTo(inputPosition.y));
            Assert.That(response.InterruptedByPausePoint, Is.False);
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
