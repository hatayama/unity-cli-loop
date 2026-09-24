namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Whether an apply run that leaves edits unapplied falls back to a compile in the same command.
    /// Skipped rows of a sibling pulled in to re-apply its active patches do not count; its Failed
    /// rows do.
    /// Lowercase members: all are single-word CLI-own values (ADR 0006).
    /// </summary>
    public enum HotReloadCompileOnSkip
    {
        auto = 0,
        on = 1,
        off = 2
    }
}
