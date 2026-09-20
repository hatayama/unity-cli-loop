using System;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Runtime;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies that method entries recorded after a marker's capture window closed are not
    /// counted, so an expired marker does not claim the armed method ran while it was armed.
    /// </summary>
    [TestFixture]
    public sealed class PausePointMethodEntryAfterExpiryTests
    {
        private DateTime _nowUtc;
        private FakePauseController _pauseController;

        [SetUp]
        public void SetUp()
        {
            _nowUtc = new DateTime(2026, 6, 3, 0, 0, 0, DateTimeKind.Utc);
            _pauseController = new FakePauseController();
            UloopPausePointRegistry.ConfigureForTests(_pauseController, () => _nowUtc);
        }

        [TearDown]
        public void TearDown()
        {
            UloopPausePointRegistry.ResetForTests();
        }

        /// <summary>
        /// Verifies a method entry recorded after the capture window closed is not counted, so the
        /// expiry message does not claim the armed method ran and never reached the armed line.
        /// </summary>
        [Test]
        public void RecordMethodEntry_AfterCaptureWindowClosed_IsNotCounted()
        {
            UloopPausePointRegistry.SetMethodEntryInstrumented("jump");
            UloopPausePointRegistry.Enable("jump", 30);
            _nowUtc = _nowUtc.AddSeconds(137);

            UloopPausePointRegistry.RecordMethodEntry("jump");

            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus("jump");
            Assert.That(snapshot.MethodEntryCount, Is.EqualTo(0));
            Assert.That(snapshot.Message, Does.Not.Contain("branch not taken"));
        }

        /// <summary>
        /// Verifies the expiry message and the recommended next action agree when every method
        /// entry happened after the window closed: the marker reports no entry and still
        /// recommends a longer timeout.
        /// </summary>
        [Test]
        public void GetStatus_WhenOnlyEntriesAfterExpiryExist_MessageAndNextActionAgree()
        {
            UloopPausePointRegistry.SetMethodEntryInstrumented("jump");
            UloopPausePointRegistry.Enable("jump", 30);
            _nowUtc = _nowUtc.AddSeconds(137);
            UloopPausePointRegistry.RecordMethodEntry("jump");

            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus("jump");

            Assert.That(
                snapshot.Message,
                Is.EqualTo("Pause point expired before it was hit. The armed method was never invoked."));
            Assert.That(
                snapshot.RecommendedNextAction,
                Is.EqualTo("Re-enable the marker with a longer --timeout-seconds and trigger the code path again; clearing the expired marker first is not required."));
        }

        /// <summary>
        /// Verifies a method entry recorded inside the capture window is still counted and still
        /// produces the branch-not-taken diagnostic after the marker expires.
        /// </summary>
        [Test]
        public void RecordMethodEntry_WithinCaptureWindow_StillReportsBranchNotTaken()
        {
            UloopPausePointRegistry.SetMethodEntryInstrumented("jump");
            UloopPausePointRegistry.Enable("jump", 30);
            _nowUtc = _nowUtc.AddSeconds(10);
            UloopPausePointRegistry.RecordMethodEntry("jump");
            _nowUtc = _nowUtc.AddSeconds(30);

            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus("jump");

            Assert.That(snapshot.MethodEntryCount, Is.EqualTo(1));
            Assert.That(
                snapshot.Message,
                Is.EqualTo("Pause point expired before it was hit. The armed method ran 1 time(s) but the armed line was never reached (branch not taken)."));
        }

        /// <summary>
        /// Verifies the pause-window freeze still applies to method entries: while another
        /// marker's hit holds the Editor paused, no countdown runs, so an entry recorded past the
        /// nominal deadline is counted.
        /// </summary>
        [Test]
        public void RecordMethodEntry_WhileAnotherMarkerHitFreezesCountdown_IsCounted()
        {
            UloopPausePointRegistry.SetMethodEntryInstrumented("jump");
            UloopPausePointRegistry.Enable("jump", 30);
            UloopPausePointRegistry.Enable("inspect", 30);
            UloopPausePoint.Pause("inspect");
            _nowUtc = _nowUtc.AddSeconds(137);

            UloopPausePointRegistry.RecordMethodEntry("jump");

            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus("jump");
            Assert.That(snapshot.Status, Is.EqualTo(UloopPausePointStatus.Enabled));
            Assert.That(snapshot.MethodEntryCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Test double that records pause requests without mutating Unity Editor state.
        /// </summary>
        private sealed class FakePauseController : IUloopPausePointPauseController
        {
            public bool IsPlaying => true;
            public bool IsPaused { get; private set; }

            public void Pause()
            {
                IsPaused = true;
            }

            public void Resume()
            {
                IsPaused = false;
            }
        }
    }
}
