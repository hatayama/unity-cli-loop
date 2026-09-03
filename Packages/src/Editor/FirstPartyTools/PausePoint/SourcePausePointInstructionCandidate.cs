namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// One IL instruction in body order, used by post-line site selection without Cecil.
    /// </summary>
    internal readonly struct SourcePausePointInstructionCandidate
    {
        public int Offset { get; }
        public SourcePausePointInstructionFlow Flow { get; }

        public SourcePausePointInstructionCandidate(int offset, SourcePausePointInstructionFlow flow)
        {
            Offset = offset;
            Flow = flow;
        }
    }
}
