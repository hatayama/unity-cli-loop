namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// One thing the preparation observed about a declaration without refusing the run.
    /// </summary>
    /// <remarks>
    /// Why not a type outcome: two of the worker's diagnostics name no type at all, so a row that
    /// promised a type name per row could not carry them. Why reported anyway: the declaration
    /// stays in the source and keeps not working until a compile, which the reader has to be told.
    /// </remarks>
    internal sealed class HotReloadIntroducedTypeNotice
    {
        internal HotReloadIntroducedTypeNotice(string ownerProjectRelativePath, string text)
        {
            OwnerProjectRelativePath = ownerProjectRelativePath ?? string.Empty;
            Text = text ?? string.Empty;
        }

        internal string OwnerProjectRelativePath { get; }

        internal string Text { get; }
    }
}
