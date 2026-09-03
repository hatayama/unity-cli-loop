namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// How control leaves one IL instruction, reduced to what post-line site selection needs.
    /// </summary>
    internal enum SourcePausePointInstructionFlow
    {
        Next,
        Branch,
        ConditionalBranch,
        Return,
        Throw
    }
}
