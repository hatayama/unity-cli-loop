using System;
using System.Linq;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Runtime;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the Harmony-injected landing point: the armed fast-path no-op and the
    /// formatted-variables handoff into the registry.
    /// </summary>
    [TestFixture]
    public sealed class SourcePausePointCaptureTests
    {
        private FakePausePointPauseController _pauseController;

        [SetUp]
        public void SetUp()
        {
            _pauseController = new FakePausePointPauseController();
            UloopPausePointRegistry.ConfigureForTests(_pauseController, () => DateTime.UtcNow);
        }

        [TearDown]
        public void TearDown()
        {
            UloopPausePointRegistry.ResetForTests();
        }

        [Test]
        public void Capture_WhenPausePointIsEnabled_RecordsFormattedVariablesInSnapshot()
        {
            // Verifies an armed marker's hit threads formatted locals/parameters into the snapshot.
            UloopPausePointRegistry.Enable("jump", 30);
            object[] parameters = { "damage", 3 };
            object[] locals = { "speed", 5 };

            SourcePausePointCapture.Capture("jump", null, parameters, locals);

            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus("jump");
            Assert.That(snapshot.IsHit, Is.True);
            Assert.That(snapshot.CapturedVariables.Select(v => v.Name), Is.EquivalentTo(new[] { "speed", "damage" }));
            Assert.That(snapshot.CapturedVariablesTruncated, Is.False);
            Assert.That(_pauseController.PauseCount, Is.EqualTo(1));
        }

        [Test]
        public void Capture_WhenPausePointIsNotArmed_DoesNotPauseOrRecordAHit()
        {
            // Verifies the IsArmed fast path no-ops when the marker was never enabled.
            SourcePausePointCapture.Capture("never-enabled", null, Array.Empty<object>(), Array.Empty<object>());

            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus("never-enabled");
            Assert.That(snapshot.IsHit, Is.False);
            Assert.That(_pauseController.PauseCount, Is.EqualTo(0));
        }

        [Test]
        public void Capture_WhenPausePointWasAlreadyHit_IgnoresSecondCall()
        {
            // Verifies a one-shot marker disarms itself so a second pass through the same line no-ops.
            UloopPausePointRegistry.Enable("jump", 30);
            SourcePausePointCapture.Capture("jump", null, Array.Empty<object>(), Array.Empty<object>());

            SourcePausePointCapture.Capture("jump", null, Array.Empty<object>(), Array.Empty<object>());

            Assert.That(_pauseController.PauseCount, Is.EqualTo(1));
        }

        private sealed class FakePausePointPauseController : IUloopPausePointPauseController
        {
            public int PauseCount { get; private set; }
            public bool IsPlaying => true;
            public bool IsPaused => PauseCount > 0;

            public void Pause()
            {
                PauseCount++;
            }
        }
    }
}
