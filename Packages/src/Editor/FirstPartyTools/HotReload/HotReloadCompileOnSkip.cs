namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Whether an apply run that leaves edits in the files passed unapplied falls back to a compile in
    /// the same command. Rows of a sibling pulled in to re-apply its active patches do not count.
    /// Lowercase members: all are single-word CLI-own values (ADR 0006).
    /// </summary>
    public enum HotReloadCompileOnSkip
    {
        auto = 0,
        on = 1,
        off = 2
    }
}
