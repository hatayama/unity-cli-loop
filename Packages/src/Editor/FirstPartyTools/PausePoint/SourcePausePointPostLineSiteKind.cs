namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Where a post-line capture lands relative to the statement's IL range.
    /// </summary>
    internal enum SourcePausePointPostLineSiteKind
    {
        // The range ends in a branch, conditional branch, or ret: capture right before it.
        BeforeControlTransfer,
        // The range falls through: capture at the first instruction after it, without
        // taking over that instruction's branch labels.
        Fallthrough,
        // The range ends in throw: there is no post-line state to capture.
        AlwaysThrows
    }
}
