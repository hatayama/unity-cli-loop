using UnityEditor;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Persists the last automatically stopped recording across domain reload.
    /// </summary>
    internal static class LastCompletedRecordingStore
    {
        private const string KeyPrefix = "io.github.hatayama.uloopmcp.recordVideo.lastCompleted.";
        private const string OutputPathKey = KeyPrefix + "outputPath";
        private const string WidthKey = KeyPrefix + "width";
        private const string HeightKey = KeyPrefix + "height";
        private const string FrameRateKey = KeyPrefix + "frameRate";
        private const string EncodedFrameCountKey = KeyPrefix + "encodedFrameCount";
        private const string SkippedFrameCountKey = KeyPrefix + "skippedFrameCount";
        private const string ElapsedSecondsKey = KeyPrefix + "elapsedSeconds";
        private const string StoppedByKey = KeyPrefix + "stoppedBy";
        private const string QualityKey = KeyPrefix + "quality";
        private const string ReportedKey = KeyPrefix + "reported";

        internal static void Save(VideoRecordingSnapshot snapshot)
        {
            Debug.Assert(!string.IsNullOrEmpty(snapshot.OutputPath), "snapshot output path must not be empty.");
            Debug.Assert(!string.IsNullOrEmpty(snapshot.StoppedBy), "snapshot stop reason must not be empty.");

            SessionState.SetString(OutputPathKey, snapshot.OutputPath);
            SessionState.SetInt(WidthKey, snapshot.Width);
            SessionState.SetInt(HeightKey, snapshot.Height);
            SessionState.SetInt(FrameRateKey, snapshot.FrameRate);
            SessionState.SetInt(EncodedFrameCountKey, snapshot.EncodedFrameCount);
            SessionState.SetInt(SkippedFrameCountKey, snapshot.SkippedFrameCount);
            SessionState.SetFloat(ElapsedSecondsKey, (float)snapshot.ElapsedSeconds);
            SessionState.SetString(StoppedByKey, snapshot.StoppedBy);
            SessionState.SetString(QualityKey, snapshot.Quality);
            SessionState.SetInt(ReportedKey, 0);
        }

        internal static LastCompletedRecording TryRead()
        {
            string outputPath = SessionState.GetString(OutputPathKey, string.Empty);
            if (string.IsNullOrEmpty(outputPath))
            {
                return LastCompletedRecording.Empty;
            }

            VideoRecordingSnapshot snapshot = new VideoRecordingSnapshot(
                outputPath,
                SessionState.GetInt(WidthKey, 0),
                SessionState.GetInt(HeightKey, 0),
                SessionState.GetInt(FrameRateKey, 0),
                SessionState.GetInt(EncodedFrameCountKey, 0),
                SessionState.GetInt(SkippedFrameCountKey, 0),
                SessionState.GetFloat(ElapsedSecondsKey, 0f),
                SessionState.GetString(StoppedByKey, string.Empty),
                false,
                SessionState.GetString(QualityKey, string.Empty));
            bool isReported = SessionState.GetInt(ReportedKey, 0) != 0;
            return new LastCompletedRecording(snapshot, isReported);
        }

        internal static void MarkReported()
        {
            SessionState.SetInt(ReportedKey, 1);
        }

        internal static void Clear()
        {
            SessionState.SetString(OutputPathKey, string.Empty);
            SessionState.SetInt(WidthKey, 0);
            SessionState.SetInt(HeightKey, 0);
            SessionState.SetInt(FrameRateKey, 0);
            SessionState.SetInt(EncodedFrameCountKey, 0);
            SessionState.SetInt(SkippedFrameCountKey, 0);
            SessionState.SetFloat(ElapsedSecondsKey, 0f);
            SessionState.SetString(StoppedByKey, string.Empty);
            SessionState.SetString(QualityKey, string.Empty);
            SessionState.SetInt(ReportedKey, 0);
        }
    }

    /// <summary>
    /// SessionState view of the last automatically stopped recording.
    /// </summary>
    internal readonly struct LastCompletedRecording
    {
        internal static LastCompletedRecording Empty => new LastCompletedRecording(default, false);

        internal LastCompletedRecording(VideoRecordingSnapshot snapshot, bool isReported)
        {
            Snapshot = snapshot;
            IsReported = isReported;
            HasValue = !string.IsNullOrEmpty(snapshot.OutputPath);
        }

        internal VideoRecordingSnapshot Snapshot { get; }

        internal bool IsReported { get; }

        internal bool HasValue { get; }
    }
}
