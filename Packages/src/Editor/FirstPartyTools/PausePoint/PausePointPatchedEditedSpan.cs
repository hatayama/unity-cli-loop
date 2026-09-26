namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// A hot-reload patched method and the span its edited body covers in the file on disk.
    /// </summary>
    internal sealed class PausePointPatchedEditedSpan
    {
        internal string Label { get; }
        internal int StartLine { get; }
        internal int EndLine { get; }

        internal PausePointPatchedEditedSpan(string label, int startLine, int endLine)
        {
            Label = label;
            StartLine = startLine;
            EndLine = endLine;
        }
    }
}
