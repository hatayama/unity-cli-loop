using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the recording session host starts, ticks, stops, and reacts to Play Mode, assembly reload,
    /// and Editor quit without touching the Editor-resident recording or real Editor callbacks.
    /// </summary>
    public sealed class RecordVideoSessionHostTests
    {
        private LastCompletedRecording _previousRecording;
        private string _outputDirectory;
        private double _now;
        private bool _clockThrows;
        private List<EncoderRequest> _encoderRequests;
        private List<FakeVideoFrameEncoder> _encoders;
        private List<EditorApplication.CallbackFunction> _updateCallbacks;
        private int _unsubscribeCount;
        private int _retentionCount;
        private RecordVideoSessionHost _host;

        [SetUp]
        public void SetUp()
        {
            // The last-recording store is shared with the live recording, so the tests do not run while one is
            // in progress. The test framework still runs TearDown when SetUp fails an assumption, and the fixture
            // instance is reused across tests, so clear the previous test's host first; TearDown skips cleanup
            // when no host was created.
            _host = null;
            _previousRecording = default;
            Assume.That(RecordVideoService.IsRecording, Is.False);
            _previousRecording = LastCompletedRecordingTestState.TakeAndClear();
            _outputDirectory = Path.Combine(Path.GetTempPath(), "uloop-record-video-host-" + Guid.NewGuid().ToString("N"));
            _now = 0.0;
            _clockThrows = false;
            _encoderRequests = new List<EncoderRequest>();
            _encoders = new List<FakeVideoFrameEncoder>();
            _updateCallbacks = new List<EditorApplication.CallbackFunction>();
            _unsubscribeCount = 0;
            _retentionCount = 0;
            _host = new RecordVideoSessionHost(
                CreateEncoder,
                ReadClock,
                callback => _updateCallbacks.Add(callback),
                callback =>
                {
                    _unsubscribeCount++;
                    _updateCallbacks.Remove(callback);
                },
                () => _retentionCount++);
        }

        [TearDown]
        public void TearDown()
        {
            if (_host == null)
            {
                return;
            }

            // Why: the session owns a HideAndDontSave Texture2D that only Stop destroys.
            _clockThrows = false;
            _host.Stop("teardown");
            LastCompletedRecordingTestState.Restore(_previousRecording);
            if (Directory.Exists(_outputDirectory))
            {
                Directory.Delete(_outputDirectory, true);
            }
        }

        /// <summary>
        /// What: Start creates the output directory and an MP4 encoder with the requested settings, clears the
        /// last completed recording, and subscribes exactly one update callback.
        /// </summary>
        [Test]
        public void Start_WithMp4Path_CreatesEncoderAndSubscribesUpdate()
        {
            LastCompletedRecordingStore.Save(CreateStoppedSnapshot());
            string outputPath = Path.Combine(_outputDirectory, "clip.mp4");

            VideoRecordingSnapshot snapshot = StartRecording(outputPath, usedDefaultOutputPath: false, stopOnPlayModeExit: false);

            Assert.That(snapshot.IsRecording, Is.True);
            Assert.That(snapshot.OutputPath, Is.EqualTo(outputPath));
            Assert.That(_host.IsRecording, Is.True);
            Assert.That(Directory.Exists(_outputDirectory), Is.True);
            Assert.That(_encoderRequests.Count, Is.EqualTo(1));
            Assert.That(_encoderRequests[0].OutputPath, Is.EqualTo(outputPath));
            Assert.That(_encoderRequests[0].Width, Is.EqualTo(4));
            Assert.That(_encoderRequests[0].Height, Is.EqualTo(2));
            Assert.That(_encoderRequests[0].FrameRate, Is.EqualTo(30));
            Assert.That(_encoderRequests[0].UseVp8, Is.False);
            Assert.That(_encoderRequests[0].Quality, Is.EqualTo(RecordVideoQuality.high));
            Assert.That(_updateCallbacks.Count, Is.EqualTo(1));
            Assert.That(LastCompletedRecordingStore.TryRead().HasValue, Is.False);
        }

        /// <summary>
        /// What: a .webm output path selects the VP8 encoder regardless of extension casing.
        /// </summary>
        [Test]
        public void Start_WithWebmPath_RequestsVp8Encoder()
        {
            StartRecording(Path.Combine(_outputDirectory, "clip.WEBM"), usedDefaultOutputPath: false, stopOnPlayModeExit: false);

            Assert.That(_encoderRequests[0].UseVp8, Is.True);
        }

        /// <summary>
        /// What: when the session cannot be created, Start disposes the encoder it created and stays idle.
        /// </summary>
        [Test]
        public void Start_WhenSessionCreationThrows_DisposesEncoderAndStaysIdle()
        {
            _clockThrows = true;

            try
            {
                StartRecording(Path.Combine(_outputDirectory, "clip.mp4"), usedDefaultOutputPath: false, stopOnPlayModeExit: false);
                Assert.Fail("Start should rethrow the session creation failure.");
            }
            catch (InvalidOperationException e)
            {
                Assert.That(e.Message, Is.EqualTo("clock failed"));
            }

            Assert.That(_encoders[0].DisposeCallCount, Is.EqualTo(1));
            Assert.That(_host.IsRecording, Is.False);
            Assert.That(_updateCallbacks, Is.Empty);
        }

        /// <summary>
        /// What: an update tick encodes due frames while the session keeps recording.
        /// </summary>
        [Test]
        public void EditorUpdate_WhileRecording_EncodesDueFrames()
        {
            StartRecording(Path.Combine(_outputDirectory, "clip.mp4"), usedDefaultOutputPath: false, stopOnPlayModeExit: false);
            _now = 0.5;

            _updateCallbacks[0]();

            Assert.That(_host.GetSnapshot().EncodedFrameCount, Is.EqualTo(15));
            Assert.That(_host.IsRecording, Is.True);
            Assert.That(_updateCallbacks.Count, Is.EqualTo(1));
        }

        /// <summary>
        /// What: a tick past the maximum duration finishes the session, unsubscribes the update callback,
        /// saves the last completed recording, and applies retention for the default output folder.
        /// </summary>
        [Test]
        public void EditorUpdate_WhenMaxDurationReached_FinishesSessionAndAppliesRetention()
        {
            StartRecording(Path.Combine(_outputDirectory, "clip.mp4"), usedDefaultOutputPath: true, stopOnPlayModeExit: false);
            _now = 11.0;

            _updateCallbacks[0]();

            LastCompletedRecording lastRecording = LastCompletedRecordingStore.TryRead();
            Assert.That(_host.IsRecording, Is.False);
            Assert.That(_updateCallbacks, Is.Empty);
            Assert.That(_encoders[0].DisposeCallCount, Is.EqualTo(1));
            Assert.That(lastRecording.HasValue, Is.True);
            Assert.That(lastRecording.Snapshot.StoppedBy, Is.EqualTo(RecordVideoConstants.StoppedByMaxDuration));
            Assert.That(_retentionCount, Is.EqualTo(1));
            Assert.That(_host.GetSnapshot().OutputPath, Is.Null);
        }

        /// <summary>
        /// What: an update tick without a session does nothing.
        /// </summary>
        [Test]
        public void OnEditorUpdate_WithoutSession_DoesNothing()
        {
            _host.OnEditorUpdate();

            Assert.That(_host.IsRecording, Is.False);
            Assert.That(_unsubscribeCount, Is.EqualTo(0));
        }

        /// <summary>
        /// What: a CLI stop returns the stopped snapshot, does not save it as the last completed recording,
        /// and skips retention for a caller-chosen output path.
        /// </summary>
        [Test]
        public void Stop_ByCli_ReturnsStoppedSnapshotWithoutSavingOrRetention()
        {
            StartRecording(Path.Combine(_outputDirectory, "clip.mp4"), usedDefaultOutputPath: false, stopOnPlayModeExit: false);

            VideoRecordingSnapshot snapshot = _host.Stop(RecordVideoConstants.StoppedByCli);

            Assert.That(snapshot.IsRecording, Is.False);
            Assert.That(snapshot.StoppedBy, Is.EqualTo(RecordVideoConstants.StoppedByCli));
            Assert.That(_host.IsRecording, Is.False);
            Assert.That(_updateCallbacks, Is.Empty);
            Assert.That(LastCompletedRecordingStore.TryRead().HasValue, Is.False);
            Assert.That(_retentionCount, Is.EqualTo(0));
            Assert.That(_host.GetSnapshot().OutputPath, Is.Null);

            // A stopped session must not linger: a later assembly reload would otherwise save it as a new recording.
            _host.OnBeforeAssemblyReload();

            Assert.That(LastCompletedRecordingStore.TryRead().HasValue, Is.False);
        }

        /// <summary>
        /// What: Stop and GetSnapshot return the default snapshot when nothing is recording.
        /// </summary>
        [Test]
        public void StopAndGetSnapshot_WithoutSession_ReturnDefault()
        {
            Assert.That(_host.Stop(RecordVideoConstants.StoppedByCli).OutputPath, Is.Null);
            Assert.That(_host.GetSnapshot().OutputPath, Is.Null);
        }

        /// <summary>
        /// What: leaving Play Mode stops a Game View recording and saves it as the last completed recording.
        /// </summary>
        [Test]
        public void OnPlayModeStateChanged_ExitingPlayModeWithGameViewRecording_StopsAndSaves()
        {
            StartRecording(Path.Combine(_outputDirectory, "clip.mp4"), usedDefaultOutputPath: false, stopOnPlayModeExit: true);

            _host.OnPlayModeStateChanged(PlayModeStateChange.ExitingPlayMode);

            Assert.That(_host.IsRecording, Is.False);
            Assert.That(LastCompletedRecordingStore.TryRead().Snapshot.StoppedBy, Is.EqualTo(RecordVideoConstants.StoppedByPlayModeExit));
        }

        /// <summary>
        /// What: a window recording keeps running when Play Mode ends, and other Play Mode transitions are ignored.
        /// </summary>
        [Test]
        public void OnPlayModeStateChanged_WindowRecordingOrOtherTransition_KeepsRecording()
        {
            StartRecording(Path.Combine(_outputDirectory, "clip.mp4"), usedDefaultOutputPath: false, stopOnPlayModeExit: false);

            _host.OnPlayModeStateChanged(PlayModeStateChange.ExitingPlayMode);
            _host.OnPlayModeStateChanged(PlayModeStateChange.EnteredEditMode);

            Assert.That(_host.IsRecording, Is.True);
        }

        /// <summary>
        /// What: a Game View recording ignores Play Mode transitions other than exiting Play Mode.
        /// </summary>
        [Test]
        public void OnPlayModeStateChanged_GameViewRecordingEnteringPlayMode_KeepsRecording()
        {
            StartRecording(Path.Combine(_outputDirectory, "clip.mp4"), usedDefaultOutputPath: false, stopOnPlayModeExit: true);

            _host.OnPlayModeStateChanged(PlayModeStateChange.EnteredPlayMode);

            Assert.That(_host.IsRecording, Is.True);
        }

        /// <summary>
        /// What: the lifecycle callbacks do nothing when no recording is active.
        /// </summary>
        [Test]
        public void LifecycleCallbacks_WithoutSession_DoNothing()
        {
            _host.OnPlayModeStateChanged(PlayModeStateChange.ExitingPlayMode);
            _host.OnBeforeAssemblyReload();
            _host.OnEditorQuitting();

            Assert.That(_unsubscribeCount, Is.EqualTo(0));
            Assert.That(LastCompletedRecordingStore.TryRead().HasValue, Is.False);
        }

        /// <summary>
        /// What: an assembly reload stops the recording and saves it as the last completed recording.
        /// </summary>
        [Test]
        public void OnBeforeAssemblyReload_WhileRecording_StopsAndSaves()
        {
            StartRecording(Path.Combine(_outputDirectory, "clip.mp4"), usedDefaultOutputPath: false, stopOnPlayModeExit: false);

            _host.OnBeforeAssemblyReload();

            Assert.That(_host.IsRecording, Is.False);
            Assert.That(LastCompletedRecordingStore.TryRead().Snapshot.StoppedBy, Is.EqualTo(RecordVideoConstants.StoppedByAssemblyReload));
        }

        /// <summary>
        /// What: an Editor quit stops the recording without saving it, because SessionState does not survive a quit.
        /// </summary>
        [Test]
        public void OnEditorQuitting_WhileRecording_StopsWithoutSaving()
        {
            StartRecording(Path.Combine(_outputDirectory, "clip.mp4"), usedDefaultOutputPath: false, stopOnPlayModeExit: false);

            _host.OnEditorQuitting();

            Assert.That(_host.IsRecording, Is.False);
            Assert.That(_encoders[0].DisposeCallCount, Is.EqualTo(1));
            Assert.That(LastCompletedRecordingStore.TryRead().HasValue, Is.False);
        }

        private VideoRecordingSnapshot StartRecording(string outputPath, bool usedDefaultOutputPath, bool stopOnPlayModeExit)
        {
            return _host.Start(
                frameRate: 30,
                maxDurationSeconds: 10,
                outputPath,
                usedDefaultOutputPath,
                width: 4,
                height: 2,
                new FakeGameViewFrameSource(),
                stopOnPlayModeExit,
                RecordVideoQuality.high);
        }

        private IVideoFrameEncoder CreateEncoder(
            string outputPath,
            int width,
            int height,
            int frameRate,
            bool useVp8,
            RecordVideoQuality quality)
        {
            _encoderRequests.Add(new EncoderRequest(outputPath, width, height, frameRate, useVp8, quality));
            FakeVideoFrameEncoder encoder = new(width, height);
            _encoders.Add(encoder);
            return encoder;
        }

        private double ReadClock()
        {
            if (_clockThrows)
            {
                throw new InvalidOperationException("clock failed");
            }

            return _now;
        }

        private static VideoRecordingSnapshot CreateStoppedSnapshot()
        {
            return new VideoRecordingSnapshot(
                "previous.mp4",
                4,
                2,
                30,
                1,
                0,
                1.0,
                RecordVideoConstants.StoppedByMaxDuration,
                false,
                "high");
        }

        private readonly struct EncoderRequest
        {
            public EncoderRequest(
                string outputPath,
                int width,
                int height,
                int frameRate,
                bool useVp8,
                RecordVideoQuality quality)
            {
                OutputPath = outputPath;
                Width = width;
                Height = height;
                FrameRate = frameRate;
                UseVp8 = useVp8;
                Quality = quality;
            }

            public string OutputPath { get; }
            public int Width { get; }
            public int Height { get; }
            public int FrameRate { get; }
            public bool UseVp8 { get; }
            public RecordVideoQuality Quality { get; }
        }

        private sealed class FakeVideoFrameEncoder : IVideoFrameEncoder
        {
            public FakeVideoFrameEncoder(int width, int height)
            {
                Width = width;
                Height = height;
            }

            internal int DisposeCallCount { get; private set; }

            public int Width { get; }

            public int Height { get; }

            public bool AddFrame(Texture2D texture)
            {
                return true;
            }

            public void Dispose()
            {
                DisposeCallCount++;
            }
        }

        private sealed class FakeGameViewFrameSource : IGameViewFrameSource
        {
            public bool IsSourceClosed => false;

            public bool TryReadFrame(Texture2D destination)
            {
                return true;
            }
        }
    }
}
