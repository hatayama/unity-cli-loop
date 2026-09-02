namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// When the injected capture runs relative to the resolved line.
    /// </summary>
    internal enum SourcePausePointSnapshotTiming
    {
        // Before the resolved line executes, like an IDE breakpoint on that line.
        PreLine,
        // After the resolved line's own statement finished, before control leaves it.
        PostLine
    }
}
