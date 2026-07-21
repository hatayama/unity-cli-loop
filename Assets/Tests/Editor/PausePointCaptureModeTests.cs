using System;
using System.Linq;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Runtime;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies pause point capture modes and bounded hit history behavior.
    /// </summary>
    [TestFixture]
    public sealed class PausePointCaptureModeTests
    {
        private DateTime _nowUtc;
        private FakePausePointPauseController _pauseController;

        [SetUp]
        public void SetUp()
        {
            _nowUtc = new DateTime(2026, 6, 3, 0, 0, 0, DateTimeKind.Utc);
            _pauseController = new FakePausePointPauseController();
            UloopPausePointRegistry.ConfigureForTests(_pauseController, () => _nowUtc);
        }

        [TearDown]
        public void TearDown()
        {
            UloopPausePointRegistry.ResetForTests();
        }

        /// <summary>
        /// Verifies continuous capture keeps the marker armed while exposing ordered history and the latest variables.
        /// </summary>
        [Test]
        public void Continuous_WhenHitMultipleTimes_PreservesHistoryAndLatestVariables()
        {
            UloopCapturedVariable[] firstVariables = { CreateVariable("speed", "1") };
            UloopCapturedVariable[] secondVariables = { CreateVariable("speed", "2") };
            UloopPausePointRegistry.Enable("jump", 30, UloopPausePointCaptureMode.Continuous, 20);

            UloopPausePointRegistry.HitWithCapturedVariables("jump", firstVariables, false);
            UloopPausePointRegistry.HitWithCapturedVariables("jump", secondVariables, false);

            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus("jump");

            Assert.That(snapshot.IsEnabled, Is.True);
            Assert.That(snapshot.HitCount, Is.EqualTo(2));
            Assert.That(snapshot.CapturedVariables.Single().Value, Is.EqualTo("2"));
            Assert.That(snapshot.CapturedVariableHistory.Select(frame => frame.HitSequence), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(snapshot.CapturedVariableHistory.Last().CapturedVariables.Single().Value, Is.EqualTo("2"));
            Assert.That(_pauseController.PauseCount, Is.EqualTo(2));
        }

        /// <summary>
        /// Verifies trace capture records a hit without requesting an Editor pause.
        /// </summary>
        [Test]
        public void Trace_WhenHit_DoesNotPauseAndKeepsMarkerArmed()
        {
            UloopPausePointRegistry.Enable("trace", 30, UloopPausePointCaptureMode.Trace, 20);

            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.Hit("trace");

            Assert.That(snapshot.IsEnabled, Is.True);
            Assert.That(snapshot.HitCount, Is.EqualTo(1));
            Assert.That(snapshot.Mode, Is.EqualTo(UloopPausePointCaptureMode.Trace));
            Assert.That(snapshot.CapturedVariableHistory, Has.Count.EqualTo(1));
            Assert.That(_pauseController.PauseCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies the bounded history drops the oldest frame and reports the dropped count.
        /// </summary>
        [Test]
        public void History_WhenMaxHistoryIsExceeded_DropsOldestFrames()
        {
            UloopPausePointRegistry.Enable("jump", 30, UloopPausePointCaptureMode.Trace, 2);

            UloopPausePointRegistry.Hit("jump");
            UloopPausePointRegistry.Hit("jump");
            UloopPausePointRegistry.Hit("jump");

            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus("jump");

            Assert.That(snapshot.CapturedVariableHistory.Select(frame => frame.HitSequence), Is.EqualTo(new[] { 2, 3 }));
            Assert.That(snapshot.HistoryDroppedCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies the default single-shot mode retains current disarm behavior and records its hit.
        /// </summary>
        [Test]
        public void SingleShot_WhenHit_DisarmsAndKeepsOneHistoryFrame()
        {
            UloopPausePointRegistry.Enable("jump", 30);

            UloopPausePointRegistry.Hit("jump");
            UloopPausePointRegistry.Hit("jump");

            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus("jump");

            Assert.That(snapshot.IsEnabled, Is.False);
            Assert.That(snapshot.HitCount, Is.EqualTo(1));
            Assert.That(snapshot.CapturedVariableHistory, Has.Count.EqualTo(1));
            Assert.That(_pauseController.PauseCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies clearing an armed continuous marker disarms it without erasing captured history.
        /// </summary>
        [Test]
        public void Clear_WhenContinuousMarkerHasHits_DisarmsAndPreservesHistory()
        {
            UloopPausePointRegistry.Enable("jump", 30, UloopPausePointCaptureMode.Continuous, 20);
            UloopPausePointRegistry.Hit("jump");

            (UloopPausePointSnapshot snapshot, _) = UloopPausePointRegistry.Clear("jump");

            Assert.That(snapshot.IsEnabled, Is.False);
            Assert.That(snapshot.CapturedVariableHistory, Has.Count.EqualTo(1));
            Assert.That(snapshot.ClearedReason, Is.EqualTo(UloopPausePointClearedReason.ExplicitClear));
            Assert.That(snapshot.Message, Is.EqualTo("Pause point cleared after 1 hit(s); capture history is preserved."));
        }

        /// <summary>
        /// Verifies expiry after a continuous hit's pause is resumed reports a capture-window
        /// message and retains history. The hit itself freezes the capture window while the
        /// Editor stays paused for inspection, so time elapsing before resume must not count.
        /// </summary>
        [Test]
        public void Expire_WhenContinuousMarkerHasHitsAndIsResumed_DisarmsAndPreservesHistory()
        {
            UloopPausePointRegistry.Enable("jump", 1, UloopPausePointCaptureMode.Continuous, 20);
            UloopPausePointRegistry.Hit("jump");
            _nowUtc = _nowUtc.AddSeconds(5);
            UloopPausePointRegistry.ResumeEditorPauseForClientDisconnect();
            UloopPausePointRegistry.ApplyPendingClientDisconnectResume();
            _nowUtc = _nowUtc.AddSeconds(2);

            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus("jump");

            Assert.That(snapshot.Status, Is.EqualTo(UloopPausePointStatus.Expired));
            Assert.That(snapshot.CapturedVariableHistory, Has.Count.EqualTo(1));
            Assert.That(snapshot.Message, Is.EqualTo("Pause point capture window expired after 1 hit(s); capture history is preserved."));
        }

        /// <summary>
        /// Verifies re-enabling a marker starts a fresh generation with empty history.
        /// </summary>
        [Test]
        public void Enable_WhenMarkerIsReenabled_ResetsHistory()
        {
            UloopPausePointRegistry.Enable("jump", 30, UloopPausePointCaptureMode.Trace, 2);
            UloopPausePointRegistry.Hit("jump");

            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.Enable(
                "jump", 30, UloopPausePointCaptureMode.Trace, 2);

            Assert.That(snapshot.HitCount, Is.EqualTo(0));
            Assert.That(snapshot.CapturedVariableHistory, Is.Empty);
            Assert.That(snapshot.HistoryDroppedCount, Is.EqualTo(0));
        }

        private static UloopCapturedVariable CreateVariable(string name, string value)
        {
            return new UloopCapturedVariable(
                name,
                UloopCapturedVariableScope.Local,
                "System.String",
                value,
                string.Empty,
                string.Empty,
                0);
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

            public void Resume()
            {
                // Why zero: Unity's isPaused is a bool; Option B Resume must fully clear pause.
                PauseCount = 0;
            }
        }
    }
}
