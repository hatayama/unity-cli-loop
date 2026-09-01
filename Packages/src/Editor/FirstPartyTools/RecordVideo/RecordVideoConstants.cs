namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Shared messages, stop reasons, and encoder defaults for record-video.
    /// </summary>
    internal static class RecordVideoConstants
    {
        internal const string StoppedByCli = "cli";
        internal const string StoppedByMaxDuration = "max-duration";
        internal const string StoppedByPlayModeExit = "play-mode-exit";
        internal const string StoppedByAssemblyReload = "assembly-reload";

        internal const string AlreadyRecordingMessage =
            "A recording is already in progress. Stop it first.";
        internal const string RenderTextureUnavailableMessage =
            "Play Mode view RenderTexture is not available. Open the Game View and make sure a camera renders.";
        internal const string NoRecordingMessage = "No recording is in progress.";
        internal const string StartedMessage = "Recording started.";
        internal const string StoppedMessage = "Recording stopped.";
        internal const string StatusRecordingMessage = "Recording is in progress.";
        internal const string StatusIdleMessage = "No recording is in progress.";

        internal const string Mp4SearchPattern = "*.mp4";
        internal const string WebmSearchPattern = "*.webm";

        internal const uint H264GopSize = 25;
        internal const uint H264ConsecutiveBFrames = 2;
        internal const uint Vp8KeyframeDistance = 25;
    }
}
