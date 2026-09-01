using UnityEditor;
using UnityEditor.Media;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Wraps UnityEditor.Media.MediaEncoder as IVideoFrameEncoder.
    /// </summary>
    internal sealed class MediaEncoderVideoFrameEncoder : IVideoFrameEncoder
    {
        private MediaEncoder _encoder;
        private readonly int _width;
        private readonly int _height;
        private bool _disposed;

        internal MediaEncoderVideoFrameEncoder(
            string filePath,
            int width,
            int height,
            int frameRate,
            bool isLinux)
        {
            Debug.Assert(!string.IsNullOrEmpty(filePath), "filePath must not be empty.");
            Debug.Assert(width > 0, "width must be positive.");
            Debug.Assert(height > 0, "height must be positive.");
            Debug.Assert(frameRate > 0, "frameRate must be positive.");

            _width = width;
            _height = height;
            _encoder = new MediaEncoder(filePath, CreateAttributes(width, height, frameRate, isLinux));
        }

        public int Width => _width;

        public int Height => _height;

        public bool AddFrame(Texture2D texture)
        {
            Debug.Assert(!_disposed, "AddFrame must not run after Dispose.");
            return _encoder.AddFrame(texture);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _encoder.Dispose();
            _encoder = null;
        }

        private static VideoTrackEncoderAttributes CreateAttributes(
            int width,
            int height,
            int frameRate,
            bool isLinux)
        {
            VideoTrackEncoderAttributes attributes = isLinux
                ? new VideoTrackEncoderAttributes(new VP8EncoderAttributes
                {
                    keyframeDistance = RecordVideoConstants.Vp8KeyframeDistance
                })
                : new VideoTrackEncoderAttributes(new H264EncoderAttributes
                {
                    gopSize = RecordVideoConstants.H264GopSize,
                    numConsecutiveBFrames = RecordVideoConstants.H264ConsecutiveBFrames,
                    profile = VideoEncodingProfile.H264High
                });

            attributes.frameRate = new MediaRational(frameRate);
            attributes.width = (uint)width;
            attributes.height = (uint)height;
            attributes.includeAlpha = false;
            attributes.bitRateMode = VideoBitrateMode.Medium;
            return attributes;
        }
    }
}
