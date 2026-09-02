namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Reasons a file:line lookup could not be resolved to a patchable instruction.
    /// </summary>
    internal enum SourcePausePointResolveFailureReason
    {
        None,
        ScriptNotInAnyAssembly,
        CompiledAssemblyNotFound,
        SymbolsUnavailable,
        NoSequencePointOnOrAfterLine,
        PostLineAlwaysThrows
    }
}
