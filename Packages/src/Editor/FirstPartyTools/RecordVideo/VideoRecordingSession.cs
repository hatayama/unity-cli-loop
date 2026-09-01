using System;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Owns one recording: pacing, encode/skip counts, and encoder lifetime.
    /// </summary>
    internal sealed class VideoRecordingSession
    {
        private readonly IVideoFrameEncoder _encoder;
        private readonly IGameViewFrameSource _frameSource;
        private readonly Func<double> _clock;
        private readonly int _frameRate;
        private readonly double _maxDurationSeconds;
        private readonly string _outputPath;
        private readonly double _startedAt;
        private Texture2D _frameTexture;
        private int _encodedFrameCount;
        private int _skippedFrameCount;
        private bool _stopped;
        private string _stoppedBy;
        private double _elapsedAtStop;

        internal VideoRecordingSession(
            IVideoFrameEncoder encoder,
            IGameViewFrameSource frameSource,
            Func<double> clock,
            int frameRate,
            double maxDurationSeconds,
            string outputPath)
        {
            Debug.Assert(encoder.Width > 0, "encoder width must be positive.");
            Debug.Assert(encoder.Height > 0, "encoder height must be positive.");

            _encoder = encoder;
            _frameSource = frameSource;
            _clock = clock;
            _frameRate = frameRate;
            _maxDurationSeconds = maxDurationSeconds;
            _outputPath = outputPath;
            _startedAt = clock();
            _frameTexture = new Texture2D(encoder.Width, encoder.Height, TextureFormat.RGBA32, false);
        }

        internal void Tick()
        {
            if (_stopped)
            {
                return;
            }

            double elapsed = _clock() - _startedAt;
            if (elapsed >= _maxDurationSeconds)
            {
                Stop(RecordVideoConstants.StoppedByMaxDuration);
                return;
            }

            int due = VideoRecordingFramePacer.FramesDue(elapsed, _frameRate, _encodedFrameCount);
            if (due == 0)
            {
                return;
            }

            if (!_frameSource.TryReadFrame(_frameTexture))
            {
                _skippedFrameCount += due;
                return;
            }

            for (int i = 0; i < due; i++)
            {
                if (_encoder.AddFrame(_frameTexture))
                {
                    _encodedFrameCount++;
                }
                else
                {
                    _skippedFrameCount++;
                }
            }
        }

        internal void Stop(string reason)
        {
            if (_stopped)
            {
                return;
            }

            _stopped = true;
            _stoppedBy = reason;
            _elapsedAtStop = _clock() - _startedAt;
            _encoder.Dispose();
            UnityEngine.Object.DestroyImmediate(_frameTexture);
            _frameTexture = null;
        }

        internal VideoRecordingSnapshot Snapshot()
        {
            double elapsed = _stopped ? _elapsedAtStop : _clock() - _startedAt;
            return new VideoRecordingSnapshot(
                _outputPath,
                _encoder.Width,
                _encoder.Height,
                _frameRate,
                _encodedFrameCount,
                _skippedFrameCount,
                elapsed,
                _stoppedBy,
                !_stopped);
        }
    }
}
