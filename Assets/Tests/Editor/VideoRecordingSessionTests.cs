using System;
using NUnit.Framework;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies session tick pacing, skip counting, and stop/dispose behavior.
    /// </summary>
    public sealed class VideoRecordingSessionTests
    {
        private VideoRecordingSession _session;
        private FakeVideoFrameEncoder _encoder;
        private FakeGameViewFrameSource _frameSource;
        private double _now;

        [SetUp]
        public void SetUp()
        {
            _now = 0.0;
            _encoder = new FakeVideoFrameEncoder();
            _frameSource = new FakeGameViewFrameSource();
            _session = new VideoRecordingSession(
                _encoder,
                _frameSource,
                () => _now,
                30,
                60.0,
                "/tmp/gameview.mp4");
        }

        [TearDown]
        public void TearDown()
        {
            if (_session == null)
            {
                return;
            }

            _session.Stop("teardown");
            _session = null;
        }

        /// <summary>
        /// What: a 0.5s tick at 30 fps encodes 15 frames when the Game View read succeeds.
        /// </summary>
        [Test]
        public void Tick_WhenHalfSecondElapsed_Encodes15Frames()
        {
            _now = 0.5;

            _session.Tick();

            Assert.That(_encoder.AddFrameCallCount, Is.EqualTo(15));
            Assert.That(_session.Snapshot().EncodedFrameCount, Is.EqualTo(15));
        }

        /// <summary>
        /// What: a failed Game View read skips the due frames without calling AddFrame.
        /// </summary>
        [Test]
        public void Tick_WhenFrameSourceFails_SkipsDueFrames()
        {
            _frameSource.ReadSucceeds = false;
            _now = 0.5;

            _session.Tick();

            VideoRecordingSnapshot snapshot = _session.Snapshot();
            Assert.That(_encoder.AddFrameCallCount, Is.EqualTo(0));
            Assert.That(snapshot.EncodedFrameCount, Is.EqualTo(0));
            Assert.That(snapshot.SkippedFrameCount, Is.EqualTo(15));
        }

        /// <summary>
        /// What: AddFrame false increments skipped frames and leaves encoded count unchanged.
        /// </summary>
        [Test]
        public void Tick_WhenAddFrameFails_CountsSkippedNotEncoded()
        {
            _encoder.AddFrameSucceeds = false;
            _now = 0.5;

            _session.Tick();

            VideoRecordingSnapshot snapshot = _session.Snapshot();
            Assert.That(_encoder.AddFrameCallCount, Is.EqualTo(15));
            Assert.That(snapshot.EncodedFrameCount, Is.EqualTo(0));
            Assert.That(snapshot.SkippedFrameCount, Is.EqualTo(15));
        }

        /// <summary>
        /// What: reaching max duration stops the session and disposes the encoder once.
        /// </summary>
        [Test]
        public void Tick_WhenMaxDurationReached_StopsAndDisposes()
        {
            _session.Stop("teardown");
            FakeVideoFrameEncoder encoder = new FakeVideoFrameEncoder();
            _encoder = encoder;
            _session = new VideoRecordingSession(
                encoder,
                _frameSource,
                () => _now,
                30,
                1.0,
                "/tmp/gameview.mp4");
            _now = 1.0;

            _session.Tick();

            VideoRecordingSnapshot snapshot = _session.Snapshot();
            Assert.That(snapshot.IsRecording, Is.False);
            Assert.That(snapshot.StoppedBy, Is.EqualTo("max-duration"));
            Assert.That(encoder.DisposeCallCount, Is.EqualTo(1));
        }

        /// <summary>
        /// What: a second Stop is a no-op so the encoder is disposed only once.
        /// </summary>
        [Test]
        public void Stop_WhenCalledTwice_DisposesOnce()
        {
            _session.Stop("cli");
            _session.Stop("cli");

            Assert.That(_encoder.DisposeCallCount, Is.EqualTo(1));
        }

        /// <summary>
        /// What: Snapshot elapsed seconds stay frozen after Stop even if the clock advances.
        /// </summary>
        [Test]
        public void Snapshot_AfterStop_KeepsElapsedSecondsFixed()
        {
            _now = 0.5;
            _session.Stop("cli");
            _now = 10.0;

            VideoRecordingSnapshot snapshot = _session.Snapshot();

            Assert.That(snapshot.ElapsedSeconds, Is.EqualTo(0.5).Within(0.0001));
            Assert.That(snapshot.IsRecording, Is.False);
        }

        private sealed class FakeVideoFrameEncoder : IVideoFrameEncoder
        {
            internal int AddFrameCallCount { get; private set; }

            internal int DisposeCallCount { get; private set; }

            internal bool AddFrameSucceeds { get; set; } = true;

            public int Width => 2;

            public int Height => 2;

            public bool AddFrame(Texture2D texture)
            {
                AddFrameCallCount++;
                return AddFrameSucceeds;
            }

            public void Dispose()
            {
                DisposeCallCount++;
            }
        }

        private sealed class FakeGameViewFrameSource : IGameViewFrameSource
        {
            internal bool ReadSucceeds { get; set; } = true;

            public bool TryReadFrame(Texture2D destination)
            {
                return ReadSucceeds;
            }

            public bool TryGetSize(out int width, out int height)
            {
                width = 2;
                height = 2;
                return true;
            }
        }
    }
}
