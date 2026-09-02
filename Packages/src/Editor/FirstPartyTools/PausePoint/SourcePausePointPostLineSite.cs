namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Result of post-line site selection: the instruction index to inject before, and the
    /// IL offset whose lexical scope decides which locals are capturable.
    /// </summary>
    internal readonly struct SourcePausePointPostLineSite
    {
        public SourcePausePointPostLineSiteKind Kind { get; }
        public int InstructionIndex { get; }
        // Why a separate scope offset: on fallthrough the injection index is the next
        // statement's first instruction, which may already sit outside the block that
        // declared the locals assigned on the requested line.
        public int ScopeOffset { get; }

        public SourcePausePointPostLineSite(
            SourcePausePointPostLineSiteKind kind,
            int instructionIndex,
            int scopeOffset)
        {
            Kind = kind;
            InstructionIndex = instructionIndex;
            ScopeOffset = scopeOffset;
        }

        public static SourcePausePointPostLineSite AlwaysThrows()
        {
            return new SourcePausePointPostLineSite(SourcePausePointPostLineSiteKind.AlwaysThrows, -1, -1);
        }
    }
}
