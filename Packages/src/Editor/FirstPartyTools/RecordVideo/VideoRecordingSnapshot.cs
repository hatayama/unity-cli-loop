namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Immutable view of a video recording session at a point in time.
    /// </summary>
    internal readonly struct VideoRecordingSnapshot
    {
        internal VideoRecordingSnapshot(
            string outputPath,
            int width,
            int height,
            int frameRate,
            int encodedFrameCount,
            int skippedFrameCount,
            double elapsedSeconds,
            string stoppedBy,
            bool isRecording,
            string quality)
        {
            OutputPath = outputPath;
            Width = width;
            Height = height;
            FrameRate = frameRate;
            EncodedFrameCount = encodedFrameCount;
            SkippedFrameCount = skippedFrameCount;
            ElapsedSeconds = elapsedSeconds;
            StoppedBy = stoppedBy;
            IsRecording = isRecording;
            Quality = quality;
        }

        internal string OutputPath { get; }

        internal int Width { get; }

        internal int Height { get; }

        internal int FrameRate { get; }

        internal int EncodedFrameCount { get; }

        internal int SkippedFrameCount { get; }

        internal double ElapsedSeconds { get; }

        internal string StoppedBy { get; }

        internal bool IsRecording { get; }

        internal string Quality { get; }
    }
}
