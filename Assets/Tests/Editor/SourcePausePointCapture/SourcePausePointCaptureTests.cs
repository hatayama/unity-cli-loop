using System;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEngine;
using UnityEngine.TestTools;

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

        /// <summary>
        /// Verifies the source capture path records every hit for an armed continuous marker.
        /// </summary>
        [Test]
        public void Capture_WhenContinuousPausePointIsEnabled_RecordsEveryFormattedHit()
        {
            UloopPausePointRegistry.Enable("jump", 30, UloopPausePointCaptureMode.Continuous, 20);

            SourcePausePointCapture.Capture("jump", null, Array.Empty<object>(), new object[] { "speed", 1 });
            SourcePausePointCapture.Capture("jump", null, Array.Empty<object>(), new object[] { "speed", 2 });

            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus("jump");

            Assert.That(snapshot.IsEnabled, Is.True);
            Assert.That(snapshot.HitCount, Is.EqualTo(2));
            Assert.That(snapshot.CapturedVariableHistory, Has.Count.EqualTo(2));
            Assert.That(snapshot.CapturedVariables.Single(variable => variable.Name == "speed").Value, Is.EqualTo("2"));
            Assert.That(_pauseController.PauseCount, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator Capture_WhenCalledOffMainThread_RecordsHitOnNextMainThreadTick()
        {
            // Verifies an off-main-thread Capture call is marshalled to the main thread
            // (must-fix 2): EditorApplication.isPaused and the registry's own bookkeeping are
            // main-thread-only, so the hit must land via MainThreadSwitcher's continuation queue
            // rather than running inline on the calling background thread.
            UloopPausePointRegistry.Enable("jump", 30);
            object[] locals = { "speed", 5 };

            Task.Run(() => SourcePausePointCapture.Capture("jump", null, Array.Empty<object>(), locals));

            float timeoutTime = Time.realtimeSinceStartup + 5f;
            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus("jump");
            while (!snapshot.IsHit && Time.realtimeSinceStartup < timeoutTime)
            {
                yield return null;
                snapshot = UloopPausePointRegistry.GetStatus("jump");
            }

            Assert.That(snapshot.IsHit, Is.True, "hit should be recorded on the main thread within timeout");
            Assert.That(snapshot.CapturedVariables.Select(v => v.Name), Is.EquivalentTo(new[] { "speed" }));
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
