namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// One IL instruction in body order, used by post-line site selection without Cecil.
    /// </summary>
    internal readonly struct SourcePausePointInstructionCandidate
    {
        public int Offset { get; }
        public SourcePausePointInstructionFlow Flow { get; }
        public int BranchTargetOffset { get; }

        public SourcePausePointInstructionCandidate(
            int offset,
            SourcePausePointInstructionFlow flow,
            int branchTargetOffset)
        {
            Offset = offset;
            Flow = flow;
            BranchTargetOffset = branchTargetOffset;
        }
    }
}
