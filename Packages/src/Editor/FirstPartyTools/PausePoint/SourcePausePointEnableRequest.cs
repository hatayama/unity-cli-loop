namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The original enable request behind a source pause point, kept so hot-reload
    /// auto-retarget can re-resolve with the same file, line, and snapshot timing.
    /// </summary>
    internal readonly struct SourcePausePointEnableRequest
    {
        public string NormalizedFile { get; }
        public int Line { get; }
        public SourcePausePointSnapshotTiming SnapshotTiming { get; }

        public SourcePausePointEnableRequest(
            string normalizedFile,
            int line,
            SourcePausePointSnapshotTiming snapshotTiming)
        {
            NormalizedFile = normalizedFile;
            Line = line;
            SnapshotTiming = snapshotTiming;
        }
    }
}
