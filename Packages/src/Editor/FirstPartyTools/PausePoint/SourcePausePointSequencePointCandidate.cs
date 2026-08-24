namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// One PDB sequence point used by monotonic IL-order selection.
    /// </summary>
    internal readonly struct SourcePausePointSequencePointCandidate
    {
        public int StartLine { get; }
        public int Offset { get; }
        public bool IsHidden { get; }

        public SourcePausePointSequencePointCandidate(int startLine, int offset, bool isHidden)
        {
            StartLine = startLine;
            Offset = offset;
            IsHidden = isHidden;
        }
    }
}
